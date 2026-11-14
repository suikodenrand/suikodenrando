using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SuikodenRandomizer.Unified;

/// <summary>Reads PE metadata only; never loads or executes a candidate assembly.</summary>
internal static class PluginInventoryAudit
{
    private static readonly HashSet<string> ConflictingGuids = new(StringComparer.Ordinal)
    {
        "agilen.suikoden.recruitment_randomizer",
        "agilen.suikoden2.recruitment_randomizer",
        "agilen.starsofdestiny"
    };

    internal static string[] FindConflicts(string pluginRoot, string ownAssemblyFile)
    {
        string root = Path.GetFullPath(pluginRoot);
        string own = Path.GetFullPath(ownAssemblyFile);
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        var conflicts = new List<string>();
        foreach (string candidate in Directory.EnumerateFiles(root, "*.dll", options))
        {
            string file = Path.GetFullPath(candidate);
            if (!string.Equals(file, own, StringComparison.OrdinalIgnoreCase) && HasConflictingPlugin(file))
                conflicts.Add(file);
        }
        return conflicts.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasConflictingPlugin(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            if (!pe.HasMetadata) return false;
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return false;
            foreach (var handle in metadata.TypeDefinitions)
            {
                var definition = metadata.GetTypeDefinition(handle);
                if (metadata.GetString(definition.Namespace) == "Suikoden_Fix.Patches"
                    && metadata.GetString(definition.Name) == "StartingPartyVirtualizerPatch") return true;
                foreach (var attributeHandle in definition.GetCustomAttributes())
                {
                    var attribute = metadata.GetCustomAttribute(attributeHandle);
                    EntityHandle declaringType;
                    if (attribute.Constructor.Kind == HandleKind.MemberReference)
                    {
                        var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                        if (metadata.GetString(constructor.Name) != ".ctor") continue;
                        declaringType = constructor.Parent;
                    }
                    else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
                    {
                        var constructor = metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                        if (metadata.GetString(constructor.Name) != ".ctor") continue;
                        declaringType = constructor.GetDeclaringType();
                    }
                    else continue;

                    string name, ns;
                    if (declaringType.Kind == HandleKind.TypeReference)
                    {
                        var type = metadata.GetTypeReference((TypeReferenceHandle)declaringType);
                        name = metadata.GetString(type.Name); ns = metadata.GetString(type.Namespace);
                    }
                    else if (declaringType.Kind == HandleKind.TypeDefinition)
                    {
                        var type = metadata.GetTypeDefinition((TypeDefinitionHandle)declaringType);
                        name = metadata.GetString(type.Name); ns = metadata.GetString(type.Namespace);
                    }
                    else continue;
                    if (ns != "BepInEx" || name != "BepInPlugin") continue;

                    var blob = metadata.GetBlobReader(attribute.Value);
                    if (blob.ReadUInt16() != 1) continue;
                    string? guid = blob.ReadSerializedString();
                    if (guid is not null && ConflictingGuids.Contains(guid)) return true;
                }
            }
            return false;
        }
        catch (BadImageFormatException) { return false; } // Native/non-PE or malformed metadata.
        catch (EndOfStreamException) { return false; }
        // Access/enumeration failures propagate: an unreadable inventory cannot
        // be reported as clean before the host patches either engine.
    }
}
