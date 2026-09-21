using TrayGames.Core;

namespace TrayGames.TicTacToe;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new TicTacToeGame(), args);
}
