extern alias GSD2;

using System;
using System.IO;
using System.Text.Json;
using HarmonyLib;

namespace SuikodenRandomizer.Suikoden2Plugin;

// The first native save precedes MachiInit, so its ordinary player-coordinate
// source is absent. Change only that save's temporary snapshot, before the
// original serializer runs; later saves retain their actual player position.
internal static class Gsd2InitialSavePosition
{
    internal static void Prepare(GSD2.GSD2SaveData.TmpSave snapshot, Gsd2SaveScope scope)
    {
        if (!scope.InitializeCampPosition || !scope.BindCampaign) return;
        if (scope.CampPositionFailure is not null) throw new InvalidDataException(scope.CampPositionFailure);
        if (snapshot.Pointer == IntPtr.Zero || snapshot.version != 100 || snapshot.game_data is null)
            throw new InvalidDataException("Camp initial snapshot has an unexpected native schema.");
        var location = snapshot.game_data;
        if (location.area_no != 1 || location.town_no != 10 || location.map_no != 2 || location.mem_type != 1
            || location.area_no2 != 1 || location.town_no2 != 1)
            throw new InvalidDataException("Camp initial snapshot has the wrong native destination.");
        if (scope.CampSnapshot != IntPtr.Zero)
        {
            if (scope.CampSnapshot != snapshot.Pointer || !scope.CampPositionPrepared
                || snapshot.px != Gsd2CampStart.SavePx || snapshot.py != Gsd2CampStart.SavePy)
                throw new InvalidDataException("Camp initial serialization received a different or changed snapshot.");
            return;
        }
        int previousX = snapshot.px, previousY = snapshot.py;
        scope.CampSnapshot = snapshot.Pointer;
        try
        {
            snapshot.px = Gsd2CampStart.SavePx; snapshot.py = Gsd2CampStart.SavePy;
            scope.CampPositionPrepared = true;
        }
        catch
        {
            // A failed temporary snapshot never reaches storage. Preserve its
            // original coordinates where the native setters still permit it.
            try { snapshot.px = previousX; } catch { }
            try { snapshot.py = previousY; } catch { }
            throw;
        }
    }

    internal static void ValidateStorage(string json, Gsd2SaveScope scope)
    {
        if (!scope.InitializeCampPosition) return;
        if (!scope.CampPositionPrepared || scope.CampPositionFailure is not null)
            throw new InvalidDataException(scope.CampPositionFailure ?? "Camp initial snapshot did not pass the native serializer boundary.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireInteger(root, "version", 100);
        RequireInteger(root, "px", Gsd2CampStart.SavePx);
        RequireInteger(root, "py", Gsd2CampStart.SavePy);
        JsonElement game = Unique(root, "game_data");
        RequireInteger(game, "area_no", 1); RequireInteger(game, "town_no", 10);
        RequireInteger(game, "map_no", 2); RequireInteger(game, "mem_type", 1);
        RequireInteger(game, "area_no2", 1); RequireInteger(game, "town_no2", 1);
        // Validate the exact string subsequently passed to BindSnapshot and
        // native storage. Do not parse/reserialize or normalize unknown data.
    }

    private static JsonElement Unique(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Camp snapshot requires an object.");
        int matches = 0; JsonElement value = default;
        foreach (var property in parent.EnumerateObject())
            if (property.NameEquals(name)) { matches++; value = property.Value; }
        if (matches != 1) throw new InvalidDataException("Camp snapshot requires one " + name + " field.");
        return value;
    }
    private static void RequireInteger(JsonElement parent, string name, int expected)
    {
        var value = Unique(parent, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int actual) || actual != expected)
            throw new InvalidDataException("Camp snapshot has an invalid " + name + ".");
    }
}

[HarmonyPatch(typeof(global::LitJson.JsonMapper), "ToJson", new Type[] { typeof(Il2CppSystem.Object) })]
internal static class Gsd2InitialSnapshotPositionPatch
{
    [HarmonyPrefix]
    private static void Prefix(Il2CppSystem.Object __0)
    {
        var scope = Gsd2SaveScope.Current;
        if (scope?.InitializeCampPosition != true || !scope.IsCurrent || !scope.BindCampaign || Gsd2Plugin.NativeCheckpointCapture) return;
        try
        {
            var snapshot = __0?.TryCast<GSD2.GSD2SaveData.TmpSave>();
            if (snapshot is not null) Gsd2InitialSavePosition.Prepare(snapshot, scope);
        }
        catch (Exception exception)
        {
            scope.CampPositionFailure = exception.Message;
            scope.Runtime?.Fail("Suikoden II camp initial serialization: " + exception.Message);
        }
    }
}
