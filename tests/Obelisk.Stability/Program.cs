using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Platform.Sdl3;
using Obelisk.Plugin;
using Obelisk.Plugins.Images;
using Obelisk.Runtime;
using SDL3;

namespace Obelisk.Stability;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (Value(args, "--probe") is { } probe) return NativeProbe.Run(probe, (int)Read(args, "--cycles", 40));
            var quick = args.Contains("--quick");
            var minutes = Read(args, "--minutes", quick ? 0.15 : 30);
            var seconds = Read(args, "--cycle-seconds", quick ? 2 : 60);
            if (!double.IsFinite(minutes) || !double.IsFinite(seconds) || minutes <= 0 || seconds < 1 || (!quick && minutes < 30))
                throw new ArgumentException("Use at least 30 minutes, or explicitly pass --quick for a smoke test.");
            var output = Path.GetFullPath(Value(args, "--output") ?? "artifacts/stability");
            Directory.CreateDirectory(output);
            using var metrics = new StreamWriter(Path.Combine(output, "metrics.jsonl")) { AutoFlush = true };
            var timer = Stopwatch.StartNew();
            var cycles = new List<Measurement>();
            var cycle = 0;
            do
            {
                var sample = cycle % 3;
                var options = new AppOptions { Title = $"Obelisk stability {cycle}: {sample}", Width = 960, Height = 540, Resizable = sample != 2 };
                var platform = new Sdl3ApplicationPlatform(options);
                IGame game = sample switch
                {
                    0 => new MovingRectangle.MovingRectangleGame(),
                    1 => new CanvasShowcase.ShowcaseGame(null, null),
                    _ => new Pong.PongGame(),
                };
                using (var plugins = new PluginHost(Path.Combine(AppContext.BaseDirectory, "assets")))
                {
                    plugins.Add(new PluginManifest
                    {
                        Name = "images",
                        Version = "0.1.0",
                        EngineMinimum = "0.1.0",
                        EngineMaximumExclusive = "0.2.0",
                        Assembly = "Obelisk.Plugins.Images.dll",
                        EntryPoint = typeof(ImagePlugin).FullName!
                    },
                        new ImagePlugin(), AppContext.BaseDirectory);
                    var scenario = new Scenario(game, platform, seconds, sample != 2,
                        () => Write(metrics, Capture("running", cycle, sample, timer.Elapsed.TotalSeconds)));
                    var result = new ApplicationHost(platform).Run(plugins.Wrap(scenario));
                    if (result != 0 || scenario.Frames == 0 || scenario.Transitions != (sample == 2 ? 4 : 8))
                        throw new InvalidOperationException("Sample did not complete its rendering/window scenario.");
                }
                if (GpuResource.LiveCount != 0 || platform.WindowHandle != 0 || SDL.WasInit(SDL.InitFlags.Video) != 0)
                    throw new InvalidOperationException("Native window/GPU ownership survived shutdown.");
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var measurement = Capture("stopped", cycle, sample, timer.Elapsed.TotalSeconds);
                cycles.Add(measurement);
                Write(metrics, measurement);
                Console.WriteLine(JsonSerializer.Serialize(measurement));
                cycle++;
            } while (timer.Elapsed.TotalMinutes < minutes || cycle < 3);

            // Ignore a complete first pass to allow JIT, shader compiler and driver caches to warm.
            var steady = cycles.Skip(3).ToArray();
            if (steady.Length >= 6)
            {
                var start = steady.Take(3).ToArray();
                var end = steady.TakeLast(3).ToArray();
                if (end.Average(x => x.PrivateBytes) - start.Average(x => x.PrivateBytes) > 64L * 1024 * 1024 ||
                    end.Average(x => x.ManagedBytes) - start.Average(x => x.ManagedBytes) > 16L * 1024 * 1024 ||
                    end.Average(x => x.Handles) - start.Average(x => x.Handles) > 16 ||
                    end.Average(x => x.Threads) - start.Average(x => x.Threads) > 8)
                    throw new InvalidOperationException("Post-shutdown resource growth exceeded the documented warm-up tolerances.");
            }
            var summary = new
            {
                Passed = true,
                Quick = quick,
                ElapsedSeconds = timer.Elapsed.TotalSeconds,
                Cycles = cycles.Count,
                FinalGpuHandles = GpuResource.LiveCount,
                ProcessId = Environment.ProcessId,
                PostShutdown = cycles,
                TrendChecked = steady.Length >= 6
            };
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("STABILITY PASSED");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private static double Read(string[] args, string name, double fallback) =>
        Value(args, name) is { } value ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
    private static Measurement Capture(string phase, int cycle, int sample, double seconds)
    {
        using var process = Process.GetCurrentProcess();
        return new(phase, cycle, sample, seconds, process.PrivateMemorySize64, process.WorkingSet64,
            GC.GetTotalMemory(false), process.HandleCount, process.Threads.Count,
            process.TotalProcessorTime.TotalSeconds, GpuResource.LiveCount);
    }
    private static void Write(StreamWriter writer, Measurement value) => writer.WriteLine(JsonSerializer.Serialize(value));
    private sealed record Measurement(string Phase, int Cycle, int Sample, double Seconds, long PrivateBytes,
        long WorkingSet, long ManagedBytes, int Handles, int Threads, double CpuSeconds, long GpuHandles);

    private sealed class Scenario(IGame game, Sdl3ApplicationPlatform platform, double duration, bool resize, Action sample) : IGame
    {
        private readonly Stopwatch _timer = new();
        private int _stage;
        private int _nextSample;
        private long? _gpuBaseline;
        private ITextureDevice? _textures;
        public int Frames { get; private set; }
        public int Transitions { get; private set; }
        public void Initialize(InitializeContext context)
        {
            game.Initialize(context);
            _textures = context.Textures;
            _timer.Start();
        }
        public void Update(UpdateContext context)
        {
            var fraction = _timer.Elapsed.TotalSeconds / duration;
            var window = platform.WindowHandle;
            // Native SDL requests target only this test-owned window, on its owning thread.
            double[] times = [0.15, 0.22, 0.35, 0.44, 0.60, 0.68, 0.80, 0.90];
            if (_stage < times.Length && fraction >= times[_stage])
            {
                var stage = _stage++;
                if (stage is 0 or 4) Check(SDL.MinimizeWindow(window));
                if (stage is 1 or 5) Check(SDL.RestoreWindow(window));
                if (stage is 0 or 1 or 4 or 5)
                {
                    Check(SDL.SyncWindow(window));
                    var minimized = (SDL.GetWindowFlags(window) & SDL.WindowFlags.Minimized) != 0;
                    if (minimized != (stage is 0 or 4)) throw new InvalidOperationException("Window minimize/restore state mismatch.");
                    Transitions++;
                }
                if (resize && stage is 2 or 6)
                {
                    Check(SDL.SetWindowSize(window, stage == 2 ? 800 : 1024, stage == 2 ? 450 : 576));
                    Check(SDL.SyncWindow(window));
                    Transitions++;
                }
                if (resize && stage is 3 or 7)
                {
                    var expected = stage == 3 ? new OutputSize(800, 450) : new OutputSize(1024, 576);
                    if (context.OutputSize != expected) throw new InvalidOperationException($"Resize event mismatch: {context.OutputSize} != {expected}.");
                    Transitions++;
                }
            }
            if (game is Pong.PongGame pong && pong.Match.State != Pong.MatchState.Playing) pong.Match.Start();
            game.Update(context);
            if (_timer.Elapsed.TotalSeconds >= _nextSample)
            {
                _nextSample += 5;
                using (var texture = _textures!.CreateTexture(new RgbaImage(2, 2, Enumerable.Repeat((byte)255, 16).ToArray()))) { }
                if (Frames > 30)
                {
                    _gpuBaseline ??= GpuResource.LiveCount;
                    if (GpuResource.LiveCount != _gpuBaseline) throw new InvalidOperationException("GPU handle ownership grew during rendering.");
                }
                sample();
            }
            if (fraction >= 1 && _stage == times.Length)
            {
                var quit = new SDL.Event { Type = (uint)SDL.EventType.Quit };
                Check(SDL.PushEvent(ref quit));
            }
        }
        public void Render(RenderContext context) { game.Render(context); Frames++; }
        public void Shutdown(ShutdownContext context) => game.Shutdown(context);
        private static void Check(bool result)
        {
            if (!result) throw new InvalidOperationException(SDL.GetError());
        }
    }
}
