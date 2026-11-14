# Production graphics ownership tests

The project links the actual `Gsd2Graphics.cs`, including its loader callbacks,
projection, retirement and resource-lease guards. It does not replace that class
with property stubs. Test state is prepared through the actual `Tick`, native
loader wrapper, callback and graph-proxy creation paths.

Controlled wrappers model Unity allocation, original loader completion,
delegate conversion and the minimal graph/resident API. A native-access trap
throws on foreign game/graph/Unity/time getters or writes. Ownership/runtime
admission and unrelated NPC direction-adapter policy are boundary doubles;
their production implementations have separate suites. Reflection only reads
private collection counts/generation for contract assertions.

Cases establish managed-only `Suspend`, unowned reset, canonical restoration
after retired proxies outlive `Originals`, stale preload isolation from a new
session's loading IDs, epoch/runtime identity rejection, original raw callback
cleanup, fault attribution and retained exact-pointer resource leases. Native
callback exceptions remain the original exceptions, without faulting a new
runtime. Native loader completion still runs when projection is rejected.

Run with the pinned SDK:

```powershell
dotnet run --project tests/SuikodenRandomizer.Suikoden2.GraphicsOwnership.Tests -p:NuGetAudit=false
```

No shipped game code, Unity runtime, Harmony patch installation, live attachment
or gameplay input is involved. These checks are not live game-switch validation.
