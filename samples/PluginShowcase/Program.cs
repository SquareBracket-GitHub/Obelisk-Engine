using Obelisk.Abstractions;
using Obelisk.Vanilla;

return ObeliskApp.Run(new PluginShowcase.ShowcaseGame(args.Contains("--probe")),
    new AppOptions { Title = "Obelisk official plugins: wheel = font size, arrows = blur", Width = 900, Height = 560 },
    new PluginOptions { IncludeEffects = true });
