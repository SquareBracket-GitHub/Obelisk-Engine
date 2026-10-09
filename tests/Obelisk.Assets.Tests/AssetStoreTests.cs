using Obelisk.Assets;

namespace Obelisk.Assets.Tests;

public sealed class AssetStoreTests
{
    [Theory]
    [InlineData("project:///../secret.txt")]
    [InlineData("project:///%2e%2e/secret.txt")]
    [InlineData("project:///dir%2fsecret.txt")]
    [InlineData("project:///C:/secret.txt")]
    [InlineData("project:///a\\b.txt")]
    [InlineData("file:///secret.txt")]
    [InlineData("plugin:///secret.txt")]
    public void Parse_RejectsUnsafeUri(string value) => Assert.ThrowsAny<Exception>(() => AssetUri.Parse(value));

    [Fact]
    public void Load_CachesByUriAndType_AndDisposesOnce()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "first.txt"), "payload");
        var count = 0;
        var asset = new TestAsset();
        var store = new AssetStore(temp.Path);
        store.RegisterLoader<TestAsset>(_ => { count++; return asset; });
        Assert.Same(asset, store.Load<TestAsset>("project:///first.txt"));
        Assert.Same(asset, store.Load<TestAsset>("project:///first.txt"));
        Assert.Equal(1, count);
        store.Dispose();
        store.Dispose();
        Assert.Equal(1, asset.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => store.Load<TestAsset>("project:///first.txt"));
    }

    [Fact]
    public void Load_ResolvesPluginRoot_AndReportsMissingTarget()
    {
        using var project = new TemporaryDirectory();
        using var plugin = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(plugin.Path, "data.txt"), "plugin-data");
        using var store = new AssetStore(project.Path);
        store.RegisterPlugin("sample", plugin.Path);
        store.RegisterLoader<string>(File.ReadAllText);
        Assert.Equal("plugin-data", store.Load<string>("plugin://sample/data.txt"));
        var missing = Assert.Throws<FileNotFoundException>(() => store.Load<string>("plugin://sample/absent.txt"));
        Assert.Contains("plugin://sample/absent.txt", missing.Message);
        Assert.Contains("absent.txt", missing.FileName);
        Assert.Throws<InvalidOperationException>(() => store.RegisterPlugin("sample", plugin.Path));
        Assert.Throws<InvalidOperationException>(() => store.RegisterLoader<string>(File.ReadAllText));
    }

    [Fact]
    public void Load_WrapsLoaderFailureWithAssetUriAndPath()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "broken.txt"), "broken");
        using var store = new AssetStore(temp.Path);
        store.RegisterLoader<string>(_ => throw new FormatException("invalid data"));
        var error = Assert.Throws<InvalidDataException>(() => store.Load<string>("project:///broken.txt"));
        Assert.Contains("project:///broken.txt", error.Message);
        Assert.Contains("invalid data", error.Message);
        Assert.IsType<FormatException>(error.InnerException);
    }

    private sealed class TestAsset : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "obelisk-assets-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
