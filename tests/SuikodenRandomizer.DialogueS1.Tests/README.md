# Suikoden I dialogue adapter checks

This .NET 10 console harness links the production `DialogueS1`, `AvatarRuntime.Dialogue` and `AvatarSaveCall` files. Native types, Harmony attributes, engine admission and the shared dialogue bridge are managed doubles. These checks do not execute a detour or establish live game behavior; the real Unified build and retained interop metadata independently verify native signatures.

The checks cover exactly scoped body, choice, backlog and name calls; stale campaign, ownership and proxy publications; the native 961/481 UTF-16 bounds and failed getter refusal; role zero and canonical-only name resolution; preservation of unassigned roles; and nested save/load barriers. Finalizers must preserve the original native exception, including when presentation cleanup fails.

Build with the pinned SDK and this folder’s `NuGet.Config`. Run the generated test DLL directly; an optional single argument writes a JSON case receipt at that path. The harness does not load the game, patch native code, or access installed configuration or saves.
