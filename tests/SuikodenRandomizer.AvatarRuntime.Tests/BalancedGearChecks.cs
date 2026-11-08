extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class Program
{
    private const ushort EarlyRobe=100,MidArmor=101,LateArmor=102,EarlyHeavy=103,PurchasedArmor=104,Medicine=200;

    private static void PrepareGearCatalog()
    {
        if(GSD1.G_item_c_data.item_data_table.Count>1) return;
        var table=Enumerable.Range(0,201).Select(_=>(GSD1.ITEM_DATA?)null).ToList();
        GSD1.BOUGU_DATA Armor(int price,byte mask,byte defense,byte speed=0)
        {
            var bonuses=new List<byte>(new byte[8]);bonuses[7]=defense;bonuses[2]=speed;
            return new GSD1.BOUGU_DATA{nedan=price,type=0xA0,soubi_type=mask,equip_data=bonuses};
        }
        table[EarlyRobe]=Armor(100,0x38,1);
        table[EarlyHeavy]=Armor(150,4,2);
        table[MidArmor]=Armor(1000,4,20);
        table[LateArmor]=Armor(120000,4,63,20);
        table[PurchasedArmor]=Armor(50000,4,40,5);
        table[Medicine]=new GSD1.ITEM_DATA{nedan=10,type=1};
        GSD1.G_item_c_data.item_data_table=table;
    }

    private static void SetStartingKit(GSD1.PLAYER_BASE value,byte weaponLevel,ushort armor,bool spare=false)
    {
        value.WeaponId=(byte)(40+value.chara_no%50);value.WeaponLevel=weaponLevel;
        value.WeaponRunes=new byte[]{1,2,3,4,5,6};value.MagicPoints=new byte[]{7,6,5,4};
        value.monsyo_data!.monsyo_id=60;value.monsyo_data.monsyo_level=3;value.monsyo_data.monsyo_exp=777;
        value.GearItems=Enumerable.Range(0,9).Select(_=>new GSD1.PLAYER_ITEM()).ToList();
        value.GearItems[0]=new GSD1.PLAYER_ITEM{item_id=armor,soubi=130,data=4};
        value.GearItems[1]=new GSD1.PLAYER_ITEM{item_id=Medicine,data=9};
        if(spare) value.GearItems[2]=new GSD1.PLAYER_ITEM{item_id=LateArmor,data=1};
        value.ItemCount=(byte)(spare?3:2);
    }

    private static void PrepareStartingKits(World world)
    {
        foreach(int role in CombatCatalog.NormalCombatIds) {
            SetStartingKit(world.Base(role),16,LateArmor,true);
            world.Originals[role].soubi_type=4;
        }
        SetStartingKit(world.Base(8),1,EarlyRobe);
        world.Originals[8].soubi_type=0x38;
        SetStartingKit(world.Base(1),2,EarlyRobe);
        world.Originals[1].soubi_type=0x38;
    }

    private static string FighterIdentityFingerprint(GSD1.PLAYER_BASE value) => string.Join("|",
        value.level,value.max_hp,value.exp,value.WeaponId,string.Join(",",value.WeaponRunes),
        value.monsyo_data?.monsyo_id,value.monsyo_data?.monsyo_level,value.monsyo_data?.monsyo_exp,
        string.Join(",",value.noryoku),string.Join(",",value.MagicPoints),string.Join(",",value.seicho_type));

    private static void PurchaseUpgrade(GSD1.PLAYER_BASE value)
    {
        value.WeaponLevel=14;value.GearItems[0].item_id=PurchasedArmor;value.exp=912;
    }

    private static void RunBalancedGearChecks()
    {
        Case("Balanced shuffle uses each canonical recruitment tier and preserves fighter identity",()=>{
            var w=new World(false);PrepareStartingKits(w);
            var profile=AvatarShuffleProfile.Create(AvatarPlugin.ConfiguredSeed);
            int donor=profile.GetDonor(1);
            int lateRole=profile.RoleIds.First(role=>role!=1&&role!=donor&&profile.GetDonor(role)!=1&&profile.GetDonor(role)!=role);
            SetStartingKit(w.Base(lateRole),9,MidArmor);
            string incoming=FighterIdentityFingerprint(w.Base(donor));
            string tir=AvatarNativeData.Fingerprint(w.Base(8));
            AvatarPlugin.ConfiguredBalancedGear=true;w.Runtime.BeginNewGame();
            AvatarPlugin.ConfiguredBalancedGear=false; // Campaign preference was captured at New Game.
            w.Ready();
            Check(w.Base(1).WeaponLevel==2&&w.Base(1).GearItems[0].item_id==EarlyHeavy
                &&w.Base(1).GearItems[0].soubi==130&&w.Base(1).GearItems[2].item_id==EarlyHeavy,
                "An early role retained late-game sharpening, worn armor or spare armor");
            Check(w.Base(lateRole).WeaponLevel==9&&w.Base(lateRole).GearItems[0].item_id==MidArmor,
                "A later canonical recruitment slot was flattened to the opening tier");
            Check(FighterIdentityFingerprint(w.Base(1))==incoming&&w.Base(1).GearItems[1].item_id==Medicine
                &&w.Base(1).GearItems[1].data==9,"Gear balancing changed weapon identity, runes, stats or ordinary items");
            Check(AvatarNativeData.Fingerprint(w.Base(8))==tir,"Balancing changed native Tir");
            PurchaseUpgrade(w.Base(1));
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('a',64)};
            w.Runtime.WriteCampaignBinding(save);
            string earned=AvatarNativeData.Fingerprint(w.Base(1));
            AvatarPlugin.ConfiguredBalancedGear=false;w.Runtime.PrepareLoad(save);w.Ready();
            Check(AvatarNativeData.Fingerprint(w.Base(1))==earned,"Balanced shuffle reload reset purchased gear or sharpening");
        });

        Case("Original shuffle preserves donor starting gear and ignores a changed reload preference",()=>{
            var w=new World(false);PrepareStartingKits(w);
            int donor=AvatarShuffleProfile.Create(AvatarPlugin.ConfiguredSeed).GetDonor(1);
            var expected=AvatarNativeData.CloneBase(w.Base(donor));expected.chara_no=1;
            expected.seicho_type=AvatarNativeData.CloneBytes(w.Originals[donor].seicho_type);
            w.Runtime.BeginNewGame();w.Ready();
            Check(AvatarNativeData.Fingerprint(w.Base(1))==AvatarNativeData.Fingerprint(expected)
                &&w.Base(1).WeaponLevel==16&&w.Base(1).GearItems[0].item_id==LateArmor,"Original mode changed the donor kit");
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('b',64)};
            w.Runtime.WriteCampaignBinding(save);string before=AvatarNativeData.Fingerprint(w.Base(1));
            AvatarPlugin.ConfiguredBalancedGear=true;w.Runtime.PrepareLoad(save);w.Ready();
            Check(AvatarNativeData.Fingerprint(w.Base(1))==before,"Loading an Original campaign applied current balancing preferences");
        });

        Case("A balanced fixed roster normalizes every template once and retains earned upgrades on return",()=>{
            var w=new World(false);PrepareStartingKits(w);w.StageOpening();
            AvatarPlugin.ConfiguredMode="FixedChosen";AvatarPlugin.ConfiguredCompanions="11,12,13,14,15";
            AvatarPlugin.ConfiguredBalancedGear=true;w.Runtime.BeginNewGame();w.Ready();
            w.Game.party_data.narabi[1]=4;w.Game.party_data.player_kazu=2;w.Ready();
            Check(w.Base(108).WeaponLevel==1&&w.Base(108).GearItems[0].item_id==EarlyHeavy,"Fixed companion retained late-game starting gear");
            PurchaseUpgrade(w.Base(108));
            w.Base(8).WeaponLevel=10;
            w.Game.party_data.narabi[2]=2;w.Game.party_data.player_kazu=3;w.Ready();
            Check(w.Runtime.MapDisplayId(6)==12&&w.Base(6).WeaponLevel==1,"An unused fixed fighter was rebalanced against later protagonist upgrades");
            w.Game.party_data.narabi[1]=2;w.Game.party_data.narabi[2]=-1;w.Game.party_data.player_kazu=2;w.Ready();
            w.Game.party_data.narabi[1]=4;w.Game.party_data.narabi[2]=2;w.Game.party_data.player_kazu=3;w.Ready();
            Check(w.Base(108).WeaponLevel==14&&w.Base(108).GearItems[0].item_id==PurchasedArmor,"A returning fixed fighter lost earned upgrades");
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('c',64)};
            w.Runtime.WriteCampaignBinding(save);string before=AvatarNativeData.Fingerprint(w.Base(108));
            AvatarPlugin.ConfiguredBalancedGear=false;w.Runtime.PrepareLoad(save);w.Runtime.CompleteFixedLoad();w.Ready();
            Check(AvatarNativeData.Fingerprint(w.Base(108))==before,"Fixed roster reload changed earned gear after a config change");
        });

        Case("Nuzlocke replacements inherit purchased gear while a later role uses its saved balancing preference",()=>{
            var w=new World(false);PrepareStartingKits(w);
            AvatarPlugin.ConfiguredMode="Nuzlocke";AvatarPlugin.ConfiguredBalancedGear=true;w.Runtime.BeginNewGame();w.Ready();
            Check(w.Base(1).WeaponLevel==2&&w.Base(1).GearItems[0].item_id==EarlyHeavy,"Nuzlocke opening gear was not balanced");
            PurchaseUpgrade(w.Base(1));
            var battle=StartNuzlockeBattle(w);KnockOut(w,battle,1);w.Runtime.FinishNuzlockeBattle(false);
            Check(w.Base(1).WeaponLevel==14&&w.Base(1).GearItems[0].item_id==PurchasedArmor,"KO replacement rebalanced purchased gear");
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('d',64)};
            w.Runtime.WriteCampaignBinding(save);string before=AvatarNativeData.Fingerprint(w.Base(1));
            Check(NuzlockeBindingStore.Bindings[save.Identity].BalancedStartingGear,"Nuzlocke save omitted the preference for future roles");
            AvatarPlugin.ConfiguredBalancedGear=false;w.Runtime.PrepareLoad(save);w.Runtime.CompleteNuzlockeLoad();w.Ready();
            Check(AvatarNativeData.Fingerprint(w.Base(1))==before,"Nuzlocke reload reset current fighter gear");
            var state=NuzlockeCampaign.Restore(NuzlockeBindingStore.Bindings[save.Identity].Campaign);
            int later=state.RoleIds.First(role=>CombatCatalog.IsNormalCombat(role)&&role!=1&&role!=2&&role!=6
                &&state.GetDonor(role)>=0&&state.GetDonor(role)!=1);
            SetStartingKit(w.Base(later),9,MidArmor);
            SetNuzlockeActive(w,8,1,later);
            Check(w.Base(later).WeaponLevel==9&&w.Base(later).GearItems[0].item_id!=LateArmor,
                "A new Nuzlocke role used current config or the opening tier instead of the saved policy and native role tier");
            Check(w.Base(1).WeaponLevel==14&&w.Base(1).GearItems[0].item_id==PurchasedArmor,"A later party change reset a surviving fighter's upgrades");
        });

        Case("An Original Nuzlocke save does not balance a later role after the website preference changes",()=>{
            var w=new World(false);PrepareStartingKits(w);
            AvatarPlugin.ConfiguredMode="Nuzlocke";w.Runtime.BeginNewGame();w.Ready();
            var save=new GSD1.SaveDataManager.SaveData{Identity=new string('e',64)};
            w.Runtime.WriteCampaignBinding(save);string before=AvatarNativeData.Fingerprint(w.Base(1));
            var state=NuzlockeCampaign.Restore(NuzlockeBindingStore.Bindings[save.Identity].Campaign);
            int later=state.RoleIds.First(role=>CombatCatalog.IsNormalCombat(role)&&role!=1&&role!=2&&role!=6
                &&state.GetDonor(role)>=0&&state.GetDonor(role)!=1);
            AvatarPlugin.ConfiguredBalancedGear=true;w.Runtime.PrepareLoad(save);w.Runtime.CompleteNuzlockeLoad();w.Ready();
            Check(AvatarNativeData.Fingerprint(w.Base(1))==before,"Original Nuzlocke reload changed starting equipment");
            SetStartingKit(w.Base(later),9,MidArmor);SetNuzlockeActive(w,8,1,later);
            Check(w.Base(later).WeaponLevel==16&&w.Base(later).GearItems[0].item_id==LateArmor,
                "Original Nuzlocke campaign adopted balancing for a later role");
        });
    }
}
