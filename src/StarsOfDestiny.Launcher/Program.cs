using System;
using System.Windows.Forms;

namespace StarsOfDestiny.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? folder = null;
        string? previewFolder = null;
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (args[index] == "--game-path") folder = args[index + 1];
            if (args[index] == "--render-previews") previewFolder = args[index + 1];
        }
        var form = new MainForm(folder);
        if (previewFolder is not null)
            form.Shown += (_, _) => form.BeginInvoke(() => form.RenderPreviews(previewFolder));
        Application.Run(form);
    }
}
