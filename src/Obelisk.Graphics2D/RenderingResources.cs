namespace Obelisk.Graphics2D;

/// <summary>Selects how a texture is sampled. Nearest preserves pixel art; Linear interpolates adjacent texels.</summary>
public enum TextureFilter
{
    /// <summary>Samples the nearest texel.</summary>
    Nearest,
    /// <summary>Interpolates adjacent texels.</summary>
    Linear,
}

/// <summary>Experimental owning-thread rendering resource factory, independent of native backend types.</summary>
public interface IRenderDevice : ITextureDevice
{
    /// <summary>Gets the actual sample count, falling back to a supported count no greater than requested.</summary>
    int SampleCount { get; }
    /// <summary>Gets the current window's display scale for sizing UI/font caches. Rendering coordinates remain output pixels.</summary>
    float DisplayScale { get; }
    /// <summary>Creates an owned RGBA8 target. Its texture can be sampled after an earlier RenderTo pass.</summary>
    RenderTarget CreateRenderTarget(int width, int height);
    /// <summary>Compiles an experimental HLSL Shade function. The caller owns the material.</summary>
    Material CreateMaterial(string fragmentFunction);
}

/// <summary>Owns a renderable texture. Keep alive through presentation; dispose on the device's owning thread.</summary>
public sealed class RenderTarget : IDisposable
{
    /// <summary>Takes ownership of a backend-created texture; arbitrary textures are rejected by the rendering backend.</summary>
    public RenderTarget(Texture texture) => Texture = texture ?? throw new ArgumentNullException(nameof(texture));
    /// <summary>Gets the borrowed texture. Dispose this target rather than independently disposing the texture.</summary>
    public Texture Texture { get; }
    /// <summary>Gets whether the owned texture has been released.</summary>
    public bool IsDisposed => Texture.IsDisposed;
    /// <summary>Releases the owned texture; repeated successful disposal is safe.</summary>
    public void Dispose() => Texture.Dispose();
}

/// <summary>Experimental owned fragment material using the engine's fixed sprite vertex and sampler contract.</summary>
public sealed class Material : IDisposable
{
    private readonly Action _release;
    /// <summary>Wraps an engine identity and owning-thread callback, primarily for backend implementations.</summary>
    public Material(ulong id, Action release)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentNullException.ThrowIfNull(release);
        Id = id; _release = release;
    }
    /// <summary>Gets an engine identity, never a native GPU pointer.</summary>
    public ulong Id { get; }
    /// <summary>Gets whether disposal completed.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Releases backend pipelines on their owning thread. Keep alive until queued draws are presented.</summary>
    public void Dispose() { if (IsDisposed) return; _release(); IsDisposed = true; }
}

/// <summary>An ordered offscreen pass captured by Canvas2D.RenderTo. Nested passes and target feedback are rejected.</summary>
/// <param name="Target">Borrowed destination target.</param>
/// <param name="Commands">Captured child Canvas commands.</param>
/// <param name="ClearColor">Straight-alpha clear color, converted by the backend for transparent compositing.</param>
public sealed record RenderPassCommand(RenderTarget Target, IReadOnlyList<RenderCommand2D> Commands, ColorRgba ClearColor) : RenderCommand2D;
