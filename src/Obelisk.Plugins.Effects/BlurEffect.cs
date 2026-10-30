using System.Numerics;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;

namespace Obelisk.Plugins.Effects;

/// <summary>Owns a Gaussian material and two reusable targets. Use one instance per differently sized layer in a frame.</summary>
public sealed class BlurEffect : IDisposable
{
    private const string Shader = """
        float4 Shade(float4 color, float2 uv) {
            float4 sum = 0; float weights = 0;
            int radius = (int)Parameters.z;
            [loop] for (int i=-32; i<=32; ++i) {
                if (abs(i)>radius) continue;
                float weight=exp(-0.5*(i/Parameters.w)*(i/Parameters.w));
                float4 value=SampleImage(uv+Parameters.xy*i);
                sum.rgb += value.rgb*value.a*weight;
                sum.a += value.a*weight; weights += weight;
            }
            float alpha=sum.a/weights;
            return float4(sum.a>0.000001 ? sum.rgb/sum.a : float3(0,0,0), alpha)*color;
        }
        """;
    private readonly IRenderDevice _device;
    private readonly Material _material;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private RenderTarget? _scratch;
    private RenderTarget? _output;
    private ulong? _frame;
    /// <summary>Compiles the effect material on the device's owning thread. Targets are created on first nonzero application.</summary>
    public BlurEffect(IRenderDevice device)
    { ArgumentNullException.ThrowIfNull(device); _device = device; _material = device.CreateMaterial(Shader); }
    /// <summary>Gets whether this effect has been disposed.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Queues horizontal/vertical passes and returns a borrowed result, or the original source at radius zero. Do not dispose or retain the result across resize/disposal.</summary>
    public Texture Apply(RenderContext context, Texture source, BlurSettings? settings = null)
    {
        EnsureAlive(); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);
        settings ??= new BlurSettings(); settings.Validate();
        var frame = context.Time.FrameNumber;
        if (_frame is { } previous && frame < previous) throw new InvalidOperationException("Frame numbers must not move backwards.");
        if (settings.Radius == 0) { _frame = frame; return source; }
        var width = Math.Max(1, (source.Width + settings.Downsample - 1) / settings.Downsample);
        var height = Math.Max(1, (source.Height + settings.Downsample - 1) / settings.Downsample);
        if (_scratch is null || _scratch.Texture.Width != width || _scratch.Texture.Height != height)
        {
            if (_scratch is not null && _frame == frame) throw new InvalidOperationException("Cannot resize a blur instance after it was used in this frame. Use another effect instance.");
            var scratch = _device.CreateRenderTarget(width, height);
            RenderTarget output;
            try { output = _device.CreateRenderTarget(width, height); } catch { scratch.Dispose(); throw; }
            _scratch?.Dispose(); _output?.Dispose(); _scratch = scratch; _output = output;
        }
        _frame = frame;
        var radius = (int)Math.Ceiling((float)settings.Radius / settings.Downsample);
        var sigma = Math.Max(0.1f, settings.Sigma / settings.Downsample);
        var bounds = new RectangleF(0, 0, width, height);
        context.Canvas.RenderTo(_scratch, layer => Pass(layer, source, bounds, new(1f / width, 0, radius, sigma)));
        context.Canvas.RenderTo(_output!, layer => Pass(layer, _scratch.Texture, bounds, new(0, 1f / height, radius, sigma)));
        return _output!.Texture;
    }
    private void Pass(Canvas2D layer, Texture source, RectangleF bounds, Vector4 parameters)
    {
        layer.Blend = BlendMode.Opaque; layer.Filter = TextureFilter.Linear;
        layer.Material = _material; layer.Parameters = parameters; layer.DrawSprite(source, bounds);
    }
    /// <summary>Applies the effect and alpha-composites it into the destination, preserving the caller's Canvas state.</summary>
    public void Draw(RenderContext context, Texture source, RectangleF destination, BlurSettings? settings = null, ColorRgba? tint = null)
    {
        if (!destination.IsVisible) throw new ArgumentOutOfRangeException(nameof(destination));
        var resolvedTint = tint ?? ColorRgba.White;
        if (!float.IsFinite(resolvedTint.R) || !float.IsFinite(resolvedTint.G) || !float.IsFinite(resolvedTint.B) || !float.IsFinite(resolvedTint.A))
            throw new ArgumentOutOfRangeException(nameof(tint));
        var result = Apply(context, source, settings);
        var canvas = context.Canvas;
        var blend = canvas.Blend; var filter = canvas.Filter; var material = canvas.Material; var parameters = canvas.Parameters;
        try
        {
            canvas.Blend = BlendMode.Alpha; canvas.Filter = TextureFilter.Linear;
            canvas.Material = null; canvas.Parameters = default; canvas.DrawSprite(result, destination, tint: resolvedTint);
        }
        finally { canvas.Blend = blend; canvas.Filter = filter; canvas.Material = material; canvas.Parameters = parameters; }
    }
    private void EnsureAlive()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Effects must be used on their owning thread.");
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
    /// <summary>Releases targets and material on the owning thread after their final presentation.</summary>
    public void Dispose()
    {
        if (IsDisposed) return; EnsureAlive();
        _scratch?.Dispose(); _output?.Dispose(); _material.Dispose(); IsDisposed = true;
    }
}
