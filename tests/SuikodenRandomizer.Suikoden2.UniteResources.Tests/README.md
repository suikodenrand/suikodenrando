# Suikoden II unite resource scopes

This fixture links the actual `Gsd2Presentation.cs` and invokes its real Harmony
callbacks. It models only the native image constructor/iterator boundary and the
managed-to-Il2Cpp iterator bridge; it does not execute Unity or the game.

The tests use a non-idempotent mapping chain (32 -> 33 -> 2 -> 50), so an accidental
second projection changes the resource and fails the assertions. They check the
three literal Fancy Lad face IDs through asynchronous yields, ordinary/raw
interleaving, constructor/MoveNext/Current exceptions, early disposal and exact
scope cleanup. Controller ownership and the actual Fancy Lad MoveNext hook are
covered by the separate shuffled-unite runtime fixture.
