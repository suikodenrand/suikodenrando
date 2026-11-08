# Retry resource gate checks

This project links the actual production `NuzlockeRetryResourceGate.cs`. Controlled asynchronous callbacks and a manual clock exercise the transition from shared-resource loading to native retry scheduling.

The checks verify one load per attempt, no retry or resource read before completion, no callback-thread battle entry, synchronous-completion deferral, missing-resource rejection, timeout, cancellation across campaign changes, and one-time handling of load/read/scheduling failures.

These checks do not execute Il2Cpp delegate conversion, allocate or release native OPD resources, or reconstruct a battle. Those native boundaries require a genuine-reference plugin build and an in-game full-wipe retry test.
