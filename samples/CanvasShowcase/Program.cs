using System.Numerics;
using Obelisk.Abstractions;
using Obelisk.Assets;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Images;
using Obelisk.Vanilla;

namespace CanvasShowcase;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string? file = null;
            Uri? url = null;
            if (args.Length == 2 && args[0] == "--image") file = args[1];
            else if (args.Length == 2 && args[0] == "--url") url = new Uri(args[1], UriKind.Absolute);
            else if (args.Length != 0) throw new ArgumentException("Usage: CanvasShowcase [--image path | --url https://...]");
            using var game = new ShowcaseGame(file, url);
            return ObeliskApp.Run(game, new AppOptions { Title = "Obelisk Canvas — Sprite / Line / Circle", Width = 960, Height = 540, Resizable = true });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}

internal sealed class ShowcaseGame(string? file, Uri? url) : IGame, IDisposable
{
    private readonly ImagePlugin _images = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Texture? _texture;
    private Font? _font;
    private AssetStore? _assets;
    private ITextureDevice? _device;
    private Task<RgbaImage>? _pending;
    private float _angle;

    public void Initialize(InitializeContext context)
    {
        try
        {
            _device = context.Textures;
            _assets = new AssetStore(Path.Combine(AppContext.BaseDirectory, "assets"));
            _assets.RegisterLoader<RgbaImage>(path => _images.Decode(Convert.FromBase64String(File.ReadAllText(path))));
            _texture = file is not null ? _images.LoadTexture(_device, file) :
                _device.CreateTexture(_assets.Load<RgbaImage>("project:///checker.png.base64"));
            var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
            _font = new FontLoader(_device, 28, "안녕하세요 오벨리스크 엔진").LoadFile(fontPath);
            if (url is not null)
            {
                _pending = _images.LoadUrlAsync(url, _cancellation.Token);
                // Observe faults even if the user closes the window before the request completes.
                _ = _pending.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Update(UpdateContext context)
    {
        _angle += context.Time.Delta;
        if (_pending is { IsCompleted: true })
        {
            try
            {
                var loaded = _device!.CreateTexture(_pending.GetAwaiter().GetResult());
                _texture?.Dispose();
                _texture = loaded;
            }
            catch (Exception error) { Console.Error.WriteLine($"Image URL import failed: {error.Message}"); }
            _pending = null;
        }
    }

    public void Render(RenderContext context)
    {
        var canvas = context.Canvas;
        canvas.FillRectangle(new(25, 25, 910, 490), new(0.08f, 0.12f, 0.18f));
        canvas.DrawSprite(_texture!, new(60, 70, 160, 160));
        canvas.DrawSprite(_texture!, new(280, 70, 160, 160), rotation: _angle, origin: new Vector2(80, 80));
        canvas.DrawSprite(_texture!, new(510, 70, 160, 160), tint: new ColorRgba(0.5f, 1, 1, 0.6f));
        canvas.DrawText(_font!, "Hello, Obelisk!", new(60, 235), ColorRgba.White);
        canvas.DrawText(_font!, "안녕하세요 오벨리스크 엔진", new(60, 280), new(1, 0.85f, 0.35f));
        canvas.FillRectangle(new(70, 320, 140, 100), ColorRgba.Orange);
        canvas.FillRectangle(new(140, 360, 140, 100), new(0.1f, 0.7f, 1, 0.5f));
        canvas.DrawLine(new(320, 350), new(470, 445), 12, new(0.3f, 1, 0.6f));
        canvas.FillCircle(new(610, 390), 65, new(0.8f, 0.3f, 1));
        canvas.FillCircle(new(655, 405), 45, new(1, 0.7f, 0.2f, 0.6f));
    }

    public void Shutdown(ShutdownContext context) => Dispose();

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _cancellation.Cancel();
        _texture?.Dispose();
        _font?.Dispose();
        _assets?.Dispose();
        _images.Dispose();
        _cancellation.Dispose();
        _disposed = true;
    }
}
