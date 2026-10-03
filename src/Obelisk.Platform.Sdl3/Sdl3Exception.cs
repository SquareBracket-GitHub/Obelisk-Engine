namespace Obelisk.Platform.Sdl3;

public sealed class Sdl3Exception : Exception
{
    public Sdl3Exception(string operation, string nativeError)
        : base(CreateMessage(operation, nativeError))
    {
        Operation = operation;
        NativeError = nativeError;
    }

    public string Operation { get; }

    public string NativeError { get; }

    private static string CreateMessage(string operation, string nativeError)
    {
        var detail = string.IsNullOrWhiteSpace(nativeError)
            ? "SDL3 did not provide an error message."
            : nativeError;

        return $"{operation} failed: {detail}";
    }
}
