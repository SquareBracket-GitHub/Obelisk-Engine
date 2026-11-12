# Terminal debugging

Obelisk 0.3.0 lets a second terminal control a development game without moving the
OS pointer. Use matching 0.3.0 CLI and engine packages. A normal `run` or `dev`
launch does not enable the endpoint.

## Install and start

```sh
dotnet tool update --global Obelisk.Cli --version 0.3.0
# On a new computer, use `dotnet tool install` instead of `update`.
cd MyGame
obelisk run --debug-control --session mygame
```

Keep that terminal open for game logs. In another terminal, under the same OS user:

```sh
obelisk debug --session mygame status
obelisk debug --session mygame pause
obelisk debug --session mygame click 320 180
obelisk debug --session mygame key down W
obelisk debug --session mygame step 30
obelisk debug --session mygame key up W
obelisk debug --session mygame screenshot ./frame.png
obelisk debug --session mygame resume
obelisk debug --session mygame quit
```

Choose a unique session per game. Names contain 1-48 ASCII letters, digits,
underscores or hyphens. If both launch and control omit `--session`, run both
commands in the same project directory. Session names are local to the OS user.

## Commands

| Command after `obelisk debug [--session name]` | Behavior |
| --- | --- |
| `status` | Read process ID, dimensions, display scale, MSAA count, completed frames/time and held inputs. |
| `pause` | Stop game updates/renders while continuing native event processing. |
| `resume` | Continue normal wall-clock simulation. |
| `step [N]` | While paused, advance 1-600 frames; omitted count means one. |
| `click X Y [button]` | Down at the coordinates, then up on the following simulation frame. Default button is Left. |
| `mouse move X Y` | Move the engine input position. |
| `mouse down button` / `mouse up button` | Change a button at the current engine input position. |
| `key down key` / `key up key` | Hold/release a mapped engine key. |
| `key press key` | Down and up on separate simulation frames. |
| `screenshot file.png` | Write the most recently rendered engine frame as PNG. |
| `quit` | Request normal game shutdown. In dev mode, the watcher remains active. |
| `help` | Display command syntax. |

Buttons: `Left`, `Middle`, `Right`, `X1`, `X2`. Keys use the engine `Key` names,
including `A`-`Z`, `Alpha0`-`Alpha9`, `Left`, `Right`, `Up`, `Down`, `Space`,
`Enter`, `Tab`, `Backspace`, `LeftShift`, `RightShift`, `LeftControl`,
`RightControl`, `LeftAlt` and `RightAlt`. Names are case-insensitive. Escape
injection is rejected; use `quit`. Release an already-held key/button before
using its press/click command.

## Coordinates and timing

Coordinates are finite drawable output pixels from the top-left, matching Canvas
and `InputSnapshot`. They must be inside `status.width` and `status.height`.
Retina logical window units and OS screen coordinates can differ; read dimensions
and `displayScale` before choosing positions. Decimal coordinates are accepted.

Input commands automatically advance paused simulation: one frame for down/up/move,
two for click/press. `step N` uses 1/60 second per frame, subject to the game's
maximum delta setting. `pause` and idle status queries do not advance simulation.
Successful input/step responses follow Update, Render and Present completion.
Use `completedFrames` and `elapsedSeconds` to check the actual applied progress.
Real physical input remains active and may affect a session.

## Screenshots and logs

Capture does not advance simulation. PNG includes engine offscreen passes and
MSAA, but excludes the window frame and OS cursor. While minimized, capture still
refers to the last rendered frame. Relative output paths use the control terminal's
working directory. Existing files are preserved; select a new filename to retry.

Game stdout/stderr remain in the launch terminal and can be redirected using the
shell. `status` reports engine state, not game-specific scene/entity variables.
Debug mode adds a snapshot texture and composite pass; ordinary execution does not.

## Development restart and troubleshooting

Use `obelisk dev --debug-control --session mygame` to rebuild on saves. Each new
game process starts unpaused with fresh input, frame and debug state; check its
`processId` after restarting. `quit` closes the game, while Ctrl+C in the launch
terminal ends the development watcher and releases owned child processes.

A connection failure usually means the game has not finished starting, debug
control was not enabled, or the project/session does not match. A command timeout
is not a transaction rollback: inspect status before retrying an input that might
already have applied. The endpoint is restricted to the current OS user and
accepts bounded commands; it is not a network listener or arbitrary-code console.

## Compatibility

Upgrade the game's `Obelisk.Vanilla` reference to 0.3.0 for these commands. The CLI
accepts existing schema-1 `engineVersion: 0.2.0` project files; new projects use
0.3.0. Plugin compatibility is checked against the actual engine version 0.3.0.
A plugin declaring an exclusive maximum of 0.3.0 needs validation and a manifest
range update before it can run on this release. Assembly contract identities and
all v0.2 public signatures are retained; native dependencies still need a build
for the target OS and architecture.
