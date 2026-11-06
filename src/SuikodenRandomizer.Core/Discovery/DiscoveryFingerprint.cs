using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Discovery;

/// <summary>
/// Canonical structural hash. Observation times and plugin versions do not
/// create variants. Native addresses and live wrappers have no representation.
/// Null, empty, missing and truncated records remain distinct.
/// </summary>
public static class DiscoveryFingerprint
{
    public static string Compute(DiscoveryMapSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write("SRR:event-discovery:1");
            writer.Write(snapshot.SchemaVersion);
            Text(writer, snapshot.GameBuildSha256);
            writer.Write(snapshot.Village); writer.Write(snapshot.Map); writer.Write(snapshot.Area);
            writer.Write(snapshot.HdMode); writer.Write(snapshot.Language); Text(writer, snapshot.Chapter);
            Text(writer, snapshot.EventsState); Number(writer, snapshot.DeclaredEventCount);
            Events(writer, snapshot.Events);
            Integers(writer, snapshot.PartyMemberIds);
            Text(writer, snapshot.SourceTableState);
            Text(writer, snapshot.SourceEventsState); Number(writer, snapshot.DeclaredSourceEventCount);
            Events(writer, snapshot.SourceEvents);
            writer.Write(snapshot.MapMetadata.Count);
            foreach (var pair in snapshot.MapMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            { Text(writer, pair.Key); writer.Write(pair.Value); }
            Integers(writer, snapshot.MapMessageIds);
            Text(writer, snapshot.NpcImagesState); Number(writer, snapshot.DeclaredNpcCount);
            writer.Write(snapshot.NpcImages.Count);
            foreach (DiscoveryNpcRecord npc in snapshot.NpcImages)
            {
                writer.Write(npc.Index); writer.Write(npc.IsNull); Number(writer, npc.FpNo);
                Number(writer, npc.OpdIndex); Text(writer, npc.OpdState); Text(writer, npc.OpdId);
                Text(writer, npc.OpdPath); Number(writer, npc.OpdAnimationCount); Text(writer, npc.Issue);
            }
            writer.Write(snapshot.IsComplete); Strings(writer, snapshot.Issues);
        }
        buffer.Position = 0;
        using var sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(buffer);
        var text = new StringBuilder(digest.Length * 2);
        foreach (byte value in digest) text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }

    private static void Events(BinaryWriter writer, List<DiscoveryEventRecord> events)
    {
        writer.Write(events.Count);
        foreach (DiscoveryEventRecord record in events)
        {
            writer.Write(record.Index); writer.Write(record.IsNull);
            writer.Write(record.X); writer.Write(record.Y); writer.Write(record.Flag1);
            writer.Write(record.Image); writer.Write(record.PutNo);
            Bytes(writer, record.EventPut); Bytes(writer, record.EventForm); Bytes(writer, record.EventCom);
            Strings(writer, record.Issues);
        }
    }

    private static void Bytes(BinaryWriter writer, DiscoveryByteArray array)
    {
        Text(writer, array.State); Number(writer, array.DeclaredLength); Text(writer, array.Issue);
        writer.Write(array.Values is not null);
        if (array.Values is null) return;
        writer.Write(array.Values.Length); writer.Write(array.Values);
    }

    private static void Integers(BinaryWriter writer, DiscoveryIntArray array)
    {
        Text(writer, array.State); Number(writer, array.DeclaredLength); Text(writer, array.Issue);
        writer.Write(array.Values is not null);
        if (array.Values is null) return;
        writer.Write(array.Values.Length);
        foreach (int value in array.Values) writer.Write(value);
    }

    private static void Number(BinaryWriter writer, int? value)
    { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }

    private static void Text(BinaryWriter writer, string? value)
    { writer.Write(value is not null); if (value is not null) writer.Write(value); }

    private static void Strings(BinaryWriter writer, List<string> values)
    { writer.Write(values.Count); foreach (string value in values) Text(writer, value); }
}
