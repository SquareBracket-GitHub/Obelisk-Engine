# Third-party distribution notices

These files preserve the license and notice texts for the fixed dependencies used
by the Windows x64 engine. The owning engine projects copy their respective
subdirectories to build/publish output and package them as NuGet content files.
Keep the complete `licenses/` directory with redistributed games and tools.

| Directory | Fixed packages / binaries | License information |
| --- | --- | --- |
| `sdl3/` | SDL3-CS 3.4.16.2; patched SDL 3.4.16-obelisk.1 (win-x64); SDL3-CS.Windows.Shadercross 3.0.0.12 | Binding, SDL and SDL_shadercross zlib texts; SPIRV-Cross, DXC and their bundled notices are preserved separately. |
| `skiasharp/` | SkiaSharp and SkiaSharp.NativeAssets.Win32 4.148.0 | MIT wrapper license and native Skia third-party notices. |
| `stbimagesharp/` | StbImageSharp 2.30.16 | Package expression `Unlicense OR MIT`; this distribution uses the Unlicense option. |

Binding/Shadercross texts were copied byte-for-byte from the NuGet packages produced
from [SDL3-CS commit 7dbf32f](https://github.com/edwardgushchin/SDL3-CS/tree/7dbf32ff44ac611925df38c43897eef5f9c17c86).
The SDL license is the unaltered upstream 3.4.16 text (also supplied by the former
SDL3-CS.Windows package). The runtime now builds from
[SDL commit fa2c02b](https://github.com/libsdl-org/SDL/tree/fa2c02bb6e21974a89ea9824bc53c9932abe5f9c)
with the hotplug-event close patch. `sdl3/obelisk/` explicitly identifies this
alteration and carries the patch, source/toolchain inventory and binary checksum.
SkiaSharp texts come from its restored 4.148.0 packages, source commit
[4e4ce7a](https://github.com/mono/SkiaSharp/tree/4e4ce7af7ea8702593af5aeb25d05c65ffb74e90).
StbImageSharp's [pinned source](https://github.com/StbSharp/StbImageSharp/tree/125af70cb557033f2c46aec8e82eaaf72ac49817)
declares public domain or MIT, and its package declares the expression above.
The standard [SPDX Unlicense text](https://spdx.org/licenses/Unlicense.html) is
included because that package has no separate license file.

The Shadercross package includes `SDL3_shadercross.dll`, `spirv-cross-c-shared.dll`,
`dxcompiler.dll` and `dxil.dll`; the patched SDL build supplies `SDL3.dll`, and SkiaSharp supplies
`libSkiaSharp.dll`. Preserve all supplied notices, including DXC's Microsoft terms,
LLVM text and third-party notices. This review covers the packaged Windows x64
dependency set; it does not establish redistribution terms for other platforms.

PE import-table inspection of these pinned x64 binaries also finds
`VCRUNTIME140.dll` (Shadercross and SPIRV-Cross), `VCRUNTIME140_1.dll` and
`MSVCP140.dll` (SPIRV-Cross), plus Windows system/Universal CRT libraries.
Install Microsoft's supported [Visual C++ v14 x64 runtime](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
on the target machine. A self-contained .NET publish does not supply these native
DLL names. The patched SDL build itself uses upstream VisualC Release's static CRT
and imports only Windows system DLLs; Shadercross still needs the runtime above.
This repository does not copy runtime DLLs out of a developer's system
directories. The current SDK-blocked deployment test uses a machine with the
VC++ runtime installed; a separate clean-machine check remains release validation.

Sample checker images and WAV fixtures were authored for this repository. The
samples read Windows' `malgun.ttf` at runtime; they do not distribute the font file.
Vanilla's self-contained publish target copies Microsoft's runtime license and
third-party notices from the actual resolved runtime pack into `licenses/dotnet/`;
the SDK's normal publish does not copy these package-root files. Source builds
import the same target through `Directory.Build.targets`. Preserve these files.
Plugins and user-supplied assets need their own
applicable notices. The engine's own release license remains a separate release
decision and is not granted by these third-party licenses.
