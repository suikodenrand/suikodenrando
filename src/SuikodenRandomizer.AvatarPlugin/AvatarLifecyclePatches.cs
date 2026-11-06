extern alias GSD1;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(GSD1.TitleChapter), "TitleMain")]
internal static class AvatarNewGamePatch
{
    private static IntPtr _instance;
    private static int _step = -1;
    [HarmonyPrefix]
    private static void Prefix(GSD1.TitleChapter __instance, int __0)
    {
        try
        {
            int step = GSD1.ChapterManager.GetGameStepSeq(__0);
            if (_instance == __instance.Pointer && _step == step) return;
            _instance = __instance.Pointer; _step = step;
            if (step == (int)GSD1.TitleChapter.State.DifficultyInit) AvatarRuntime.Current?.BeginNewGame();
        }
        catch (Exception exception) { AvatarRuntime.Current?.Fail("New Game boundary inspection failed: " + exception.Message); }
    }
}

[HarmonyPatch(typeof(GSD1.GameInit), "LoadPlayerData")]
internal static class AvatarGameInitPatch
{
    [HarmonyPrefix]
    private static void Prefix() => AvatarRuntime.Current?.EnsureProxy("native party reconstruction", false);
    [HarmonyPostfix]
    private static void Postfix()
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsAdmitted == true) runtime.EnsureProxy("native party reconstruction complete step", false);
    }
}

[HarmonyPatch(typeof(GSD1.PlayerManager), "GetPlayerData")]
internal static class AvatarCharacterDataPatch
{
    [HarmonyPostfix]
    private static void Postfix(GSD1.CHARACTER_ID __0, ref GSD1.PLAYER_DATA __result)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsAdmitted != true || !runtime.IsRole((int)__0)) return;
        if (AvatarPresentation.ResolvingFaces || AvatarPresentation.ResolvingNames)
        {
            if (runtime.TryGetOriginal((int)__0, out var original)) __result = original;
            return;
        }
        __result = runtime.ProjectPlayerData((int)__0, __result) ?? __result;
    }
}

[HarmonyPatch(typeof(GSD1.P_player_c), "LoadPlayer")]
internal static class AvatarResidentLoadPatch
{
    [HarmonyPrefix]
    private static void Prefix(int __0)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsAdmitted == true && runtime.IsRole(__0))
            runtime.EnsureProxy("canonical resident load", false);
        // The integer argument is deliberately NEVER changed.
    }
    [HarmonyPostfix]
    private static void Postfix()
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsAdmitted == true) runtime.EnsureProxy("after native canonical resident load", false, force: true);
    }
}

[HarmonyPatch(typeof(GSD1.P_player_c), "AddPlayer")]
internal static class AvatarResidentBindPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsAdmitted == true) runtime.EnsureProxy("after native canonical base binding", false, force: true);
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "initialize_battle")]
internal static class AvatarBattleEntryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref int __result)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime is null) return true;
        try
        {
            if (runtime.FaultReason is not null) { __result = 0; return false; }
            if (!runtime.IsAdmitted) return true;
            if (!runtime.State.IsInitialized && !runtime.HasMappedPartyRole()) return true;
            if (!runtime.EnsureProxy("normal battle entry", true)) { __result = 0; return false; }
            return true;
        }
        catch (Exception exception) { runtime.Fail("Battle entry inspection failed: " + exception.Message); __result = 0; return false; }
    }
}

[HarmonyPatch]
internal static class AvatarBattleObservationPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "player_initialize", "calc_exp", "battle_end_free" })
            yield return AccessTools.Method(typeof(GSD1.BattleBase), name)
                ?? throw new MissingMethodException("BattleBase." + name);
    }
    [HarmonyPostfix]
    private static void Postfix(MethodBase __originalMethod)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsActive != true) return;
        try
        {
            runtime.BindResidents();
            runtime.VerifyFinalParty(__originalMethod.Name);
            if (__originalMethod.Name == "player_initialize")
            {
                var data = GSD1.BattleBase.battle_work?.p_battle_data;
                if (data is not null)
                    for (int n = 0; n < data.Count; n++)
                    {
                        var player = data[n]?.player_data;
                        if (player is not null && runtime.IsRole(player.chara_id))
                        {
                            var role = AvatarNativeData.FindBase(player.chara_id);
                            var proxy = runtime.ProxyFor(player.chara_id);
                            runtime.Trace("BATTLE LINK slot=" + n + "; data/base=" + player.chara_id + "/"
                                + player.player_base?.chara_no + "; donor=" + runtime.Profile.GetDonor(player.chara_id)
                                + "; proxy=" + (player.Pointer == proxy?.Pointer)
                                + "; avatarOPD=" + (player.battle_data?.opd_data?.Pointer == proxy?.battle_data?.opd_data?.Pointer)
                                + "; canonicalBase=" + (player.player_base?.Pointer == role.Pointer)
                                + "; actions=" + player.battle_data?.action_table?.Count);
                        }
                    }
            }
        }
        catch (Exception exception) { runtime.Fail("Battle identity check failed: " + exception.Message); }
    }
}

[HarmonyPatch(typeof(GSD1.Event_c), "GoEventCommand")]
internal static class AvatarFaultDriverPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GSD1.Event_c __instance)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.FaultReason is null) return true;
        __instance.ClrEventRenzoku();
        return false;
    }
}
