# Native saves in unbound Suikoden I campaigns

Loading an ordinary save without a Stars of Destiny campaign binding now leaves
native saving available, including Suikoden Fix Save Anywhere. It does not adopt
the configured Shuffle seed, convert characters or create randomizer sidecars.
The destination slot remains the responsibility of the native game or the save
mod; this change does not impose a slot-number limit.

Passthrough requires a successful native load in the current owned execution
epoch, an unbound campaign with no fault, and no active randomizer publication.
Any retained publication for the current player manager is restored first.
If a snapshot still has a treasure, joining-weapon or joining-equipment sidecar
but its campaign binding is missing, restore the matching campaign binding to
save again. Pending New Game initialization, rejected bindings, failed native
loads and suspended/faulted randomized campaigns remain save-blocked.

Native-only save calls have a separate scope that masks an outer randomizer
save transaction. Capture and storage hooks skip binding work for that scope,
and finalization restores its predecessor even when native saving throws.
Already-bound Shuffle and Fixed Roster saves retain their existing preparation,
canonical snapshot binding, policy-sidecar and storage-failure checks.
For bound saves, a rejected binding or a failed native storage call also
suppresses the verified Suikoden Fix 1.5.2 EditSave mirror for that call.

`tests/SuikodenRandomizer.AvatarRuntime.Tests` links these production runtime
and save patches against managed native test doubles. Its save-admission cases
cover native callback/result neutrality, no conversion or metadata, failed and
pending admissions, epoch suspension, reused manager cleanup, nested scopes and
unchanged bound-save storage protection. These checks do not replace a native
Save Anywhere/reload playtest with the compiled game-facing plugin.
