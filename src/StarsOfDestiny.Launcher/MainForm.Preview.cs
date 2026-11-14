using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StarsOfDestiny.Launcher;

internal sealed partial class MainForm
{
    // Render the app's own forms for layout verification without desktop capture or config writes.
    internal void RenderPreviews(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            for (int index = 0; index < _tabs.TabPages.Count; index++)
            {
                _tabs.SelectedIndex = index;
                PerformLayout();
                Refresh();
                using var image = new Bitmap(Width, Height);
                DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
                // Native combo boxes omit their selected text when Windows renders WM_PRINT.
                using (var graphics = Graphics.FromImage(image)) DrawComboText(graphics, this);
                string name = string.Join("-", _tabs.TabPages[index].Text.Split(Path.GetInvalidFileNameChars().Concat(new[] { ' ', '&' }).ToArray(), StringSplitOptions.RemoveEmptyEntries));
                image.Save(Path.Combine(directory, index + "-" + name + ".png"), ImageFormat.Png);
            }
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory, "render-error.txt"), error.ToString()); }
        finally { Close(); }
    }

    private void DrawComboText(Graphics graphics, Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            if (child is ComboBox combo && combo.SelectedItem is not null)
            {
                Point origin = combo.PointToScreen(Point.Empty);
                using var background = new SolidBrush(combo.Enabled ? combo.BackColor : SystemColors.Control);
                graphics.FillRectangle(background, origin.X - Left + 1, origin.Y - Top + 1,
                    combo.Width - SystemInformation.VerticalScrollBarWidth - 2, combo.Height - 2);
                var bounds = new Rectangle(origin.X - Left + 3, origin.Y - Top + 2,
                    combo.Width - SystemInformation.VerticalScrollBarWidth - 6, combo.Height - 4);
                TextRenderer.DrawText(graphics, combo.GetItemText(combo.SelectedItem), combo.Font, bounds,
                    combo.Enabled ? combo.ForeColor : SystemColors.GrayText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            DrawComboText(graphics, child);
        }
    }
}
