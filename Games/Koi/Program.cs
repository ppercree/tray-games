using TrayGames.Core;

namespace TrayGames.Koi;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new KoiGame(), args);
}
