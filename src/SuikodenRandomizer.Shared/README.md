# Shared ownership and UI routing

This project targets .NET Standard 2.1 and has no game, Unity, BepInEx, Harmony or
filesystem dependencies. The unified .NET 6 host supplies the current scene and
game-specific backend facades; native state and persistence remain inside each
engine.

Call `ActiveGameRouter.ObserveScene` before `GameUiCoordinator.Tick`, input or UI
selection. The default classifier accepts only exact `GSD1`/`GSD2` names; an
injected classifier can supply a separately verified native selection policy.
Unknown or failed classification has no owner. `Invalidate` advances the token
generation for a same-scene native load/reset. A token from another router is
also rejected. Tokens are transient and must never be stored in campaign files.

Capture ownership when registering deferred work, then call `TryRun` or
`IsCurrent` immediately before each later phase. These checks do not make an
async operation atomic or authorize native work on a background thread. Unity
native calls still belong on the host's main thread.

Register at most one `IGameUiBackend` per game. `Tick`, `SelectUi` and focused F6
dispatch select only the current owner. F6 consumes one event per supplied
frame number, including in a launcher scene; it cannot acquire a second game
destination during that frame. Backend exceptions are recorded per game and
generation. The other engine remains available. A reported engine fault remains
visible and blocks F6 diagnostics, while owned lifecycle Tick still runs so the
engine can perform title/load cleanup. Engines guard their own projections.
Unhandled coordinator-caught exceptions stop that owner/generation until an
explicit clear or a new generation; the shared panel can still toggle locally.

`LegacyGamePaths` contains immutable descriptors relative to the existing
BepInEx config root. S1/S2 config names and binding directories remain separate.
Historical S1 folders are preservation inventory, not schema selection. This
project never reads, moves, merges, rewrites or creates configuration/binding/
native-save data. It contains no shared recruitment or gameplay state.
