# ADR 0012: Opt-in local terminal debug control

Accepted for v0.3.0-alpha261112 on 2026-11-12.

## Context

Native test-only SDL event injection cannot control a separately running game.
Agent-driven testing needs frame acknowledgments, stable Canvas coordinates and
captures, while keeping SDL/native handles out of public contracts.

## Decision

The CLI opts each game subprocess into a user-restricted named pipe using an
explicit environment value. Session identity is a short user/project-or-name
hash to fit macOS Unix socket path limits. Restore/build subprocesses and ordinary
game launches do not inherit that value through the CLI process runner. Transport
accepts one bounded request at a time; native/input/GPU work executes on the owning
thread. No socket listens on a network interface and no command evaluates code.

The additive Runtime `IFrameControl` contract gates simulation frames and supplies
a fixed stepping delta without changing `IApplicationPlatform` or game callbacks.
SDL implements it explicitly. Paused loops keep pumping events and reset wall-clock
baselines, avoiding a large delta on resume. Step and input responses follow actual
Update/Render/Present completion. Click/press uses distinct down and up frames.
Inputs may advance paused simulation; clients receive completed frame counts.

Opted-in rendering records the original command stream once into an RGBA snapshot,
including existing offscreen passes, then composites it to the swapchain. Screenshot
requests download that persistent snapshot, encode PNG and return it to the client;
the game never accepts a screenshot filesystem path. Normal rendering is unchanged.

## Compatibility and limits

Existing v0.2 public member/type contracts are retained, with a separate v0.3 API
baseline for the additive interface. Assembly identities and plugin range checks
remain unchanged. Sessions reset on dev restart, do not provide custom game-state
inspection, and mix with real input. Escape injection is rejected in favor of an
explicit quit command. Request timeouts are not transactional cancellation; status
must be checked before retrying a potentially applied command.

Apple Silicon source/native integration and automated tests passed. Windows and
Intel Mac native execution, distribution signing and long-duration certification
remain separate gates. The previously observed empty plugin output-directory
issue after removal is not changed by this feature.
