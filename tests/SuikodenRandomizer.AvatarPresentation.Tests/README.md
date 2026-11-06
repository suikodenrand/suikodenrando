# Presentation adapter behavioral checks

This suite links the actual production `AvatarPresentation.cs` file. Explicit
Harmony/runtime/native test doubles drive deterministic converter chains and field
sprite factory outcomes. These are managed adapter tests, not native detour or
full external-wrapper/plugin compilation checks.

The portrait regression directly invokes the real static/instance converter patch
prefixes, the native-model character-to-face conversion, postfixes and finalizers
without any `GetFaceSprite` call. It covers both nesting directions, full-scope
pristine donor access, protected and encoded IDs, shared face assets, self maps,
exception unwinding and the absence of a second sprite-ID projection. Name and
field adapter checks are retained.

Run `dotnet run --project tests/SuikodenRandomizer.AvatarPresentation.Tests -c Release`.
