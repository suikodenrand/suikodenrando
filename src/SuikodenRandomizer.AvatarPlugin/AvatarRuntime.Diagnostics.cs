extern alias GSD1;

using System;
using System.Linq;
using System.Text;
using UnityEngine.SceneManagement;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    private long _nextDiagnosticObservation;
    private string? _lastDiagnosticLocation;

    // Call only on the game thread. Exception/log callbacks use the journal's
    // managed cached context and must never dereference native game objects.
    internal void DiagnosticCheckpoint(string boundary)
    {
        CrashDiagnostics.Breadcrumb(boundary + ".enter", ManagedDiagnosticContext());
        try
        {
            var game = GSD1.OldSrcBase.game_work;
            if (game is null) { CrashDiagnostics.Breadcrumb(boundary + ".state", "game=null"); return; }
            var party = game.party_data;
            var text = new StringBuilder("game=0x" + game.Pointer.ToString("X"));
            if (party is null) { CrashDiagnostics.Breadcrumb(boundary + ".state", text + "; party=null"); return; }
            text.Append("; location=").Append(party.vil_no).Append('/').Append(party.map_no).Append('/').Append(party.area_no)
                .Append("; position=").Append(party.x).Append(',').Append(party.y)
                .Append("; count=").Append(party.player_kazu)
                .Append("; codes=[").Append(string.Join(",", party.chara_code.Take(6))).Append(']')
                .Append("; order=[").Append(string.Join(",", party.narabi.Take(6))).Append(']');
            var residents = game.player_data;
            if (residents is not null)
                for (int slot = 0; slot < Math.Min(6, Math.Min(residents.Count, party.chara_code.Count)); slot++)
                {
                    int role = party.chara_code[slot];
                    var resident = residents[slot];
                    var liveBase = resident?.player_base;
                    int donor = IsNuzlocke && _nuzCampaign!.IsRole(role) ? _nuzCampaign.GetDonor(role)
                        : _fixedAssignments?.Assignments.TryGetValue(role, out int fixedDonor) == true ? fixedDonor
                        : State.Profile?.IsRole(role) == true ? State.Profile.GetDonor(role) : role;
                    _proxies.TryGetValue(role, out var proxy);
                    text.Append("; slot=").Append(slot).Append(" role=").Append(role).Append(" donor=").Append(donor)
                        .Append(" resident=0x").Append(resident?.Pointer.ToString("X") ?? "0")
                        .Append(" base=0x").Append(liveBase?.Pointer.ToString("X") ?? "0")
                        .Append(" baseRole=").Append(liveBase?.chara_no.ToString() ?? "null")
                        .Append(" hp=").Append(liveBase?.hp.ToString() ?? "null")
                        .Append(" proxy=0x").Append(proxy?.Pointer.ToString("X") ?? "0");
                }
            CrashDiagnostics.Breadcrumb(boundary + ".state", text.ToString());
        }
        catch (Exception exception) { CrashDiagnostics.Exception(boundary + ".snapshot", exception); }
    }

    private string ManagedDiagnosticContext() => "mode=" + (IsNuzlocke ? "Nuzlocke" : IsFixedRoster ? "FixedRoster" : "Shuffle")
        + "; admitted=" + IsAdmitted + "; active=" + IsActive + "; fault=" + (FaultReason ?? "none")
        + "; proxies=" + _proxies.Count + "; heldOldProxies=" + _retired.Count
        + (IsNuzlocke ? "; battle=" + _nuzBattleId + "; battleOpen=" + _nuzBattleOpen
            + "; retry=" + _nuzRetryPending + "; retired=" + string.Join(",", _nuzCampaign!.RetiredHeroIds)
            + "; freshReserves=" + _nuzCampaign.RemainingReserves : "");

    private void ObserveDiagnosticState()
    {
        try { ObserveDiagnosticStateCore(); }
        catch (Exception exception) { CrashDiagnostics.Exception("diagnostic world observation", exception); }
    }

    private void ObserveDiagnosticStateCore()
    {
        long now = _timestamp();
        if (now < _nextDiagnosticObservation) return;
        _nextDiagnosticObservation = now + AutomaticCheckInterval;
        string scene = SceneManager.GetActiveScene().name;
        var game = scene == "GSD1" ? GSD1.OldSrcBase.game_work : null;
        var party = game?.party_data;
        string location = scene + "; game=0x" + (game?.Pointer.ToString("X") ?? "0")
            + (party is null ? "; party=null" : "; map=" + party.vil_no + "/" + party.map_no + "/" + party.area_no
                + "; count=" + party.player_kazu + "; codes=" + string.Join(",", party.chara_code.Take(6))
                + "; order=" + string.Join(",", party.narabi.Take(6)));
        if (location == _lastDiagnosticLocation) return;
        _lastDiagnosticLocation = location;
        CrashDiagnostics.Breadcrumb("world.changed", location);
        DiagnosticCheckpoint("world.changed");
    }
}
