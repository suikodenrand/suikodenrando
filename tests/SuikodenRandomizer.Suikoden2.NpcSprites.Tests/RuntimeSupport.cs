using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal sealed class Gsd2Runtime
{
    internal static Gsd2Runtime? Current = null;
    internal bool Active = true;
    internal object? Profile = new();
    internal string? FaultReason;
    internal bool IsActive => Active && FaultReason is null;
    internal readonly HashSet<int> Roles = new() { 22, 28 };
    internal readonly List<string> Traces = new();
    internal bool IsRole(int id) => Roles.Contains(id);
    internal void Trace(string message) => Traces.Add(message);
    internal void Fail(string message) => FaultReason = message;
}
internal static class Gsd2Graphics
{
    internal static readonly Dictionary<int, OPD_DATA> Projected = new();
    internal static readonly List<int> Requests = new();
    internal static OPD_DATA? NpcFieldOpd(int canonical)
    {
        Requests.Add(canonical);
        return Gsd2Runtime.Current?.IsActive == true && Gsd2Runtime.Current.IsRole(canonical)
            && Projected.TryGetValue(canonical, out var data) ? data : null;
    }
}
internal static class Gsd2Plugin
{
    internal static readonly TestLog PluginLog = new();
}
internal sealed class TestLog
{
    internal readonly List<string> Warnings = new();
    internal bool ThrowWarnings = false;
    internal void LogWarning(string message)
    {
        if (ThrowWarnings) throw new InvalidOperationException("injected logging failure");
        Warnings.Add(message);
    }
}
