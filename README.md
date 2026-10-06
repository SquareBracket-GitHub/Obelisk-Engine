# Obelisk Engine

Obelisk is an experimental, plugin-first 2D game engine for .NET 10 and SDL3.
The repository is currently building the first playable slice of the v0.1 engine.

## Prerequisites

- .NET SDK 10.0.401 (pinned by `global.json`)
- Git

## Build and test

```powershell
dotnet build Obelisk.sln
dotnet test Obelisk.sln --no-build
```

## Run the current sample

```powershell
dotnet run --project samples/MovingRectangle/MovingRectangle.csproj
```

The sample opens a resizable SDL3 GPU window and draws an orange rectangle.
Move it with the arrow keys. Movement uses frame delta time, and the rectangle
remains inside the drawable area after a resize. Press Escape or use the window
close button to exit.

## Repository layout

- `src/`: engine libraries and the CLI
- `samples/`: example games and plugin
- `tests/`: automated tests
- `docs/`: architecture decisions and user documentation (added as work progresses)

