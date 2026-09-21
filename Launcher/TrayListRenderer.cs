using TrayGames.Core;

namespace TrayGames.Launcher;

/// The tray list stays bare - icon and name - so the only state it shows is an
/// outline around the games currently in the tray.
internal sealed class TrayListRenderer : DarkRenderer
{
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderMenuItemBackground(e);

        if (e.Item.Tag is not GameEntry entry || !entry.IsRunning) return;

        // Item background rendering happens in item-local coordinates.
        var r = new RectangleF(1.5f, 1f, e.Item.Width - 3f, e.Item.Height - 2f);
        Draw.DrawRounded(e.Graphics, r, 4, Theme.Green, 1.4f);
    }
}
