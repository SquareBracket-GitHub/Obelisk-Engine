using System.Reflection;
using System.Text.Json;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugin;
using Obelisk.Runtime;

namespace Obelisk.Plugin.Tests;

public sealed class PluginTests
{
    [Fact]
    public void MissingNativeLibraryReportsPluginAndNativeDependency()
    {
        using var files = new PackageFiles();
        var package = files.InstallFixture("native-failure");
        PackageFiles.WriteManifest(package, PackageFiles.Manifest("native-failure") with
        { Assembly = "fixture.dll", EntryPoint = typeof(MissingNativeEntry).FullName! });
        using var host = new PluginHost(files.Root);
        host.Load(package);
        var error = Assert.Throws<PluginException>(() => new ApplicationHost(new FakePlatform()).Run(host.Wrap(new Game())));
        Assert.Contains("native-failure", error.Message);
        Assert.Contains("obelisk-day8-missing-native", error.ToString());
    }

    [Fact]
    public void CustomAssetShaderAndNativeRootsStayInsidePackage()
    {
        using var files = new PackageFiles();
        var paths = new PluginPaths(files.Root, PackageFiles.Manifest("test") with
        { Assets = "data/art", Shaders = "data/gpu", Native = "runtimes/win-x64" });
        Assert.Equal(Path.Combine(files.Root, "data", "art", "accent.txt"), paths.ResolveAsset("accent.txt"));
        Assert.Equal(Path.Combine(files.Root, "data", "gpu", "sprite.spv"), paths.ResolveShader("sprite.spv"));
        Assert.Equal(Path.Combine(files.Root, "runtimes", "win-x64", "decoder.dll"), paths.ResolveNative("decoder.dll"));
    }

    [Fact]
    public void CleanupFailureStillReleasesRemainingAssetsAndPlugins()
    {
        using var files = new PackageFiles();
        Directory.CreateDirectory(Path.Combine(files.Root, "assets"));
        File.WriteAllText(Path.Combine(files.Root, "assets", "a"), "asset");
        File.WriteAllText(Path.Combine(files.Root, "assets", "b"), "asset");
        var events = new List<string>();
        using var host = new PluginHost(files.Root);
        host.Add(PackageFiles.Manifest("first"), new DelegatePlugin(c => c.RegisterAssetLoader<FailingAsset>(
            path => new FailingAsset(Path.GetFileName(path), events)), () => events.Add("first.dispose")), files.Root);
        host.Add(PackageFiles.Manifest("second"), new DelegatePlugin(_ => { },
            () => { events.Add("second.dispose"); throw new InvalidDataException("plugin cleanup refused"); }), files.Root);
        _ = host.Assets.Load<FailingAsset>("plugin://first/a");
        _ = host.Assets.Load<FailingAsset>("plugin://first/b");
        var error = Assert.Throws<AggregateException>(host.Dispose);
        Assert.Contains("asset cleanup refused", error.ToString());
        Assert.Contains("plugin cleanup refused", error.ToString());
        Assert.Equal(["a.dispose", "b.dispose", "second.dispose", "first.dispose"], events);
        host.Dispose();
    }

    [Fact]
    public void ModuleInstanceCannotBeSharedAcrossPluginRegistrations()
    {
        using var files = new PackageFiles();
        using var host = new PluginHost(files.Root);
        var module = new Module([], Environment.CurrentManagedThreadId);
        host.Add(PackageFiles.Manifest("first"), new DelegatePlugin(c => c.RegisterModule(module)), files.Root);
        Assert.Contains("another plugin", Assert.Throws<PluginException>(() => host.Add(PackageFiles.Manifest("second"),
            new DelegatePlugin(c => c.RegisterModule(module)), files.Root)).Message);
    }
    [Fact]
    public void PrivateManagedDependencyLoadsFromItsPackageAndSharesPublicImageType()
    {
        using var files = new PackageFiles();
        var directory = files.InstallImages();
        var png = Path.Combine(files.Root, "image.png");
        File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="));
        using var host = new PluginHost(files.Root);
        host.Load(directory);
        var image = host.Assets.Load<RgbaImage>("project:///image.png");
        Assert.Equal(1, image.Width);
        var contexts = System.Runtime.Loader.AssemblyLoadContext.All.Where(c => c.Assemblies.Any(a => a.GetName().Name == "Obelisk.Plugins.Images"));
        Assert.Contains(contexts, c => c != System.Runtime.Loader.AssemblyLoadContext.Default && c.Assemblies.Any(a => a.GetName().Name == "StbImageSharp"));
    }

    [Fact]
    public void MissingPrivateDependencyIncludesPluginAssetAndDependencyName()
    {
        using var files = new PackageFiles();
        var directory = files.InstallImages();
        File.Delete(Path.Combine(directory, "StbImageSharp.dll"));
        File.WriteAllBytes(Path.Combine(directory, "assets", "image.png"), [1]);
        using var host = new PluginHost(files.Root);
        host.Load(directory);
        var error = Assert.Throws<InvalidDataException>(() => host.Assets.Load<RgbaImage>("plugin://images/image.png"));
        Assert.Contains("plugin://images/image.png", error.Message);
        Assert.Contains("StbImageSharp", error.ToString());
    }
    [Fact]
    public void RealSampleInstallationRunsAssetsAndCanvasAndRemovalStopsDiscovery()
    {
        using var files = new PackageFiles();
        var package = files.InstallSample();
        using (var host = new PluginHost(files.Root))
        {
            host.LoadDirectory(files.Plugins);
            Assert.Equal("sample", Assert.Single(host.Plugins).Name);
            Assert.Equal("0.15,0.85,0.45", host.Assets.Load<string>("plugin://sample/accent.txt").Trim());
            var platform = new FakePlatform();
            Assert.Equal(0, new ApplicationHost(platform).Run(host.Wrap(new Game())));
            var badge = Assert.IsType<CircleCommand>(Assert.Single(platform.Commands));
            Assert.Equal(0.85f, badge.Color.G);
            Assert.NotEqual(10f, badge.Radius); // Update participated before Render.
            Assert.True(platform.Stopped);
            Assert.Throws<ObjectDisposedException>(() => host.Assets.Load<string>("plugin://sample/accent.txt"));
        }
        // Removal applies to a new startup, never to a running plugin.
        Directory.Delete(package, recursive: true);
        using var next = new PluginHost(files.Root);
        next.LoadDirectory(files.Plugins);
        Assert.Empty(next.Plugins);
        var empty = new FakePlatform();
        new ApplicationHost(empty).Run(next.Wrap(new Game()));
        Assert.Empty(empty.Commands);
    }

    [Theory]
    [InlineData("missing-manifest", "manifest")]
    [InlineData("missing-assembly", "assembly is missing")]
    [InlineData("invalid-assembly", "format")]
    [InlineData("missing-entry", "Missing.Entry")]
    [InlineData("invalid-entry", "IObeliskPlugin")]
    [InlineData("constructor-fails", "constructor refused")]
    [InlineData("registration-fails", "registration refused")]
    public void LocalLoadingFailuresIdentifyPackageAndCause(string failure, string reason)
    {
        using var files = new PackageFiles();
        var package = files.InstallFixture("broken");
        var manifest = PackageFiles.Manifest("broken") with { Assembly = "fixture.dll", EntryPoint = typeof(RecordingEntry).FullName! };
        switch (failure)
        {
            case "missing-manifest": File.Delete(Path.Combine(package, PluginManifest.FileName)); break;
            case "missing-assembly": File.Delete(Path.Combine(package, "fixture.dll")); break;
            case "invalid-assembly": File.WriteAllText(Path.Combine(package, "fixture.dll"), "not an assembly"); break;
            case "missing-entry": manifest = manifest with { EntryPoint = "Missing.Entry" }; break;
            case "invalid-entry": manifest = manifest with { EntryPoint = typeof(PluginTests).FullName! }; break;
            case "constructor-fails": manifest = manifest with { EntryPoint = typeof(ConstructorFailureEntry).FullName! }; break;
            case "registration-fails": manifest = manifest with { EntryPoint = typeof(RegistrationFailureEntry).FullName! }; break;
        }
        if (failure != "missing-manifest") PackageFiles.WriteManifest(package, manifest);
        using var host = new PluginHost(files.Root);
        var error = Assert.Throws<PluginException>(() => host.Load(package));
        Assert.Contains("broken", error.Message);
        Assert.Contains(reason, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ObjectDisposedException>(() => host.Wrap(new Game()));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("incompatible")]
    public void DiscoveryPreflightRejectsWholeSetBeforeAnyRegistration(string failure)
    {
        using var files = new PackageFiles();
        var first = files.InstallFixture("first");
        var second = files.InstallFixture("second");
        var invalid = PackageFiles.Manifest(failure == "duplicate" ? "first" : "second") with
        {
            Assembly = "fixture.dll",
            EntryPoint = typeof(RecordingEntry).FullName!,
            EngineMinimum = failure == "incompatible" ? "0.2.0" : "0.1.0",
            EngineMaximumExclusive = "0.3.0",
        };
        PackageFiles.WriteManifest(second, invalid);
        using var host = new PluginHost(files.Root);
        var error = Assert.Throws<PluginException>(() => host.LoadDirectory(files.Plugins));
        Assert.Contains(failure == "duplicate" ? "Duplicate plugin 'first'" : "Engine 0.1.0 is incompatible", error.Message);
        Assert.False(File.Exists(Path.Combine(first, "assets", "registered.txt")));
    }

    [Theory]
    [InlineData("{", "JSON")]
    [InlineData("{}", "required")]
    [InlineData("[]", "object")]
    public void InvalidManifestHasActionableDiagnostics(string json, string reason)
    {
        using var files = new PackageFiles();
        var path = Path.Combine(files.Root, PluginManifest.FileName);
        File.WriteAllText(path, json);
        var error = Assert.Throws<PluginException>(() => PluginManifest.Read(path));
        Assert.Contains(reason, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("version", "1.0", "three-part")]
    [InlineData("schemaVersion", "2", "schema")]
    [InlineData("name", "INVALID", "lowercase")]
    [InlineData("engineMinimum", "0.2.0", "minimum")]
    [InlineData("engineMaximumExclusive", "0.1.0", "minimum")]
    public void ManifestValidationRejectsInvalidFields(string field, string value, string reason)
    {
        using var files = new PackageFiles();
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(PackageFiles.Serialize(PackageFiles.Manifest("test")))!;
        values[field] = JsonSerializer.SerializeToElement(field == "schemaVersion" ? (object)int.Parse(value) : value);
        var path = Path.Combine(files.Root, PluginManifest.FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(values));
        Assert.Contains(reason, Assert.Throws<PluginException>(() => PluginManifest.Read(path)).Message);
    }

    [Theory]
    [InlineData("duplicate-property")]
    [InlineData("unknown-property")]
    [InlineData("too-large")]
    public void ManifestRejectsAmbiguityAndUnboundedInput(string failure)
    {
        using var files = new PackageFiles();
        var json = PackageFiles.Serialize(PackageFiles.Manifest("test"));
        json = failure switch
        {
            "duplicate-property" => json.Insert(1, "\"name\":\"other\","),
            "unknown-property" => json.Insert(1, "\"unknown\":true,"),
            _ => new string(' ', 65537),
        };
        var path = Path.Combine(files.Root, PluginManifest.FileName);
        File.WriteAllText(path, json);
        Assert.Throws<PluginException>(() => PluginManifest.Read(path));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:\\escape")]
    [InlineData("assets//file")]
    [InlineData("assets/./file")]
    [InlineData("file:stream")]
    public void EveryPluginPathRejectsEscape(string path)
    {
        using var files = new PackageFiles();
        var paths = new PluginPaths(files.Root, PackageFiles.Manifest("test"));
        Assert.Throws<InvalidDataException>(() => paths.ResolveAsset(path));
        Assert.Throws<InvalidDataException>(() => paths.ResolveShader(path));
        Assert.Throws<InvalidDataException>(() => paths.ResolveNative(path));
        Assert.Throws<InvalidDataException>(() => new PluginPaths(files.Root, PackageFiles.Manifest("test") with { Assembly = path }));
    }

    [Fact]
    public void PluginLoadersAreScopedAndCacheOwnedBeforePluginDisposal()
    {
        using var files = new PackageFiles();
        Directory.CreateDirectory(Path.Combine(files.Root, "assets"));
        File.WriteAllText(Path.Combine(files.Root, "assets", "same.txt"), "source");
        var events = new List<string>();
        using var host = new PluginHost(files.Root);
        var a = new DelegatePlugin(c => c.RegisterAssetLoader<OwnedAsset>(_ => new OwnedAsset("a", events)), () => events.Add("plugin.a.dispose"));
        var b = new DelegatePlugin(c => c.RegisterAssetLoader<OwnedAsset>(_ => new OwnedAsset("b", events)), () => events.Add("plugin.b.dispose"));
        host.Add(PackageFiles.Manifest("a"), a, files.Root);
        host.Add(PackageFiles.Manifest("b"), b, files.Root);
        var first = host.Assets.Load<OwnedAsset>("plugin://a/same.txt");
        Assert.Same(first, host.Assets.Load<OwnedAsset>("plugin://a/same.txt"));
        Assert.Equal("b", host.Assets.Load<OwnedAsset>("plugin://b/same.txt").Name);
        Assert.Throws<InvalidOperationException>(() => host.Assets.Load<OwnedAsset>("project:///assets/same.txt"));
        host.Dispose(); host.Dispose();
        Assert.Equal(["asset.a.dispose", "asset.b.dispose", "plugin.b.dispose", "plugin.a.dispose"], events);
    }

    [Fact]
    public void LifecycleOrderThreadAndBlendArePreserved()
    {
        using var files = new PackageFiles();
        var events = new List<string>();
        var owner = Environment.CurrentManagedThreadId;
        using var host = new PluginHost(files.Root);
        PluginRegistrationContext? captured = null;
        host.Add(PackageFiles.Manifest("test"), new DelegatePlugin(c =>
        {
            captured = c;
            c.RegisterModule(new Module(events, owner));
        }, () => events.Add("plugin.dispose")), files.Root);
        Assert.Throws<InvalidOperationException>(() => captured!.RegisterModule(new Module(events, owner)));
        var wrapped = host.Wrap(new Game(events));
        Assert.Throws<InvalidOperationException>(() => host.Wrap(new Game()));
        var platform = new FakePlatform(events);
        new ApplicationHost(platform).Run(wrapped);
        Assert.Equal(["platform.initialize", "module.initialize", "game.initialize", "module.update", "game.update",
            "game.render", "module.render", "platform.present", "game.shutdown", "module.shutdown", "plugin.dispose", "platform.shutdown"], events);
        Assert.Equal(BlendMode.Alpha, platform.Blend);
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("update")]
    [InlineData("render")]
    [InlineData("shutdown")]
    public void ModuleFailureAlwaysShutsDownPlatformAndOtherOwnedResources(string failure)
    {
        using var files = new PackageFiles();
        var events = new List<string>();
        using var host = new PluginHost(files.Root);
        host.Add(PackageFiles.Manifest("broken"), new DelegatePlugin(c => c.RegisterModule(new Module(events, Environment.CurrentManagedThreadId, failure)),
            () => events.Add("plugin.dispose")), files.Root);
        var platform = new FakePlatform(events);
        var app = new ApplicationHost(platform);
        var error = Assert.ThrowsAny<Exception>(() => app.Run(host.Wrap(new Game(events))));
        Assert.Contains("Plugin 'broken'", error.ToString());
        Assert.Contains($"module {failure}", error.ToString());
        Assert.Contains("module.shutdown", events);
        Assert.Contains("plugin.dispose", events);
        Assert.Equal("platform.shutdown", events[^1]);
        Assert.Equal(ApplicationState.Stopped, app.State);
    }

    [Fact]
    public void FailedRegistrationDisposesEarlierPluginsAndRejectsFurtherUse()
    {
        using var files = new PackageFiles();
        var events = new List<string>();
        using var host = new PluginHost(files.Root);
        host.Add(PackageFiles.Manifest("first"), new DelegatePlugin(_ => { }, () => events.Add("first")), files.Root);
        Assert.Throws<PluginException>(() => host.Add(PackageFiles.Manifest("second"),
            new DelegatePlugin(_ => throw new InvalidDataException("refused"), () => events.Add("second")), files.Root));
        Assert.Equal(["second", "first"], events);
        Assert.Throws<ObjectDisposedException>(() => host.Load(files.Root));
    }

    [Fact]
    public void DuplicateModuleAndDuplicateBuiltInNameAreRejected()
    {
        using var files = new PackageFiles();
        using var host = new PluginHost(files.Root);
        var module = new Module([], Environment.CurrentManagedThreadId);
        Assert.Contains("already registered", Assert.Throws<PluginException>(() => host.Add(PackageFiles.Manifest("test"),
            new DelegatePlugin(c => { c.RegisterModule(module); c.RegisterModule(module); }), files.Root)).Message);
        using var next = new PluginHost(files.Root);
        next.Add(PackageFiles.Manifest("images"), new DelegatePlugin(_ => { }), files.Root);
        Assert.Contains("Duplicate plugin 'images'", Assert.Throws<PluginException>(() => next.Add(PackageFiles.Manifest("images"), new DelegatePlugin(_ => { }), files.Root)).Message);
    }

    [Fact]
    public void OptionalDirectoryMissingIsEmptyAndCrossThreadMutationIsRejected()
    {
        using var files = new PackageFiles();
        using var host = new PluginHost(files.Root);
        host.LoadDirectory(Path.Combine(files.Root, "absent"));
        Assert.Empty(host.Plugins);
        Exception? error = null;
        var worker = new Thread(() => { try { host.Load(files.Root); } catch (Exception caught) { error = caught; } });
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(error);
    }

    private sealed class OwnedAsset(string name, List<string> events) : IDisposable
    {
        public string Name => name;
        public void Dispose() => events.Add($"asset.{name}.dispose");
    }
    private sealed class FailingAsset(string name, List<string> events) : IDisposable
    {
        public void Dispose()
        {
            events.Add($"{name}.dispose");
            if (name == "a") throw new InvalidDataException("asset cleanup refused");
        }
    }
    private sealed class DelegatePlugin(Action<PluginRegistrationContext> register, Action? dispose = null) : IObeliskPlugin
    {
        public void Register(PluginRegistrationContext context) => register(context);
        public void Dispose() => dispose?.Invoke();
    }
    private sealed class Module(List<string> events, int owner, string? failure = null) : IObeliskModule
    {
        private void Step(string operation)
        {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            events.Add($"module.{operation}");
            if (failure == operation) throw new InvalidDataException("module refused");
        }
        public void Initialize(InitializeContext context) => Step("initialize");
        public void Update(UpdateContext context) => Step("update");
        public void Render(RenderContext context) { context.Canvas.Blend = BlendMode.Opaque; Step("render"); }
        public void Shutdown(ShutdownContext context) => Step("shutdown");
    }
    private sealed class Game(List<string>? events = null) : IGame
    {
        public void Initialize(InitializeContext context) => events?.Add("game.initialize");
        public void Update(UpdateContext context) => events?.Add("game.update");
        public void Render(RenderContext context) => events?.Add("game.render");
        public void Shutdown(ShutdownContext context) => events?.Add("game.shutdown");
    }
    private sealed class FakePlatform(List<string>? events = null) : IApplicationPlatform
    {
        private int _frames;
        public InputSnapshot Input => InputSnapshot.Empty;
        public OutputSize OutputSize => new(100, 100);
        public IReadOnlyList<RenderCommand2D> Commands { get; private set; } = [];
        public BlendMode Blend { get; private set; }
        public bool Stopped { get; private set; }
        public void Initialize() => events?.Add("platform.initialize");
        public bool PumpEvents() { Thread.Sleep(1); return _frames++ == 0; }
        public void Present(RenderContext context) { Commands = context.Canvas.Commands; Blend = context.Canvas.Blend; events?.Add("platform.present"); }
        public void Shutdown() { Stopped = true; events?.Add("platform.shutdown"); }
    }
}

public sealed class RecordingEntry : IObeliskPlugin
{
    public void Register(PluginRegistrationContext context) => File.WriteAllText(context.Paths.ResolveAsset("registered.txt"), "registered");
}
public sealed class ConstructorFailureEntry : IObeliskPlugin
{
    public ConstructorFailureEntry() => throw new InvalidDataException("constructor refused");
    public void Register(PluginRegistrationContext context) { }
}
public sealed class RegistrationFailureEntry : IObeliskPlugin
{
    public void Register(PluginRegistrationContext context) => throw new InvalidDataException("registration refused");
}
public sealed class MissingNativeEntry : IObeliskPlugin
{
    public void Register(PluginRegistrationContext context) => context.RegisterModule(new NativeModule());
    private sealed class NativeModule : IObeliskModule
    {
        [System.Runtime.InteropServices.DllImport("obelisk-day8-missing-native")]
        private static extern int Probe();
        public void Initialize(InitializeContext context) => _ = Probe();
    }
}

internal sealed class PackageFiles : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "obelisk-plugin-tests", Guid.NewGuid().ToString("N"));
    public string Plugins => Path.Combine(Root, "plugins");
    public PackageFiles() => Directory.CreateDirectory(Plugins);
    public string InstallFixture(string name)
    {
        var directory = Path.Combine(Plugins, name);
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.Copy(typeof(RecordingEntry).Assembly.Location, Path.Combine(directory, "fixture.dll"));
        WriteManifest(directory, Manifest(name) with { Assembly = "fixture.dll", EntryPoint = typeof(RecordingEntry).FullName! });
        return directory;
    }
    public string InstallSample()
        => InstallBuild("samples", "SamplePlugin", "sample");
    public string InstallImages()
    {
        var directory = InstallBuild("src", "Obelisk.Plugins.Images", "images");
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        WriteManifest(directory, Manifest("images") with { Assembly = "Obelisk.Plugins.Images.dll", EntryPoint = "Obelisk.Plugins.Images.ImagePlugin" });
        return directory;
    }
    private string InstallBuild(string category, string project, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Obelisk.sln"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("SamplePlugin build needs repository.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var source = Path.Combine(root.FullName, category, project, "bin", configuration, "net10.0");
        var target = Path.Combine(Plugins, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return target;
    }
    public static PluginManifest Manifest(string name) => new()
    {
        Name = name,
        Version = "0.1.0",
        EngineMinimum = "0.1.0",
        EngineMaximumExclusive = "0.2.0",
        Assembly = "SamplePlugin.dll",
        EntryPoint = "SamplePlugin.PulsePlugin",
    };
    public static string Serialize(PluginManifest manifest) => JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    public static void WriteManifest(string directory, PluginManifest manifest) => File.WriteAllText(Path.Combine(directory, PluginManifest.FileName), Serialize(manifest));
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
