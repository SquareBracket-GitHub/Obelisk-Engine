using System.Runtime.InteropServices;
using Obelisk.Graphics2D;

namespace Obelisk.Platform.Sdl3;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct QuadVertex(float X, float Y, float R, float G, float B, float A, float U = 0, float V = 0);

internal sealed record QuadMesh(QuadVertex[] Vertices, uint[] Indices)
{
    public static QuadMesh Create(IReadOnlyList<RectangleCommand> rectangles, uint width, uint height) =>
        CanvasMesh.Create(rectangles, width, height, _ => throw new InvalidOperationException("Rectangles have no texture.")).Mesh;
}
