# Obelisk Engine

Obelisk is an experimental, plugin-first 2D game engine for .NET 10 and SDL3.
The repository is currently at the project-scaffolding stage of the v0.1 plan.

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

The current sample opens a resizable black SDL3 window. Press Escape or use the
window close button to exit. Rendering and movement are added in later stages.

See [PLAN.md](PLAN.md) for the six-week implementation plan and scope.

## Repository layout

- `src/`: engine libraries and the CLI
- `samples/`: example games and plugin
- `tests/`: automated tests
- `docs/`: architecture decisions and user documentation (added as work progresses)

