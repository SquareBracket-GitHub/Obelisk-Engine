using System.Text.Json;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugin;
using Obelisk.Vanilla;
using SDL3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed partial class Sdl3ApplicationPlatformTests
{
    [Fact]
    public void NativeVanillaLoadsSampleModuleAndBundledImageLoaderBeforeNormalShutdown()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "obelisk-native-plugin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var source = Path.Combine(FindRepositoryRoot(), "samples", "SamplePlugin", "bin", configuration, "net10.0");
            var plugins = Path.Combine(root, "plugins");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(plugins, "sample", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            File.WriteAllBytes(Path.Combine(root, "image.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="));
            using var cancel = new CancellationTokenSource();
            Exception? eventError = null;
            var worker = new Thread(() =>
            {
                try
                {
                    while (!cancel.IsCancellationRequested && SDL.WasInit(SDL.InitFlags.Video) == 0) Thread.Sleep(10);
                    if (cancel.Token.WaitHandle.WaitOne(500)) return;
                    var quit = new SDL.Event { Type = (uint)SDL.EventType.Quit };
                    if (!SDL.PushEvent(ref quit)) throw new InvalidOperationException(SDL.GetError());
                }
                catch (Exception error) { eventError = error; }
            }) { IsBackground = true };
            var game = new PluginGame();
            worker.Start();
            try
            {
                Assert.Equal(0, ObeliskApp.Run(game, new AppOptions { Title = "Obelisk Plugin integration" },
                    new PluginOptions { Directory = plugins, ProjectAssetRoot = root }));
            }
            finally { cancel.Cancel(); Assert.True(worker.Join(TimeSpan.FromSeconds(5))); }
            Assert.Null(eventError);
            Assert.True(game.Stopped);
            Assert.NotNull(game.Frame);
            Assert.IsType<CircleCommand>(Assert.Single(game.Frame.Canvas.Commands));
            Assert.Equal((SDL.InitFlags)0, SDL.WasInit(SDL.InitFlags.Video | SDL.InitFlags.Audio));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void VanillaRejectsIncompatiblePluginBeforePlatformAndGameInitialization()
    {
        var root = Path.Combine(Path.GetTempPath(), "obelisk-reject-plugin", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "plugins", "future");
        Directory.CreateDirectory(package);
        try
        {
            var manifest = new PluginManifest
            {
                Name = "future", Version = "1.0.0", EngineMinimum = "0.2.0", EngineMaximumExclusive = "0.3.0",
                Assembly = "missing.dll", EntryPoint = "Missing.Entry",
            };
            File.WriteAllText(Path.Combine(package, PluginManifest.FileName), JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var game = new PluginGame();
            var error = Assert.Throws<PluginException>(() => ObeliskApp.Run(game, plugins: new PluginOptions
                { Directory = Path.Combine(root, "plugins"), ProjectAssetRoot = root }));
            Assert.Contains("future", error.Message);
            Assert.Contains("incompatible", error.Message);
            Assert.Null(game.Frame);
            Assert.False(game.Started);
            if (OperatingSystem.IsWindows()) Assert.Equal((SDL.InitFlags)0, SDL.WasInit(SDL.InitFlags.Video | SDL.InitFlags.Audio));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class PluginGame : IGame
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public RenderContext? Frame { get; private set; }
        public void Initialize(InitializeContext context)
        {
            Started = true;
            Assert.Equal(1, context.Assets.Load<RgbaImage>("project:///image.png").Width);
            Assert.Equal("0.15,0.85,0.45", context.Assets.Load<string>("plugin://sample/accent.txt").Trim());
        }
        public void Update(UpdateContext context) { }
        public void Render(RenderContext context) => Frame = context;
        public void Shutdown(ShutdownContext context) => Stopped = true;
    }
}
