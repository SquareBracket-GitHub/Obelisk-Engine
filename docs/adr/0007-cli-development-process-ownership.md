# ADR 0007: CLI development loop and process ownership

## Status

Accepted for revised roadmap Day 10, implemented 2026-10-12.

## Context

The CLI must execute independent generated projects and restart them after code
changes without overlapping or orphaned games. SDK commands can spawn descendants;
waiting for only the direct child does not establish that the entire tree stopped.
The initial v0.1 platform is Windows x64.

## Decision

Keep the CLI as standard dotnet orchestration. Validate schema 1 obelisk.json,
then restore, build and run in the project directory. Pass game arguments with
ProcessStartInfo.ArgumentList, without a shell. Drain stdout and stderr concurrently.
Reject missing/unknown/duplicate manifest fields, incompatible engine versions,
absolute/traversal/reparse paths and Windows trailing-dot/space aliases.

Use a recursive FileSystemWatcher with a bounded signal channel and a 300 ms quiet
period. Ignore SDK output and editor metadata. Track revisions during preparation:
skip launching a stale build and prepare the latest revision. Dispose the current
game tree before the next restore. Build or later manifest failures wait for a save;
normal game exit waits too. Initial invalid metadata fails immediately.

Each Windows SDK child has a private Job Object with KILL_ON_JOB_CLOSE. Assign
the started child before returning its owner; descendants inherit membership.
Termination checks the job's active process count before allowing a restart and
aborts if the count cannot reach zero within five seconds. Normal child completion
also stops remaining descendants before draining inherited pipes. OS handle closure
cleans assigned descendants when the CLI is forcibly terminated. Assignment follows
Process.Start, so this is lifecycle management for SDK/game processes, not isolation
against code deliberately escaping ownership during startup.

Ctrl+C cancels preparation or execution, releases the owned tree and returns 130.
Windows job ownership is private to the CLI and introduces no native engine API
or additional package dependency. Other platforms use Process.Kill with tree
termination for normal cancellation; forced CLI-exit cleanup remains Windows-only.

## Consequences and validation

Restart replaces the game process and its state; this is not in-process hot reload.
Custom manifest content roots affect watching; matching project copy rules and game
startup options remain the author's responsibility. Global SDK cache/tool settings
are unchanged. CLI tests cover cancellation, failed stages, repaired metadata,
changes during build, debounce, argument/exit-code preservation and descendant
termination after parent exit. The external packaged Gate checks a native generated
game, one restart for a save burst and cleanup after killing only the CLI.

References: [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
and [Process.Kill descendant waiting limits](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0).
