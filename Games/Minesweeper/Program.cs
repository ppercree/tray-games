using TrayGames.Core;

namespace TrayGames.Minesweeper;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new MinesweeperGame(), args);
}
