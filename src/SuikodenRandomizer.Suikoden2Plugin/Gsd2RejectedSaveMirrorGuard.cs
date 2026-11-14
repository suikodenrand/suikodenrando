using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Protect the known Fix mirror writer when an owned S2 snapshot is rejected.</summary>
internal static class Gsd2RejectedSaveMirrorGuard
{
    private const string FixAssemblyName = "Suikoden_Fix";
    private const string FixTypeName = "Suikoden_Fix.Patches.EditSavePatch";
    private const string FixSha256 = "4b1a38e7512e4a91cbd86f94d9f233749f83dbb0584979242f8062faf6132f46";
    private const string HarmonyId = "agilen.suikoden2.recruitment_randomizer.camp_rejected_mirror";
    private static readonly Guid FixModuleId = new("bbe7c9c4-4493-4f6a-a91f-2ed9244c3ad9");
    private static readonly object Gate = new();
    private static MethodInfo? _guardedMethod;
    private static Harmony? _harmony;
    private static string _status = "Not checked";

    internal static string Status { get { lock (Gate) return _status; } }

    // This decision is independent of reflection, file access and Harmony.
    internal static bool AllowMirrorWrite()
    {
        var scope = Gsd2SaveScope.Current;
        if (scope is null || !scope.BindCampaign || !Gsd2Runtime.MayOwnSaveContext()) return true;
        if (scope.StorageRejected) return false;
        return !scope.IsCurrent || !scope.InitializeCampPosition || scope.Ready;
    }

    internal static void Ensure()
    {
        lock (Gate)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => assembly.GetName().Name == FixAssemblyName).ToArray();
                if (assemblies.Length == 0)
                {
                    if (_guardedMethod is not null)
                        throw new InvalidOperationException("The previously guarded SuikodenFix assembly is no longer loaded.");
                    _status = "SuikodenFix is not loaded; no mirror guard needed";
                    return;
                }
                if (assemblies.Length != 1)
                    throw new InvalidOperationException("More than one SuikodenFix assembly is loaded.");
                var assembly = assemblies[0];
                if (assembly.GetName().Version != new Version(1, 5, 2, 0) || assembly.ManifestModule.ModuleVersionId != FixModuleId)
                    throw new InvalidOperationException("The loaded SuikodenFix module is not the verified version 1.5.2.");
                if (string.IsNullOrEmpty(assembly.Location))
                    throw new InvalidOperationException("The loaded SuikodenFix module has no verifiable file location.");
                using (var input = File.OpenRead(assembly.Location))
                using (var hash = SHA256.Create())
                    if (!string.Equals(Convert.ToHexString(hash.ComputeHash(input)), FixSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The loaded SuikodenFix file does not match the verified build.");

                var type = assembly.GetType(FixTypeName, throwOnError: false)
                    ?? throw new InvalidOperationException("The verified SuikodenFix mirror writer type is missing.");
                var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == "Save" && method.ReturnType == typeof(void)
                        && method.GetParameters().Select(parameter => parameter.ParameterType)
                            .SequenceEqual(new[] { typeof(string), typeof(string) })).ToArray();
                if (methods.Length != 1 || methods[0].MetadataToken != 0x0600009f)
                    throw new InvalidOperationException("The verified SuikodenFix mirror writer signature changed.");
                var target = methods[0];
                var attributes = CustomAttributeData.GetCustomAttributes(target);
                if (attributes.Count(attribute => attribute.AttributeType == typeof(HarmonyPostfix)) != 1)
                    throw new InvalidOperationException("The SuikodenFix mirror writer is not the verified Harmony postfix.");
                var patches = attributes.Where(attribute => attribute.AttributeType == typeof(HarmonyPatch)).ToArray();
                if (patches.Length != 1 || patches[0].ConstructorArguments.Count != 2
                    || patches[0].ConstructorArguments[0].Value is not Type originalType || originalType.FullName != "SystemSave"
                    || originalType.Assembly.GetName().Name != "GSDShare"
                    || patches[0].ConstructorArguments[1].Value is not string originalName || originalName != "Save")
                    throw new InvalidOperationException("The SuikodenFix mirror writer target declaration changed.");

                var prefix = typeof(Gsd2RejectedSaveMirrorGuard).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("The save mirror guard callback is missing.");
                if (_guardedMethod is not null)
                {
                    if (_guardedMethod != target || !Registered(target, prefix))
                        throw new InvalidOperationException("The registered save mirror guard changed or was removed.");
                    return;
                }
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                if (!Registered(target, prefix))
                    throw new InvalidOperationException("The save mirror guard was not registered.");
                _guardedMethod = target;
                _status = "Verified SuikodenFix 1.5.2 mirror writer guarded for rejected Suikoden II saves";
            }
            catch (Exception exception)
            {
                _status = "Suikoden II save mirror protection unavailable: " + exception.Message;
                throw new InvalidOperationException("Suikoden II cannot protect rejected native saves from the loaded SuikodenFix mirror writer: " + exception.Message, exception);
            }
        }
    }

    private static bool Registered(MethodInfo target, MethodInfo prefix)
        => Harmony.GetPatchInfo(target)?.Prefixes.Any(patch => patch.owner == HarmonyId && patch.PatchMethod == prefix) == true;

    // Patch only the verified managed postfix method. The original shared save
    // arguments, other mods and every unrelated or admitted mirror write remain intact.
    private static bool Prefix() => AllowMirrorWrite();
}
