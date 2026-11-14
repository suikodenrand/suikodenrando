using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal sealed class Gsd2Runtime
{
    internal static Gsd2Runtime? Current;
    internal static long OwnerEpoch;
    internal bool Allowed = true, Bound = true, HasTables = true;
    internal long ExecutionEpoch { get; set; } = OwnerEpoch;
    internal bool OwnsExecution => Allowed && ExecutionEpoch == OwnerEpoch;
    internal bool MatchesExecution(long epoch) => OwnsExecution && ExecutionEpoch == epoch;
    internal bool IsAdmitted => OwnsExecution && Bound;
    internal bool TablesReady => IsAdmitted && HasTables;
    internal bool IsActive => TablesReady && Gsd2Graphics.IsReady;
    internal readonly Dictionary<int, int> Map = new();
    internal bool IsRole(int id) => IsAdmitted && Map.ContainsKey(id);
    internal int MapDisplayId(int id) => TablesReady && Map.TryGetValue(id, out int donor) ? donor : id;
    internal readonly List<string> Failures = new(), Traces = new();
    internal void Fail(string message) => Failures.Add(message);
    internal void Trace(string message) => Traces.Add(message);
}

// Unrelated direction-adapter policy is a boundary double. Its production
// implementation has a separate17-case suite; this suite links Graphics itself.
internal static class Gsd2NpcDirectionAdapters
{
    internal sealed class RetryGate
    {
        internal bool Begin(int donor, IntPtr source, float now) => true;
        internal void Complete(int donor, IntPtr source) { }
        internal void Clear() { }
    }
    internal static bool IsSupportedDonor(int donor) => false;
    internal static OPD_DATA? Find(int donor, OPD_DATA? source) => null;
    internal static OPD_DATA? Prepare(int donor, OPD_DATA? source) => null;
}
