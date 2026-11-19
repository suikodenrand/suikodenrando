namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class AvatarRuntime
{
    public static AvatarRuntime? Current { get; set; } = new();
    public bool OwnsExecution { get; set; } = true;
}
internal static class AvatarPlugin
{
    internal sealed class Logger { public void LogInfo(string message) { } }
    internal static Logger PluginLog { get; } = new();
}
