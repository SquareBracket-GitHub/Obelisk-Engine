# Images plugin dependency

`Obelisk.Plugins.Images` pins StbImageSharp **2.30.16**, a managed port of stb_image.
Its NuGet package declares the license expression **Unlicense OR MIT**. This
dependency is confined to the image importer and is brought into default games by
the Vanilla bundle.

- Package and license: https://www.nuget.org/packages/StbImageSharp/2.30.16
- Source: https://github.com/StbSharp/StbImageSharp
- Package source commit: `125af70cb557033f2c46aec8e82eaaf72ac49817`

The CanvasShowcase 2×2 checker PNG and test fixtures were authored for this
repository; they do not require a third-party image download.
