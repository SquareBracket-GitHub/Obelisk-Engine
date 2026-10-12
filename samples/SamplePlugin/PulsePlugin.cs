using System.Globalization;
using System.Numerics;
using Obelisk.Abstractions;
using Obelisk.Graphics2D;
using Obelisk.Plugin;

namespace SamplePlugin;

/// <summary>A local plugin adds an animated badge without changing or referencing the game.</summary>
public sealed class PulsePlugin : IObeliskPlugin
{
    public void Register(PluginRegistrationContext context)
    {
        context.RegisterAssetLoader<string>(File.ReadAllText);
        context.RegisterModule(new PulseModule($"plugin://{context.Manifest.Name}/accent.txt"));
    }

    private sealed class PulseModule(string accentUri) : IObeliskModule
    {
        private ColorRgba _accent;
        private float _phase;
        public void Initialize(InitializeContext context)
        {
            var values = context.Assets.Load<string>(accentUri).Trim().Split(',');
            if (values.Length != 3) throw new InvalidDataException("Sample accent requires three RGB values.");
            _accent = new ColorRgba(float.Parse(values[0], CultureInfo.InvariantCulture),
                float.Parse(values[1], CultureInfo.InvariantCulture), float.Parse(values[2], CultureInfo.InvariantCulture), 1);
        }
        public void Update(UpdateContext context) => _phase = (_phase + (float)context.Time.DeltaTime.TotalSeconds * 3) % MathF.Tau;
        public void Render(RenderContext context)
        {
            context.Canvas.Blend = BlendMode.Alpha;
            context.Canvas.FillCircle(new Vector2(28, 28), 10 + 3 * MathF.Sin(_phase), _accent);
        }
    }
}
