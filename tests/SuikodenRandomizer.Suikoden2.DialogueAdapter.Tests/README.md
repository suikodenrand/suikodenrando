# Suikoden II dialogue adapter checks

Run with the .NET 10 SDK:

```powershell
dotnet run --project tests/SuikodenRandomizer.Suikoden2.DialogueAdapter.Tests
```

This console harness links the real dialogue adapter, runtime, and character
publication code. Its native objects and bridge are test doubles. The checks
exercise admission, projected canonical name lookup, pointer replacement,
ownership changes, and scope cleanup. They do not start the game or install
Harmony patches. The separate NativeContracts project verifies the compiled
hook declarations and game interop signatures.
