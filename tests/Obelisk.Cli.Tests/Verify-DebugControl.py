"""Standalone native CLI integration gate. Requires a desktop GPU and the pinned SDK on PATH.
Run: python3 tests/Obelisk.Cli.Tests/Verify-DebugControl.py [--dotnet /path/to/dotnet]
Opens one test-owned game window; output and logs remain in a printed temporary directory.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import struct
import subprocess
import tempfile
import time
import uuid
import zlib

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--configuration', default='Debug', choices=['Debug','Release'])
parser.add_argument('--cli', help='Installed obelisk executable; default uses the source Debug CLI')
parser.add_argument('--plugin-removal', action='store_true', help='Regress installed plugin removal before the main debug gate')
parser.add_argument('--dev', action='store_true', help='Also verify development restart and fresh session state')
parser.add_argument('--feed', help='Local package feed; default uses source ProjectReference')
options = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
cli = repo / ('src/Obelisk.Cli/bin/' + options.configuration + '/net10.0/Obelisk.Cli.dll')
root = Path(tempfile.mkdtemp(prefix='obelisk-debug-gate-')).resolve()
session = 'gate_' + uuid.uuid4().hex
print('Debug integration artifacts:', root, flush=True)
env = os.environ.copy()
exe = str(Path(options.dotnet).resolve()) if '/' in options.dotnet else options.dotnet
if '/' in exe:
    env['PATH'] = str(Path(exe).parent) + os.pathsep + env['PATH']
    env['DOTNET_ROOT'] = str(Path(exe).parent)
command = [str(Path(options.cli).resolve())] if options.cli else [exe, str(cli)]
if options.feed: env['NUGET_PACKAGES'] = str(root / 'packages')

def invoke(args, cwd=root, expected=0):
    result = subprocess.run(command + args, cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=40)
    assert result.returncode == expected, (args, result.returncode, result.stdout)
    return result.stdout

invoke(['new', 'DebugGame'])
game = root / 'DebugGame'
if not options.feed: (game / 'DebugGame.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup><ItemGroup><Content Include="assets/**/*;plugins/**/*" CopyToOutputDirectory="PreserveNewest" /> <ProjectReference Include="''' + str(repo / 'src/Obelisk.Vanilla/Obelisk.Vanilla.csproj') + '''" /></ItemGroup><Import Project="''' + str(repo / 'src/Obelisk.Vanilla/buildTransitive/Obelisk.Vanilla.targets') + '''" /></Project>''')
if options.feed:
    (game / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="candidate" value="' + str(Path(options.feed).resolve()) + '"/><add key="public" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
(game / 'Game.cs').write_text('''using Obelisk.Abstractions;
using Obelisk.Graphics2D;
namespace DebugGame;
public sealed class Game : IGame {
 private RenderTarget _target = null!;
 public void Initialize(InitializeContext context) { _target = ((IRenderDevice)context.Textures).CreateRenderTarget(32,32); }
 public void Update(UpdateContext context) {
  if (context.Input.Mouse.WasPressed(MouseButton.Left)) Console.WriteLine($"CLICK_DOWN:{context.Input.Mouse.X},{context.Input.Mouse.Y}");
  if (context.Input.Mouse.WasReleased(MouseButton.Left)) Console.WriteLine("CLICK_UP");
  if (context.Input.Keyboard.WasPressed(Key.W)) Console.WriteLine("W_DOWN");
  if (context.Input.Keyboard.WasReleased(Key.W)) Console.WriteLine("W_UP");
 }
 public void Render(RenderContext context) {
  context.Canvas.RenderTo(_target, child => child.FillRectangle(new(0,0,32,32),new ColorRgba(0,0,1,1)));
  context.Canvas.DrawSprite(_target.Texture,new(100,100,32,32));
  context.Canvas.FillRectangle(new(10,10,40,40),ColorRgba.Orange);
 }
 public void Shutdown(ShutdownContext context) { _target.Dispose(); Console.WriteLine("DEBUG_GAME_SHUTDOWN"); }
}''')
program = game / 'Program.cs'
program.write_text(program.read_text().replace('Height = 540', 'Height = 540, SampleCount = 4'))
if options.plugin_removal:
    package = repo / ('samples/SamplePlugin/bin/' + options.configuration + '/net10.0')
    invoke(['add', str(package)], game)
    warm_log = root / 'plugin-installed.log'
    with warm_log.open('w') as out:
        warm = subprocess.Popen(command + ['run','--debug-control','--session',session], cwd=game,
                                env=env, stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            deadline = time.monotonic() + 90
            while 'Game started' not in warm_log.read_text():
                assert warm.poll() is None and time.monotonic() < deadline, warm_log.read_text()
                time.sleep(.1)
            invoke(['debug','--session',session,'status'],game)
            invoke(['debug','--session',session,'quit'],game)
            assert warm.wait(timeout=15) == 0, warm_log.read_text()
        finally:
            if warm.poll() is None:
                if os.name == 'nt': warm.terminate()
                else: os.killpg(warm.pid,signal.SIGTERM)
                warm.wait(timeout=10)
    invoke(['remove','sample'],game)

log_path = root / 'game.log'
with log_path.open('w') as log:
    process = subprocess.Popen(command + ['dev' if options.dev else 'run', '--debug-control', '--session', session], cwd=game,
                               env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True,
                               creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == 'nt' else 0)
    def debug(*args, expected=0):
        output = invoke(['debug', '--session', session, *args], game, expected)
        if expected == 0 and args[0] != 'screenshot':
            result = json.loads(output)
            assert result['ok'] is True, result
            return result
        return output
    try:
        deadline = time.monotonic() + 60
        while 'Game started' not in log_path.read_text():
            assert process.poll() is None, log_path.read_text()
            assert time.monotonic() < deadline, log_path.read_text()
            time.sleep(.1)
        # The game subprocess may still be compiling/loading its native backend.
        while True:
            try:
                state = debug('status')
                break
            except AssertionError:
                assert process.poll() is None and time.monotonic() < deadline, log_path.read_text()
                time.sleep(.1)
        if options.plugin_removal:
            assert not (game / 'bin/Debug/net10.0/plugins/sample').exists(), 'Stale empty plugin directory survived rebuild'
        paused = debug('pause')
        baseline = paused['completedFrames']
        time.sleep(.1)
        assert debug('status')['completedFrames'] == baseline
        stepped = debug('step', '3')
        assert stepped['completedFrames'] == baseline + 3
        assert abs(stepped['elapsedSeconds'] - paused['elapsedSeconds'] - .05) < 1e-5
        clicked = debug('click', '80', '90')
        assert clicked['completedFrames'] == baseline + 5
        assert clicked['mouse'] == {'x':80,'y':90,'buttonsDown':[]}
        down = debug('key', 'down', 'W'); assert down['keysDown'] == ['W']
        held = debug('step', '2'); assert held['keysDown'] == ['W']
        up = debug('key', 'up', 'W'); assert up['keysDown'] == []
        debug('key', 'press', 'W')
        moved = debug('mouse', 'move', '100', '110'); assert moved['mouse']['x'] == 100
        before = moved['completedFrames']
        for args in [('click','NaN','0'), ('click','-1','0'), ('step','0'), ('key','down','Bogus'), ('unknown',)]:
            debug(*args, expected=1)
        assert debug('status')['completedFrames'] == before
        png = root / 'frame.png'; debug('screenshot', str(png))
        assert debug('status')['completedFrames'] == before
        original = png.read_bytes(); debug('screenshot', str(png), expected=1); assert png.read_bytes() == original
        assert original[:8] == b'\x89PNG\r\n\x1a\n'
        chunks = {}; offset = 8
        while offset < len(original):
            length = struct.unpack_from('>I', original, offset)[0]
            kind = original[offset+4:offset+8]; data = original[offset+8:offset+8+length]
            assert zlib.crc32(kind+data) == struct.unpack_from('>I', original, offset+8+length)[0]
            chunks[kind] = chunks.get(kind,b'') + data; offset += length + 12
        width,height = struct.unpack_from('>II',chunks[b'IHDR']); assert (width,height) == (moved['width'],moved['height'])
        pixels = zlib.decompress(chunks[b'IDAT']); stride = width*4+1
        assert all(pixels[y*stride] == 0 for y in range(height))
        orange = pixels[20*stride+1+20*4:20*stride+1+20*4+4]
        assert orange[0] > 200 and 50 < orange[1] < 200 and orange[2] < 60 and orange[3] == 255, orange
        blue = pixels[110*stride+1+110*4:110*stride+1+110*4+4]
        assert blue == bytes([0,0,255,255]), blue
        resumed = debug('resume'); assert resumed['paused'] is False
        time.sleep(.1); assert debug('pause')['completedFrames'] > before
        if options.dev:
            old_pid = debug('status')['processId']
            source = game / 'Game.cs'; source.write_text(source.read_text() + '\n// Trigger development restart.\n')
            deadline = time.monotonic() + 60
            while log_path.read_text().count('Game started') < 2:
                assert process.poll() is None and time.monotonic() < deadline, log_path.read_text()
                time.sleep(.1)
            restarted = debug('status')
            assert restarted['processId'] != old_pid and restarted['paused'] is False, restarted
        debug('quit')
        if options.dev:
            deadline = time.monotonic() + 15
            while 'Game exited with code 0. Waiting for changes.' not in log_path.read_text():
                assert time.monotonic() < deadline, log_path.read_text()
                time.sleep(.1)
            process.send_signal(signal.CTRL_BREAK_EVENT if os.name == 'nt' else signal.SIGINT)
            assert process.wait(timeout=15) == 130, log_path.read_text()
        else:
            assert process.wait(timeout=15) == 0, log_path.read_text()
        text = log_path.read_text()
        for marker in ['CLICK_DOWN:80,90', 'CLICK_UP', 'W_DOWN', 'W_UP', 'DEBUG_GAME_SHUTDOWN']:
            assert marker in text, (marker,text)
        # A terminated session must not remain reachable.
        debug('status', expected=1)
        print('DEBUG CONTROL PASSED: pause/step/input/acknowledgment/PNG pixels/resume/quit/session cleanup', flush=True)
    finally:
        if process.poll() is None:
            if os.name == 'nt': process.terminate()
            else: os.killpg(process.pid,signal.SIGTERM)
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                if os.name == 'nt': process.kill()
                else: os.killpg(process.pid,signal.SIGKILL)
                process.wait()
