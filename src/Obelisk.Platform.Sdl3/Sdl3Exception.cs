namespace Obelisk.Platform.Sdl3;

/// <summary>Reports the failed SDL operation and native diagnostic without exposing native handle types.</summary>
public sealed class Sdl3Exception : Exception
{
    /// <summary>Creates an operation-specific native failure diagnostic.</summary>
    public Sdl3Exception(string operation, string nativeError)
        : base(CreateMessage(operation, nativeError))
    {
        Operation = operation;
        NativeError = nativeError;
    }

    /// <summary>Gets the failed platform operation.</summary>
    public string Operation { get; }

    /// <summary>Gets the SDL-provided diagnostic, possibly empty.</summary>
    public string NativeError { get; }

    private static string CreateMessage(string operation, string nativeError)
    {
        var detail = string.IsNullOrWhiteSpace(nativeError)
            ? "SDL3 did not provide an error message."
            : nativeError;

        return $"{operation} failed: {detail}";
    }
}
