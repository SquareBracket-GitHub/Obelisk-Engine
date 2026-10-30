using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugins.Effects;
using Obelisk.Plugins.Text;

namespace PluginShowcase;

public sealed class ShowcaseGame(bool probe = false) : IGame
{
    private FontFace? _face;
    private DynamicFont? _font;
    private BlurEffect? _blur;
    private RenderTarget? _layer;
    private IRenderDevice? _graphics;
    private int _frames;
    private int _radius = 6;
    public float FontSize { get; private set; } = 28;
    public void Initialize(InitializeContext context)
    {
        try
        {
            _graphics = context.Graphics;
            _face = FontFace.LoadFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf"));
            _font = new DynamicFont(context.Textures, _face);
            _blur = new BlurEffect(context.Graphics);
            _layer = context.Graphics.CreateRenderTarget(320, 240);
        }
        catch { Shutdown(new ShutdownContext()); throw; }
    }
    public void Update(UpdateContext context)
    {
        FontSize = Math.Clamp(FontSize + context.Input.Mouse.WheelY * 2, 12, 96);
        if (context.Input.Keyboard.WasPressed(Key.Left)) _radius = Math.Max(0, _radius - 1);
        if (context.Input.Keyboard.WasPressed(Key.Right)) _radius = Math.Min(32, _radius + 1);
        if (probe && ++_frames == 12)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            if (!process.CloseMainWindow()) throw new InvalidOperationException("Cannot close plugin showcase probe.");
        }
        if (probe && _frames > 120) throw new InvalidOperationException("Plugin showcase probe did not stop.");
    }
    public void Render(RenderContext context)
    {
        var frame = context.Time.FrameNumber;
        context.Canvas.RenderTo(_layer!, layer =>
        {
            layer.FillRectangle(new(0, 0, 320, 240), new(0.08f, 0.15f, 0.26f));
            layer.FillCircle(new(250, 130), 48, ColorRgba.Orange);
            _font!.Draw(layer, frame, "동적 텍스트\nZoom 한글 Aa", new(12, 12), FontSize);
        });
        context.Canvas.DrawSprite(_layer!.Texture, new(24, 80, 320, 240));
        _blur!.Draw(context, _layer.Texture, new(390, 80, 320, 240), new BlurSettings { Radius = _radius, Sigma = Math.Max(0.5f, _radius / 3f) });
        _font!.Draw(context, "Text 0.1.0 / Effects 0.1.0", new(24, 16), 22);
        _font.Draw(context, $"Wheel: size {FontSize:0} / Left-Right: blur {_radius}", new(24, 350), 18);
        _font.Draw(context, $"Atlas pages: {_font.AtlasPageCount}, glyphs: {_font.CachedGlyphCount}, display scale: {_graphics!.DisplayScale:0.00}", new(24, 390), 16);
    }
    public void Shutdown(ShutdownContext context)
    {
        _layer?.Dispose(); _blur?.Dispose(); _font?.Dispose(); _face?.Dispose();
        _layer = null; _blur = null; _font = null; _face = null;
        if (probe) Console.WriteLine("OFFICIAL_PLUGINS_SHUTDOWN");
    }
}
