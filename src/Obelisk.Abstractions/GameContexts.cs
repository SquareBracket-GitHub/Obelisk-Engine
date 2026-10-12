using Obelisk.Graphics2D;

namespace Obelisk.Abstractions;

public sealed class InitializeContext(ITextureDevice? textures = null, IAudioDevice? audio = null, IAssetSource? assets = null)
{
    public InitializeContext WithAssets(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new InitializeContext(textures, audio, source);
    }
    public IAssetSource Assets => assets ?? throw new NotSupportedException("This context does not provide an asset source.");
    public ITextureDevice Textures => textures ?? throw new NotSupportedException("This platform does not provide textures.");
    public IAudioDevice Audio => audio ?? SilentAudioDevice.Instance;
}

public sealed class UpdateContext
{
    public UpdateContext(
        GameTime time,
        InputSnapshot? input = null,
        OutputSize outputSize = default)
    {
        Time = time;
        Input = input ?? InputSnapshot.Empty;
        OutputSize = outputSize;
    }

    public GameTime Time { get; }

    public InputSnapshot Input { get; }

    public OutputSize OutputSize { get; }
}

public sealed class RenderContext
{
    public RenderContext(GameTime time, OutputSize outputSize = default)
    {
        Time = time;
        OutputSize = outputSize;
        Canvas = new Canvas2D();
    }

    public GameTime Time { get; }

    public OutputSize OutputSize { get; }

    public Canvas2D Canvas { get; }
}

public sealed class ShutdownContext;
