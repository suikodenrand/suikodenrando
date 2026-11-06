extern alias GSD1;

using SuikodenRandomizer.Core.Avatars;
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
  internal static TestLogger PluginLog { get; } = new TestLogger();
 }
 internal static class AvatarPresentation { internal static int Resets; internal static void Reset() => Resets++; }
 internal static class AvatarSaveIdentity
 {
  internal static string Compute(GSD1.SaveDataManager.SaveData save) => save.Identity;
  internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 }
 internal static class AvatarBindingStore
 {
  internal static Dictionary<string,string> Bindings { get; } = new Dictionary<string,string>();
  internal static string Build => new string('b',64);
  internal static AvatarShuffleCampaignState? Read(string id) => Bindings.TryGetValue(id,out string? json)
   ? AvatarShuffleBindingJson.Deserialize(json,id,Build) : null;
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
    seicho_type=CloneBytes(v.seicho_type),IsComplete=v.IsComplete,Equipment=v.Equipment,Rune=v.Rune,Items=(int[])v.Items.Clone()};
  }
  internal static void CopyBase(GSD1.PLAYER_BASE from, GSD1.PLAYER_BASE to)
  {
   CopyCalls++;
   if(FailCopyRoleOnce==to.chara_no){FailCopyRoleOnce=null;throw new InvalidOperationException("Injected model setter failure.");}
   to.level=from.level;to.hp=from.hp;to.max_hp=from.max_hp;to.exp=from.exp;to.status=from.status;
   to.seicho_type=CloneBytes(from.seicho_type);to.IsComplete=from.IsComplete;to.Equipment=from.Equipment;
   to.Rune=from.Rune;to.Items=(int[])from.Items.Clone();
  }
  internal static string Fingerprint(GSD1.PLAYER_BASE value)
  {
   ValidateBase(value);
   return string.Join("|",value.chara_no,value.level,value.hp,value.max_hp,value.exp,value.status,value.Equipment,value.Rune,
    string.Join(",",value.Items),string.Join(",",value.seicho_type));
  }
  internal static GSD1.PLAYER_DATA CreateProxy(GSD1.PLAYER_DATA original,GSD1.PLAYER_DATA donor,GSD1.PLAYER_BASE roleBase) =>
   new GSD1.PLAYER_DATA {chara_id=original.chara_id,chara_name=donor.chara_name,player_base=roleBase,
    field_data=donor.field_data,battle_data=donor.battle_data,seicho_type=CloneBytes(donor.seicho_type)};
 }
}
