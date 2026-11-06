using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

/// <summary>
/// Narrow contract observed in one English/HD Kai recruitment trace. This is
/// not a general scene catalogue. Script bytes absent from that trace are
/// deliberately unspecified, not invented as a full script checksum.
/// </summary>
public static class KaiSceneContract
{
    public const int SourceId = 91;
    public const int EventRecordIndex = 10;
    public const int ActorSlot = 8;
    public const int ScriptLength = 49;

    private static readonly byte[] KnownAt5 =
    {
        0x1C, 0x00, 0x02, 0x01, 0x02, 0x0C, 0x41, 0x96,
        0x00, 0x00, 0x0A, 0x0A, 0x0C, 0x02, 0x12, 0x00
    };
    private static readonly byte[] KnownAt22 =
    {
        0x1C, 0x00, 0x01, 0x01, 0x02, 0x01, 0x00, 0x46,
        0x45, 0x5B, 0x45, 0x5B, 0x4B, 0x00, 0x5B, 0x14,
        0x24, 0x3A, 0x08, 0x0A, 0x05, 0x25, 0xFD
    };

    public static bool MatchesMap(int village, int map, int area, bool hdMode, int language) =>
        village == 3 && map == 0 && area == 1 && hdMode && language == 1;

    /// <summary>
    /// Definition fields available before InitHumanSprite creates the actor.
    /// The native routine writes putno only after its three sprite calls, so
    /// pre-initialization selection must not depend on the later putno value.
    /// </summary>
    public static bool MatchesEventDefinition(int eventRecordIndex, int image,
        int x, int y, int flags, IReadOnlyList<byte>? script) =>
        eventRecordIndex == EventRecordIndex && image == SourceId
        && x == 54 && y == 12 && flags == 3
        && script is not null && script.Count == ScriptLength
        && MatchesBytes(script, 5, KnownAt5) && MatchesBytes(script, 22, KnownAt22);

    /// <summary>The observed actor identity additionally required during dialogue.</summary>
    public static bool MatchesEvent(int eventRecordIndex, int image, int putno,
        int x, int y, int flags, IReadOnlyList<byte>? script) =>
        putno == ActorSlot && MatchesEventDefinition(eventRecordIndex, image, x, y, flags, script);

    public static bool IsSpeakerCommand(int eventNo, int window, int sourceId, int commandIndex) =>
        eventNo == EventRecordIndex && window == 0 && sourceId == SourceId
        && (commandIndex == 5 || commandIndex == 22);

    /// <summary>
    /// Restricts the pilot to the source's four standard field animation slots.
    /// Native code must separately check the actual OPD entries and resources;
    /// counts alone do not establish animation semantics or loaded textures.
    /// </summary>
    public static bool SupportsAnimation(int requested, int animationCount, int tableCount) =>
        requested >= 0 && requested < 4 && animationCount >= 4 && tableCount >= 4;

    private static bool MatchesBytes(IReadOnlyList<byte> script, int start, byte[] expected)
    {
        for (int index = 0; index < expected.Length; index++)
            if (script[start + index] != expected[index]) return false;
        return true;
    }
}

/// <summary>
/// Immutable assignment captured for one run. Native scene scopes also bind
/// the game/map/event pointers separately; this object only guards seed/run
/// identity and never stores a caller-owned mutable manifest.
/// </summary>
public sealed class KaiSceneBinding
{
    private KaiSceneBinding(int rewardId, string fingerprint, long generation)
    {
        RewardId = rewardId;
        SeedFingerprint = fingerprint;
        RunGeneration = generation;
    }

    public int SourceId => KaiSceneContract.SourceId;
    public int RewardId { get; }
    public string SeedFingerprint { get; }
    public long RunGeneration { get; }

    public static KaiSceneBinding Capture(SeedManifest seed, long generation)
    {
        if (seed is null) throw new ArgumentNullException(nameof(seed));
        if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
        SeedValidator.ValidateAndThrow(seed);
        return new KaiSceneBinding(seed.Mapping[KaiSceneContract.SourceId],
            global::SuikodenRandomizer.Core.SeedFingerprint.Compute(seed), generation);
    }

    public bool IsCurrent(long generation, string? fingerprint) =>
        generation == RunGeneration && string.Equals(fingerprint, SeedFingerprint, StringComparison.Ordinal);
}
