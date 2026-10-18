using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Obelisk.Abstractions;
using Obelisk.Assets;
using Obelisk.Graphics2D;
using SDL3;

namespace Obelisk.Platform.Sdl3.Tests;

public sealed partial class Sdl3ApplicationPlatformTests
{
    [Theory]
    [InlineData("MovingRectangle")]
    [InlineData("Pong")]
    public void VanillaSampleReferencesOnlyMetaProjectAndPublicContracts(string name)
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "samples", name, $"{name}.csproj"));
        var references = project.Descendants("ProjectReference").Select(e => (string)e.Attribute("Include")!).ToArray();
        Assert.Single(references);
        Assert.EndsWith("Obelisk.Vanilla.csproj", references[0]);
        var sample = Assembly.Load(name);
        Assert.DoesNotContain(sample.GetReferencedAssemblies(), r => r.Name is not null &&
            (r.Name.Contains("SDL", StringComparison.OrdinalIgnoreCase) || r.Name is "Obelisk.Runtime" or "Obelisk.Platform.Sdl3"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "samples", name), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("using SDL3", source);
            Assert.DoesNotContain("using Obelisk.Platform", source);
            Assert.DoesNotContain("using Obelisk.Runtime", source);
        }
    }

    [Theory]
    [InlineData("MovingRectangle")]
    [InlineData("Pong")]
    public void NativeVanillaSampleEntryPointRunsAndExitsNormally(string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        var sample = Assembly.Load(name);
        using var cancel = new CancellationTokenSource();
        Exception? eventError = null;
        var worker = new Thread(() =>
        {
            try
            {
                // Only event injection is on the worker; all game/GPU/resource operations stay on the test thread.
                while (!cancel.IsCancellationRequested && SDL.WasInit(SDL.InitFlags.Video) == 0) Thread.Sleep(10);
                if (cancel.Token.WaitHandle.WaitOne(1500)) return;
                var input = new SDL.Event
                {
                    Key = new SDL.KeyboardEvent
                    { Type = SDL.EventType.KeyDown, Key = name == "Pong" ? SDL.Keycode.Return : SDL.Keycode.Right, Down = true }
                };
                if (!SDL.PushEvent(ref input)) throw new InvalidOperationException(SDL.GetError());
                if (cancel.Token.WaitHandle.WaitOne(300)) return;
                var quit = new SDL.Event { Type = (uint)SDL.EventType.Quit };
                if (!SDL.PushEvent(ref quit)) throw new InvalidOperationException(SDL.GetError());
            }
            catch (Exception error) { eventError = error; }
        })
        { IsBackground = true };
        worker.Start();
        try { Assert.Equal(0, Assert.IsType<int>(sample.EntryPoint!.Invoke(null, null))); }
        finally { cancel.Cancel(); Assert.True(worker.Join(TimeSpan.FromSeconds(5))); }
        Assert.Null(eventError);
        Assert.Equal((SDL.InitFlags)0, SDL.WasInit(SDL.InitFlags.Video | SDL.InitFlags.Audio));
    }

    [Fact]
    public void CanvasMesh_ScaledTextPreservesGlyphSizeAdvanceLineHeightAndUv()
    {
        using var font = new Font(new Texture(new(42), 16, 16, () => { }), 10,
            new Dictionary<Rune, FontGlyph> { [new('A')] = new(new(0, 0, 8, 8), 5) });
        var canvas = new Canvas2D();
        canvas.DrawText(font, "AA\nA", new(4, 6), scale: 2);
        var mesh = CanvasMesh.Create(canvas.Commands, 100, 100, _ => (16, 16));
        var vertices = mesh.Mesh.Vertices;
        Assert.Equal(12, vertices.Length);
        Assert.Equal(-0.92f, vertices[0].X, 5); // x=4
        Assert.Equal(-0.6f, vertices[1].X, 5); // x=4+8*2
        Assert.Equal(-0.72f, vertices[4].X, 5); // next glyph x=4+5*2
        Assert.Equal(0.48f, vertices[8].Y, 5); // next line y=6+10*2
        Assert.Equal(0.5f, vertices[1].U);
        Assert.Equal(font.Atlas.Handle, Assert.Single(mesh.Batches).Texture);
    }

    [Fact]
    public void NativeFontAtlas_RepeatedReleaseAndDeviceShutdownInvalidateSurvivors()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var platform = new NativePlatformScope();
        using var device = TestDevice();
        using var renderer = new CanvasRenderer(device.Handle, SDL.GPUTextureFormat.R8G8B8A8Unorm);
        var loader = new FontLoader(renderer, 24);
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
        for (var i = 0; i < 8; i++)
        {
            var font = loader.LoadFile(path);
            Assert.Equal(2, renderer.TextureCount);
            var canvas = new Canvas2D();
            canvas.DrawText(font, "Vanilla MVP", new(4, 4), scale: 0.5f);
            _ = RenderPixels(renderer, canvas, 128);
            font.Dispose(); font.Dispose();
            Assert.Equal(1, renderer.TextureCount);
        }
        using var survivor = loader.LoadFile(path);
        renderer.Dispose(); renderer.Dispose();
        Assert.Equal(0, renderer.TextureCount);
        Assert.True(survivor.IsDisposed);
        Assert.True(survivor.Atlas.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => survivor.Measure("A"));
        survivor.Dispose();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Obelisk.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("The Vanilla sample boundary test requires the Obelisk repository.");
    }
}
