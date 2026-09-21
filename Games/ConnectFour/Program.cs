using TrayGames.Core;

namespace TrayGames.ConnectFour;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new ConnectFourGame(), args);
}
