using System.Numerics;
using System.Text;
using Obelisk.Graphics2D;

namespace Obelisk.Platform.Sdl3;

internal sealed record DrawBatch(TextureHandle Texture, BlendMode Blend, uint FirstIndex, uint Count);
internal sealed record CanvasMesh(QuadMesh Mesh, IReadOnlyList<DrawBatch> Batches)
{
    public static CanvasMesh Create(IReadOnlyList<RenderCommand2D> commands, uint width, uint height,
        Func<TextureHandle, (int Width, int Height)> textureSize)
    {
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        var vertices = new List<QuadVertex>();
        var indices = new List<uint>();
        var batches = new List<DrawBatch>();
        foreach (var command in commands)
        {
            var first = (uint)indices.Count;
            var texture = default(TextureHandle);
            if (!Enum.IsDefined(command.Blend)) throw new ArgumentOutOfRangeException(nameof(commands));
            switch (command)
            {
                case RectangleCommand rectangle:
                    AddQuad(Corners(rectangle.Bounds), rectangle.Color, new RectangleF(0, 0, 1, 1));
                    break;
                case LineCommand line:
                    var delta = line.End - line.Start;
                    if (delta.LengthSquared() == 0) break;
                    var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * (line.Thickness / 2);
                    AddQuad([line.Start - normal, line.End - normal, line.Start + normal, line.End + normal],
                        line.Color, new RectangleF(0, 0, 1, 1));
                    break;
                case CircleCommand circle:
                    var segments = (int)Math.Clamp(MathF.Ceiling(circle.Radius * 0.75f), 12, 128);
                    var center = AddVertex(circle.Center, circle.Color, Vector2.Zero);
                    for (var i = 0; i < segments; i++)
                    {
                        var angle = MathF.Tau * i / segments;
                        AddVertex(circle.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * circle.Radius,
                            circle.Color, Vector2.Zero);
                    }
                    for (var i = 0; i < segments; i++)
                        indices.AddRange([center, center + 1 + (uint)i, center + 1 + (uint)((i + 1) % segments)]);
                    break;
                case SpriteCommand sprite:
                    texture = sprite.Texture;
                    var size = textureSize(texture); // Reject stale or foreign handles before GPU submission.
                    var source = sprite.Source ?? new RectangleF(0, 0, size.Width, size.Height);
                    if (source.X < 0 || source.Y < 0 || source.X + source.Width > size.Width || source.Y + source.Height > size.Height)
                        throw new ArgumentOutOfRangeException(nameof(commands), "Sprite source lies outside its texture.");
                    var points = Corners(new RectangleF(0, 0, sprite.Destination.Width, sprite.Destination.Height));
                    var rotation = Matrix3x2.CreateRotation(sprite.Rotation);
                    var position = new Vector2(sprite.Destination.X, sprite.Destination.Y);
                    for (var i = 0; i < points.Length; i++)
                        points[i] = Vector2.Transform(points[i] - sprite.Origin, rotation) + sprite.Origin + position;
                    AddQuad(points, sprite.Tint, new RectangleF(source.X / size.Width, source.Y / size.Height,
                        source.Width / size.Width, source.Height / size.Height));
                    break;
                case TextCommand text:
                    ObjectDisposedException.ThrowIf(text.Font.IsDisposed, text.Font);
                    _ = text.Font.Measure(text.Text, text.Scale);
                    texture = text.Font.Atlas.Handle;
                    var atlasSize = textureSize(texture);
                    var pen = text.Position;
                    foreach (var rune in text.Text.EnumerateRunes())
                    {
                        if (rune.Value == '\n')
                        {
                            pen.X = text.Position.X;
                            pen.Y += text.Font.LineHeight * text.Scale;
                            continue;
                        }
                        if (rune.Value == '\r') continue;
                        var glyph = text.Font.GetGlyph(rune);
                        var cell = glyph.Source;
                        AddQuad(Corners(new RectangleF(pen.X, pen.Y, cell.Width * text.Scale, cell.Height * text.Scale)), text.Color,
                            new RectangleF(cell.X / atlasSize.Width, cell.Y / atlasSize.Height,
                                cell.Width / atlasSize.Width, cell.Height / atlasSize.Height));
                        pen.X += glyph.Advance * text.Scale;
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported Canvas command: {command.GetType().Name}");
            }
            var count = (uint)indices.Count - first;
            if (count == 0) continue;
            if (batches.Count > 0 && batches[^1].Texture == texture && batches[^1].Blend == command.Blend)
                batches[^1] = batches[^1] with { Count = batches[^1].Count + count };
            else batches.Add(new(texture, command.Blend, first, count));
        }
        return new(new(vertices.ToArray(), indices.ToArray()), batches);

        uint AddVertex(Vector2 point, ColorRgba color, Vector2 uv)
        {
            var index = (uint)vertices.Count;
            vertices.Add(new(point.X / width * 2 - 1, 1 - point.Y / height * 2,
                color.R, color.G, color.B, color.A, uv.X, uv.Y));
            return index;
        }

        void AddQuad(Vector2[] points, ColorRgba color, RectangleF uv)
        {
            var offset = (uint)vertices.Count;
            AddVertex(points[0], color, new(uv.X, uv.Y));
            AddVertex(points[1], color, new(uv.X + uv.Width, uv.Y));
            AddVertex(points[2], color, new(uv.X, uv.Y + uv.Height));
            AddVertex(points[3], color, new(uv.X + uv.Width, uv.Y + uv.Height));
            indices.AddRange([offset, offset + 1, offset + 2, offset + 2, offset + 1, offset + 3]);
        }
    }

    private static Vector2[] Corners(RectangleF r) =>
        [new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X, r.Y + r.Height), new(r.X + r.Width, r.Y + r.Height)];
}
