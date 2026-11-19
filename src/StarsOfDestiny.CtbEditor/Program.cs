namespace StarsOfDestiny.CtbEditor;

internal static class Program
{
    internal const string GameExe = "Suikoden I and II HD Remaster.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool verify = args.Length == 2 && args[0] == "--verify-ui";
        try
        {
            if (verify) EditorForm.Verify(args[1]);
            else
            {
                string? folder = args switch
                {
                    [] => FindNearbyGame(AppContext.BaseDirectory),
                    ["--game-path", string path] => path,
                    _ => throw new ArgumentException("Use --game-path followed by the game folder.")
                };
                using var form = new EditorForm(folder);
                Application.Run(form);
            }
            return 0;
        }
        catch (Exception error)
        {
            if (verify) Console.Error.WriteLine(error);
            else MessageBox.Show(error.Message, "Suikoden I CTB editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string? FindNearbyGame(string from)
    {
        for (var dir = new DirectoryInfo(from); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, GameExe))) return dir.FullName;
        return null;
    }
}
