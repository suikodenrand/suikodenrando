using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

public sealed class SeedManifest
{
    public const int CurrentSchemaVersion = 2;
    public const int CurrentAlgorithmVersion = 2;
    public const string SupportedGame = "suikoden1-hd-remaster";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int AlgorithmVersion { get; set; } = CurrentAlgorithmVersion;
    public string Game { get; set; } = SupportedGame;
    public string Seed { get; set; } = string.Empty;
    public string Profile { get; set; } = SeedProfiles.OptionalRecruitsV1;

    /// <summary>
    /// A closed permutation of recruitment roles in the selected profile.
    /// Canonical source flags remain the source of recruitment progress.
    /// </summary>
    public Dictionary<int, int> Mapping { get; set; } = new Dictionary<int, int>();

    // The experimental profile explicitly declares its startup roles.
    public StartingPartySettings StartingParty { get; set; } = new StartingPartySettings();
    public DiagnosticSettings Diagnostics { get; set; } = new DiagnosticSettings();
}

public sealed class StartingPartySettings
{
    public bool Enabled { get; set; }
    public List<int> SourceIds { get; set; } = new List<int>();
}

public sealed class DiagnosticSettings
{
    public bool TracePlayerLoading { get; set; } = true;
    public bool TracePartyInitialization { get; set; } = true;
}
