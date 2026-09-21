using TrayGames.Core;

namespace TrayGames.Tetris;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new TetrisGame(), args);
}
