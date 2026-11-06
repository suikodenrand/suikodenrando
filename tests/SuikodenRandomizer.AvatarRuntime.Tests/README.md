# Managed runtime adapter checks

This project links the **actual production `AvatarRuntime.cs`** and uses real Core shuffle profiles, campaign state, strict binding codec and batch transaction orchestration. It executes the adapter against deliberately small managed native test doubles in the separate `AvatarRuntime.TestDoubles` project. `AvatarNativeData`, logging, paths, scene access and binding storage are test doubles.

The checks cover transient startup readiness, exact live-base binding, complete pristine donor snapshot use, excluded identities, no repeated initialization, saved-seed restore, progression-preserving replay, full rollback, title lifecycle and identity rejection.

Passing this suite does **not** compile the complete plugin against real BepInEx/Unity/Il2Cpp references, install or execute native hooks, validate native object ownership or establish gameplay compatibility. The real GSD1 metadata gate and installed-reference plugin build remain separate requirements.
