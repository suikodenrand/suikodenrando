extern alias GSD1;

using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>
/// Content-addressed bindings survive moving/copying save slots. This digest
/// identifies a save snapshot; it is not an integrity hash of every save byte.
/// Version upgrades and native Mace weapon normalization are deliberately absent.
/// </summary>
internal static class AvatarSaveIdentity
{
    public static string Compute(GSD1.SaveDataManager.SaveData save)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write("SRR-save-identity-v1");
            writer.Write(save.playerName ?? string.Empty);
            writer.Write(save.playerCName ?? string.Empty);
            writer.Write(save.date_time_now ?? string.Empty);
            writer.Write(save.playTime);
            WriteBytes(writer, save.member_flag);
            WriteBytes(writer, save.storyFlagS);
            WriteBytes(writer, save.tmpEventFlagS);
            var party = save.party_data ?? throw new InvalidDataException("Save has no party data");
            writer.Write(party.vil_no); writer.Write(party.map_no); writer.Write(party.area_no);
            writer.Write(party.x); writer.Write(party.y); writer.Write(party.mochi_kin);
            writer.Write(party.player_kazu);
            writer.Write(party.chara_code.Count);
            for (int i = 0; i < party.chara_code.Count; i++) writer.Write(party.chara_code[i]);
            writer.Write(party.narabi.Count);
            for (int i = 0; i < party.narabi.Count; i++) writer.Write(party.narabi[i]);
            writer.Write(party.b_chara.Count);
            for (int i = 0; i < party.b_chara.Count; i++) writer.Write(party.b_chara[i]);
            var bases = save.player_base ?? throw new InvalidDataException("Save has no player bases");
            writer.Write(bases.Count);
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                writer.Write(b is not null);
                if (b is null) continue;
                writer.Write(b.chara_no); writer.Write(b.level); writer.Write(b.exp);
                writer.Write(b.hp); writer.Write(b.max_hp);
            }
        }
        return Hash(stream.ToArray());
    }

    private static void WriteBytes(BinaryWriter writer, Il2CppStructArray<byte>? bytes)
    {
        writer.Write(bytes?.Count ?? -1);
        if (bytes is null) return;
        for (int i = 0; i < bytes.Count; i++) writer.Write(bytes[i]);
    }

    internal static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(bytes)).ToLowerInvariant();
    }
}
