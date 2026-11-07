extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class Program
{
    private static World NuzlockeWorld(bool includeTir=false)
    {
        var world=new World(false);
        AvatarPlugin.ConfiguredMode="Nuzlocke";
        AvatarPlugin.ConfiguredIncludeTir=includeTir;
        world.Runtime.BeginNewGame();
        world.Ready();
        return world;
    }

    private static void SetNuzlockeActive(World world,params int[] roles)
    {
        world.Game.party_data.chara_code=Enumerable.Range(0,6).Select(i=>(short)(i<roles.Length?roles[i]:-1)).ToList();
        world.Game.party_data.narabi=Enumerable.Range(0,6).Select(i=>(sbyte)(i<roles.Length?i:-1)).ToList();
        world.Game.party_data.player_kazu=(byte)roles.Length;
        world.Game.player_data=Enumerable.Range(0,6).Select(i=>i<roles.Length?world.Originals[roles[i]]:null).ToList();
        world.Ready();
    }

    private static GSD1.BATTLE_WORK StartNuzlockeBattle(World world)
    {
        var party=world.Game.party_data;
        var battle=new GSD1.BATTLE_WORK{player_kazu=party.player_kazu};
        // Native battle arrays reserve element zero and number players from one.
        battle.p_battle_data.Add(new GSD1.P_BATTLE_DATA());
        battle.pm_data.Add(null);
        for(int i=0;i<party.player_kazu;i++) {
            int slot=party.narabi[i];
            var resident=world.Game.player_data[slot]!;
            battle.p_battle_data.Add(new GSD1.P_BATTLE_DATA{player_data=resident});
            battle.pm_data.Add(new GSD1.PM_DATA{hp=resident.player_base!.hp});
        }
        GSD1.BattleBase.battle_work=battle;
        world.Runtime.BeginNuzlockeBattle();
        return battle;
    }

    private static int BattleIndex(World world,int role)
    {
        var party=world.Game.party_data;
        return Enumerable.Range(0,party.player_kazu).Single(i=>party.chara_code[party.narabi[i]]==role)+1;
    }

    private static void KnockOut(World world,GSD1.BATTLE_WORK battle,int role)
    {
        int index=BattleIndex(world,role);
        battle.pm_data[index]!.hp=0;
        battle.pm_data[index]!.IsDead=true;
        world.Base(role).hp=0;
        world.Runtime.ObserveNuzlockeKnockouts();
    }

    private static NuzlockeCampaign SavedNuzlocke(World world,char identity)
    {
        string id=new string(identity,64);
        world.Runtime.WriteCampaignBinding(new GSD1.SaveDataManager.SaveData{Identity=id});
        return NuzlockeCampaign.Restore(NuzlockeBindingStore.Bindings[id].Campaign);
    }

    private static void RunNuzlockeRuntimeChecks()
    {
        Case("Nuzlocke starts with the regular seeded cast while keeping canonical story slots",()=>{
            var w=NuzlockeWorld();
            var initial=AvatarShuffleProfile.Create(AvatarPlugin.ConfiguredSeed);
            foreach(int role in new[]{1,2,6}) Check(w.Runtime.MapDisplayId(role)==initial.GetDonor(role),"Starting cast changed");
            Check(w.Runtime.IsNuzlocke&&!w.Runtime.IsFixedRoster&&w.Runtime.MapDisplayId(8)==8,"Wrong campaign mode or protected Tir");
            Check(w.Game.party_data.player_kazu==5,"Story party size changed");
            w.Runtime.VerifyFinalParty("Nuzlocke startup");
        });

        Case("A latched KO retires once and its replacement inherits level, equipment and rune state",()=>{
            var w=NuzlockeWorld(); const int role=1;
            int dead=w.Runtime.MapDisplayId(role);
            w.Base(role).level=31;w.Base(role).exp=724;w.Base(role).Equipment=555;w.Base(role).Rune=777;w.Base(role).Items=new[]{99,100};
            var battle=StartNuzlockeBattle(w);
            KnockOut(w,battle,role);
            // Revival after the observed KO must not erase the death from the run.
            int index=BattleIndex(w,role);
            battle.pm_data[index]!.hp=20;battle.pm_data[index]!.IsDead=false;w.Base(role).hp=20;
            w.Runtime.FinishNuzlockeBattle(false);
            int replacement=w.Runtime.MapDisplayId(role);
            Check(replacement!=dead&&w.Base(role).level==31&&w.Base(role).exp==724
                &&w.Base(role).Equipment==555&&w.Base(role).Rune==777&&w.Base(role).Items.SequenceEqual(new[]{99,100})
                &&w.Base(role).hp==w.Base(role).max_hp,"Replacement lost inherited progression or gear");
            w.Runtime.FinishNuzlockeBattle(false);
            var campaign=SavedNuzlocke(w,'1');
            Check(campaign.RetiredHeroIds.SequenceEqual(new[]{dead})&&campaign.LastBattleId==1,"Death callback committed more than once");
            Check(!campaign.Assignments.Values.Contains(dead),"Retired fighter still assigned to a future role");
            Check(w.Runtime.ProxyFor(role)?.field_data==w.Originals[replacement].field_data,"Replacement visuals not bound");
        });

        Case("A retired fighter cannot return through a departing or newly forced story role",()=>{
            var w=NuzlockeWorld();
            int dead=w.Runtime.MapDisplayId(1);
            var battle=StartNuzlockeBattle(w);KnockOut(w,battle,1);w.Runtime.FinishNuzlockeBattle(false);
            int current=w.Runtime.MapDisplayId(1);
            var state=SavedNuzlocke(w,'2');
            int futureRole=state.RoleIds.First(role=>!state.Assignments.ContainsKey(role)&&role!=108&&role!=109);
            SetNuzlockeActive(w,8,1,2,futureRole);
            var donors=new[]{8,1,2,futureRole}.Select(w.Runtime.MapDisplayId).ToArray();
            Check(donors.Distinct().Count()==donors.Length&&!donors.Contains(dead),"Forced party introduced a duplicate or retired hero");
            SetNuzlockeActive(w,8,2,futureRole);
            SetNuzlockeActive(w,8,1,2,futureRole);
            Check(w.Runtime.MapDisplayId(1)==current,"Returning role lost its surviving replacement");
        });

        Case("Nuzlocke save restores deaths and mapping without resetting native progression",()=>{
            var w=NuzlockeWorld(true);
            var battle=StartNuzlockeBattle(w);int dead=w.Runtime.MapDisplayId(1);KnockOut(w,battle,1);w.Runtime.FinishNuzlockeBattle(false);
            int replacement=w.Runtime.MapDisplayId(1);
            w.Base(1).level=42;w.Base(1).exp=812;w.Base(1).Equipment=932;
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('3',64)};
            w.Runtime.WriteCampaignBinding(save);
            string before=AvatarNativeData.Fingerprint(w.Base(1));
            AvatarPlugin.ConfiguredMode="Shuffle";AvatarPlugin.ConfiguredSeed="other-seed";AvatarPlugin.ConfiguredIncludeTir=false;
            w.Runtime.PrepareLoad(save);w.Runtime.CompleteNuzlockeLoad();w.Ready();
            Check(w.Runtime.IsNuzlocke&&w.Runtime.MapDisplayId(1)==replacement&&before==AvatarNativeData.Fingerprint(w.Base(1)),"Saved state was reinitialized");
            var restored=SavedNuzlocke(w,'4');
            Check(restored.IncludesTir&&restored.RetiredHeroIds.Contains(dead),"Saved mode/death list was lost");
        });

        Case("A full party wipe retires the cast and makes a single replacement retry available",()=>{
            var w=NuzlockeWorld();
            int[] roles=w.Game.party_data.narabi.Take(w.Game.party_data.player_kazu)
                .Select(slot=>(int)w.Game.party_data.chara_code[slot]).ToArray();
            int[] dead=roles.Select(w.Runtime.MapDisplayId).ToArray();
            var battle=StartNuzlockeBattle(w);
            foreach(int role in roles) KnockOut(w,battle,role);
            w.Runtime.FinishNuzlockeBattle(true);
            int[] replacements=roles.Select(w.Runtime.MapDisplayId).ToArray();
            Check(replacements.Distinct().Count()==roles.Length&&!replacements.Intersect(dead).Any(),"Wipe restored a retired or duplicate fighter");
            Check(roles.All(role=>w.Base(role).hp==w.Base(role).max_hp),"Replacement cast was not healed");
            Check(w.Runtime.TakeNuzlockeRetry()&&!w.Runtime.TakeNuzlockeRetry(),"Retry was missing or consumed twice");
            Check(w.Runtime.TakeNuzlockeRetryFade()&&!w.Runtime.TakeNuzlockeRetryFade(),"Retry fade was missing or repeated");
            var campaign=SavedNuzlocke(w,'5');
            Check(campaign.RetiredHeroIds.OrderBy(x=>x).SequenceEqual(dead.OrderBy(x=>x)),"Wipe retired the wrong native fighter identities");
        });

        Case("Replacing a knocked-out protagonist preserves the plot rune, key items and chosen name",()=>{
            var w=NuzlockeWorld();SetNuzlockeActive(w,8);
            w.Game.playerName="Chosen Hero";
            w.Base(8).monsyo_data!.monsyo_id=ViktorCheckpointData.SoulEaterRune;
            w.Base(8).monsyo_data!.monsyo_level=3;
            w.Base(8).Items=new[]{245,246,247};
            w.Base(8).level=26;
            var battle=StartNuzlockeBattle(w);KnockOut(w,battle,8);w.Runtime.FinishNuzlockeBattle(true);
            Check(w.Runtime.MapDisplayId(8)!=8&&w.Runtime.ProxyFor(8)?.chara_id==8,"Protagonist did not retain the canonical story identity");
            Check(w.Game.playerName=="Chosen Hero"&&w.Base(8).monsyo_data?.monsyo_id==ViktorCheckpointData.SoulEaterRune
                &&w.Base(8).monsyo_data?.monsyo_level==3&&w.Base(8).Items.SequenceEqual(new[]{245,246,247})
                &&w.Base(8).level==26&&w.Base(8).hp==w.Base(8).max_hp,"Protagonist replacement lost plot-critical state");
        });

        Case("Saving during an unfinished battle cannot discard a latched knockout",()=>{
            var w=NuzlockeWorld();var battle=StartNuzlockeBattle(w);KnockOut(w,battle,1);
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('6',64)};
            bool rejected=false;
            try {w.Runtime.WriteCampaignBinding(save);} catch(InvalidOperationException) {rejected=true;}
            Check(rejected&&!NuzlockeBindingStore.Bindings.ContainsKey(save.Identity),"Mid-battle save published incomplete retirement state");
            w.Runtime.FinishNuzlockeBattle(false);
            Check(SavedNuzlocke(w,'6').RetiredHeroIds.Count==1,"Rejected save changed the pending knockout");
        });

        Case("Solo play can consume every reserve and inactive survivor before the run ends",()=>{
            var w=NuzlockeWorld();SetNuzlockeActive(w,8);
            var retired=new HashSet<int>();
            for(int round=0;round<76;round++) {
                int current=w.Runtime.MapDisplayId(8);
                Check(retired.Add(current),"A retired fighter returned during reserve exhaustion");
                var battle=StartNuzlockeBattle(w);KnockOut(w,battle,8);w.Runtime.FinishNuzlockeBattle(true);
                if(round<75) {
                    Check(w.Runtime.TakeNuzlockeRetry(),"Run ended with a living inactive fighter or reserve remaining");
                    w.Runtime.TakeNuzlockeRetryFade();
                }
            }
            Check(retired.Count==76&&w.Runtime.Status.Contains("run over")&&!w.Runtime.TakeNuzlockeRetry(),"Final death failed to end the run");
            bool rejected=false;
            try {SavedNuzlocke(w,'7');} catch(InvalidOperationException) {rejected=true;}
            Check(rejected,"Completed run could still be saved as a playable campaign");
        });

        Case("A failed replacement restores native bases, cache pointers and party layout",()=>{
            var w=NuzlockeWorld();
            var battle=StartNuzlockeBattle(w);KnockOut(w,battle,1);
            string before=AvatarNativeData.Fingerprint(w.Base(1));
            string layout=w.Runtime.CanonicalLayoutFingerprint();
            var resident=w.Game.player_data[w.Game.party_data.chara_code.IndexOf(1)];
            var cache=w.Cache[1];
            AvatarNativeData.FailCopyRoleOnce=1;
            bool rejected=false;
            try {w.Runtime.FinishNuzlockeBattle(false);} catch(InvalidOperationException) {rejected=true;}
            Check(rejected&&w.Runtime.FaultReason is not null,"Failed native write did not stop the campaign");
            Check(before==AvatarNativeData.Fingerprint(w.Base(1))&&layout==w.Runtime.CanonicalLayoutFingerprint()
                &&w.Cache[1]?.Pointer==cache?.Pointer
                &&w.Game.player_data[w.Game.party_data.chara_code.IndexOf(1)]?.Pointer==resident?.Pointer,"Failure published partial native state");
        });
    }
}
