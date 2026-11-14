extern alias GSD1;

using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Core.Treasure;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BepInEx
{
 public static class Paths { public static string ConfigPath { get; set; } = Path.Combine(Path.GetTempPath(), "srr-runtime-adapter-tests-" + Guid.NewGuid().ToString("N")); }
}
namespace UnityEngine.SceneManagement
{
 public readonly struct Scene { public string name => SceneManager.SceneName; }
 public static class SceneManager { public static string SceneName { get; set; } = "GSD1"; public static Scene GetActiveScene() => new Scene(); }
}
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class)]
 internal sealed class HarmonyPatch : Attribute
 {
  public HarmonyPatch() { }
  public HarmonyPatch(Type declaringType, string methodName) { }
  public HarmonyPatch(Type declaringType, string methodName, Type[] arguments) { }
 }
 [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
 [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
 [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
 [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyTargetMethods : Attribute { }
 internal static class AccessTools
 {
  internal static System.Reflection.MethodInfo? Method(Type type,string name,Type[]? args=null) =>
   args is null ? type.GetMethod(name) : type.GetMethod(name,args);
 }
}
namespace Il2CppSystem
{
 internal delegate void Action<T>(T value);
 internal delegate void Action<T, U>(T value, U other);
}
internal static class SystemSave
{
 internal enum DataSize { Small }
 internal static void Save() { }
}
namespace SuikodenRandomizer.AvatarPlugin
{
 internal sealed class TestLogger
 {
  internal List<string> Messages { get; } = new List<string>();
  internal void LogInfo(object message) => Messages.Add(message.ToString() ?? string.Empty);
  internal void LogWarning(object message) => Messages.Add(message.ToString() ?? string.Empty);
 }
 internal static class AvatarPlugin
 {
  internal static string ConfiguredSeed { get; set; } = "agilen-080";
  internal static string ConfiguredMode { get; set; } = "Shuffle";
  internal static string ConfiguredCompanions { get; set; } = string.Empty;
  internal static string ConfiguredJoiningWeaponLevel { get; set; } = "MatchRole";
  internal static string ConfiguredJoiningEquipment { get; set; } = "Donor";
  internal static bool ConfiguredIncludeTir { get; set; }
  internal static bool ConfiguredTreasureShuffle { get; set; }
  internal static bool ConfiguredTreasureEnabled { get; set; } = true;
  internal static bool SkipToViktor { get; set; }
  internal static TestLogger PluginLog { get; } = new TestLogger();
 }
 internal static class AvatarPresentation
 {
  internal static int Resets;
  internal static bool ResolvingFaces => false;
  internal static bool ResolvingNames => false;
  internal static void Reset() => Resets++;
 }
 internal static class AvatarSaveIdentity
 {
  internal static string Compute(GSD1.SaveDataManager.SaveData save) => save.Identity;
  internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 }
 internal static class AvatarBindingStore
 {
  internal static Dictionary<string,string> Bindings { get; } = new Dictionary<string,string>();
  internal static string Build => new string('b',64);
  internal static void CheckWritable() { }
  internal static void Write(AvatarShuffleProfile profile, AvatarShuffleCampaignState state, string id) =>
   Bindings[id] = AvatarShuffleBindingJson.Serialize(profile,state,id,Build);
  internal static AvatarShuffleCampaignState? Read(string id) => Bindings.TryGetValue(id,out string? json)
   ? AvatarShuffleBindingJson.Deserialize(json,id,Build) : null;
 }
 internal sealed class TreasureBinding(TreasureShuffleProfile? profile)
 {
  internal TreasureShuffleProfile? Profile { get; } = profile;
 }
 internal static class TreasureBindingStore
 {
  internal static Dictionary<string, TreasureBinding> Bindings { get; } = new();
  internal static void CheckWritable() { }
  internal static TreasureBinding? Read(string identity, string campaignFingerprint, string seed) =>
   Bindings.TryGetValue(identity, out var binding) ? binding : null;
  internal static void Write(string identity, string campaignFingerprint, string seed, TreasureShuffleProfile? profile) =>
   Bindings[identity] = new TreasureBinding(profile);
 }
 // This suite models native PLAYER_BASE through the same fake fields used by
 // AvatarNativeData. Real sidecar serialization is checked separately.
 internal sealed class FixedRosterBaseSnapshot
 {
  private readonly GSD1.PLAYER_BASE _copy;
  private FixedRosterBaseSnapshot(GSD1.PLAYER_BASE source) => _copy = AvatarNativeData.CloneBase(source);
  public int CharacterId { get => _copy.chara_no; set => _copy.chara_no = value; }
  public byte[] Growth { get => _copy.seicho_type; set => _copy.seicho_type = value; }
  public ushort Hp { get => _copy.hp; set => _copy.hp = value; }
  public ushort MaxHp => _copy.max_hp;
  public ushort Status { get => _copy.status; set => _copy.status = value; }
  internal static FixedRosterBaseSnapshot Capture(GSD1.PLAYER_BASE source) => new FixedRosterBaseSnapshot(source);
  internal void ValidateState() => AvatarNativeData.ValidateBase(_copy);
  internal GSD1.PLAYER_BASE ToNative() => AvatarNativeData.CloneBase(_copy);
 }
 internal sealed class FixedRosterBinding
 {
  public string Seed { get; set; } = string.Empty;
  public int[] Companions { get; set; } = Array.Empty<int>();
  public Dictionary<int, int> Assignments { get; set; } = new();
  public Dictionary<int, FixedRosterBaseSnapshot> CompanionStates { get; set; } = new();
  public Dictionary<int, FixedRosterBaseSnapshot> RoleBases { get; set; } = new();
 }
 internal static class FixedRosterBindingStore
 {
  internal static Dictionary<string, FixedRosterBinding> Bindings { get; } = new();
  internal static bool HasBindings => Bindings.Count != 0;
  internal static void CheckWritable() { }
  internal static string ComputeIdentity(GSD1.SaveDataManager.SaveData save) => save.Identity;
  internal static FixedRosterBinding? Read(string identity) =>
   Bindings.TryGetValue(identity, out var binding) ? Clone(binding) : null;
  internal static void Write(string identity, FixedRosterBinding binding) => Bindings.Add(identity, Clone(binding));
  // The real JSON store reconstructs dictionaries on every read. Keeping live
  // dictionary aliases here would let a runtime reset erase the fake save.
  private static FixedRosterBinding Clone(FixedRosterBinding binding) => new()
  {
   Seed=binding.Seed,Companions=(int[])binding.Companions.Clone(),Assignments=new(binding.Assignments),
   CompanionStates=new(binding.CompanionStates),RoleBases=new(binding.RoleBases)
  };
 }
 internal static class AvatarNativeData
 {
  internal static int CopyCalls;
  internal static int? FailCopyRoleOnce;
  internal static void ValidateBase(GSD1.PLAYER_BASE value)
  {
   if (value.level == 0 || value.max_hp == 0 || !value.IsComplete) throw new InvalidOperationException("Incomplete model base.");
  }
  internal static GSD1.PLAYER_BASE FindBase(int id)
  {
   var found=GSD1.OldSrcBase.game_work!.player_base.Where(p=>p?.chara_no==id).ToArray();
   if(found.Length!=1 || found[0] is null) throw new InvalidOperationException("Missing/duplicate model base.");
   var result=found[0]!; ValidateBase(result); return result;
  }
  internal static byte[] CloneBytes(byte[] value) => (byte[])value.Clone();
  internal static GSD1.PLAYER_BASE CloneBase(GSD1.PLAYER_BASE v)
  {
   ValidateBase(v);
   return new GSD1.PLAYER_BASE {chara_no=v.chara_no,level=v.level,hp=v.hp,max_hp=v.max_hp,exp=v.exp,status=v.status,
    noryoku=v.noryoku.ToList(),monsyo_data=CloneRune(v.monsyo_data),buki_data=new GSD1.PLAYER_BUKI{buki_id=v.buki_data.buki_id,level=v.buki_data.level},
    seicho_type=CloneBytes(v.seicho_type),IsComplete=v.IsComplete,Equipment=v.Equipment,Rune=v.Rune,Items=(int[])v.Items.Clone(),
    item_kazu=v.item_kazu,item=CloneInventory(v.item)};
  }
  private static GSD1.Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GSD1.PLAYER_ITEM> CloneInventory(
   GSD1.Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GSD1.PLAYER_ITEM> items)
  {
   var result=new GSD1.Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GSD1.PLAYER_ITEM>(items.Count);
   for(int index=0;index<items.Count;index++)result[index]=new GSD1.PLAYER_ITEM{item_id=items[index].item_id,soubi=items[index].soubi,data=items[index].data};
   return result;
  }
  private static GSD1.PLAYER_MONSYO? CloneRune(GSD1.PLAYER_MONSYO? rune) => rune is null ? null
   : new GSD1.PLAYER_MONSYO {monsyo_id=rune.monsyo_id,monsyo_level=rune.monsyo_level,monsyo_exp=rune.monsyo_exp};
  internal static void CopyBase(GSD1.PLAYER_BASE from, GSD1.PLAYER_BASE to)
  {
   CopyCalls++;
   if(FailCopyRoleOnce==to.chara_no){FailCopyRoleOnce=null;throw new InvalidOperationException("Injected model setter failure.");}
   to.level=from.level;to.hp=from.hp;to.max_hp=from.max_hp;to.exp=from.exp;to.status=from.status;
   to.seicho_type=CloneBytes(from.seicho_type);to.IsComplete=from.IsComplete;to.Equipment=from.Equipment;
   to.Rune=from.Rune;to.Items=(int[])from.Items.Clone();
   to.item=CloneInventory(from.item);to.item_kazu=from.item_kazu;
   to.noryoku=from.noryoku.ToList();to.monsyo_data=CloneRune(from.monsyo_data);
   to.buki_data=new GSD1.PLAYER_BUKI{buki_id=from.buki_data.buki_id,level=from.buki_data.level};
  }
  internal static string Fingerprint(GSD1.PLAYER_BASE value)
  {
   ValidateBase(value);
   return string.Join("|",value.chara_no,value.level,value.hp,value.max_hp,value.exp,value.status,value.Equipment,value.Rune,value.buki_data.buki_id,value.buki_data.level,
    value.monsyo_data?.monsyo_id,value.monsyo_data?.monsyo_level,value.monsyo_data?.monsyo_exp,string.Join(",",value.noryoku),
    string.Join(",",value.Items),string.Join(",",value.seicho_type),value.item_kazu,
    string.Join(";",value.item.Select(item=>item.item_id+":"+item.soubi+":"+item.data)));
  }
  internal static GSD1.PLAYER_DATA CreateProxy(GSD1.PLAYER_DATA original,GSD1.PLAYER_DATA donor,GSD1.PLAYER_BASE roleBase,
   bool keepCanonicalLoadout=false) =>
   new GSD1.PLAYER_DATA {chara_id=original.chara_id,chara_name=original.chara_id==CombatCatalog.TirId?original.chara_name:donor.chara_name,player_base=roleBase,
    field_data=donor.field_data,battle_data=donor.battle_data,seicho_type=CloneBytes(keepCanonicalLoadout?original.seicho_type:donor.seicho_type),
    soubi_type=donor.soubi_type,sex=donor.sex};
 }
}
namespace SuikodenRandomizer.Plugin
{
 internal static class BuildCompatibility { internal static string GameAssemblySha256 => new string('b',64); }
}
