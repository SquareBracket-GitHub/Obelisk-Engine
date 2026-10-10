using System.Numerics;
using Obelisk.Abstractions;
using Obelisk.Assets;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Images;

namespace Pong;

internal sealed class PongGame : IGame, IDisposable
{
    internal PongMatch Match { get; } = new();
    private AssetStore? _assets;
    private Font? _font;
    private Texture? _image;
    private IAudioDevice _audio = SilentAudioDevice.Instance;
    private ISoundPlayback? _music, _effect;
    private Sound? _hit, _score, _win;
    private bool _muted;

    public void Initialize(InitializeContext context)
    {
        try
        {
            _audio = context.Audio;
            if (!_audio.IsAvailable) Console.Error.WriteLine($"Pong continues silently: {_audio.UnavailableReason}");
            _assets = new AssetStore(Path.Combine(AppContext.BaseDirectory, "assets"));
            _assets.RegisterLoader<Sound>(WavLoader.LoadFile);
            _hit = _assets.Load<Sound>("project:///hit.wav");
            _score = _assets.Load<Sound>("project:///score.wav");
            _win = _assets.Load<Sound>("project:///win.wav");
            var music = _assets.Load<Sound>("project:///music.wav");
            using var images = new ImagePlugin();
            _image = context.Textures.CreateTexture(images.Decode(Convert.FromBase64String(File.ReadAllText(
                _assets.Resolve(AssetUri.Parse("project:///checker.png.base64"))))));
            _font = new FontLoader(context.Textures, 24).LoadFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf"));
            _music = _audio.Play(music, loop: true, volume: 0.15f);
            Match.Sound += PlayEffect;
        }
        catch { Dispose(); throw; }
    }
    public void Update(UpdateContext context)
    {
        var keys = context.Input.Keyboard;
        if (keys.WasPressed(Key.Enter) && Match.State != MatchState.Playing) Match.Start();
        if (keys.WasPressed(Key.M))
        {
            _muted = !_muted;
            if (_music is { IsPlaying: true }) _music.Volume = _muted ? 0 : 0.15f;
            if (_effect is { IsPlaying: true }) _effect.Volume = _muted ? 0 : 0.55f;
        }
        Match.Update(MathF.Min(context.Time.Delta, 0.1f),
            (keys.IsDown(Key.S) ? 1 : 0) - (keys.IsDown(Key.W) ? 1 : 0),
            (keys.IsDown(Key.Down) ? 1 : 0) - (keys.IsDown(Key.Up) ? 1 : 0));
    }
    private void PlayEffect(MatchSound sound)
    {
        _effect?.Dispose();
        _effect = _audio.Play(sound switch { MatchSound.Hit => _hit!, MatchSound.Score => _score!, _ => _win! }, volume: _muted ? 0 : 0.55f);
    }
    public void Render(RenderContext context)
    {
        var canvas = context.Canvas;
        var scale = MathF.Min(context.OutputSize.Width / 960f, context.OutputSize.Height / 540f);
        if (scale <= 0) return;
        var origin = new Vector2((context.OutputSize.Width - 960 * scale) / 2, (context.OutputSize.Height - 540 * scale) / 2);
        RectangleF Rect(float x, float y, float w, float h) => new(origin.X + x * scale, origin.Y + y * scale, w * scale, h * scale);
        void Text(string text, float y)
        {
            var x = (960 - _font!.Measure(text).X) / 2;
            canvas.DrawText(_font, text, origin + new Vector2(x, y) * scale, ColorRgba.White, scale);
        }
        canvas.FillRectangle(Rect(0, 0, 960, 540), new(0.04f, 0.07f, 0.12f));
        for (var y = 20; y < 520; y += 30) canvas.FillRectangle(Rect(478, y, 4, 15), new(0.2f, 0.3f, 0.4f));
        canvas.DrawSprite(_image!, Rect(40, Match.LeftY, 14, PongMatch.PaddleHeight), tint: new(0.4f, 0.9f, 1));
        canvas.DrawSprite(_image!, Rect(906, Match.RightY, 14, PongMatch.PaddleHeight), tint: ColorRgba.Orange);
        canvas.FillCircle(origin + Match.Ball * scale, PongMatch.Radius * scale, ColorRgba.White);
        Text($"{Match.LeftScore}     :     {Match.RightScore}", 30);
        Text(_muted ? "M: unmute" : "M: mute", 485);
        if (Match.State != MatchState.Playing)
        {
            canvas.FillRectangle(Rect(150, 140, 660, 260), new(0.05f, 0.1f, 0.16f, 0.96f));
            Text(Match.State == MatchState.Title ? "OBELISK PONG" : Match.LeftScore == PongMatch.WinningScore ? "LEFT PLAYER WINS" : "RIGHT PLAYER WINS", 165);
            Text("Left: W / S     Right: Up / Down", 230);
            Text("First to 5 wins. Escape exits.", 280);
            Text(Match.State == MatchState.Title ? "Press Enter to play" : "Press Enter to restart", 335);
        }
    }
    public void Shutdown(ShutdownContext context) => Dispose();
    public void Dispose()
    {
        Match.Sound -= PlayEffect;
        _effect?.Dispose(); _effect = null;
        _music?.Dispose(); _music = null;
        _font?.Dispose(); _font = null;
        _image?.Dispose(); _image = null;
        _assets?.Dispose(); _assets = null;
    }
}
