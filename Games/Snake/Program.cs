using TrayGames.Core;

namespace TrayGames.Snake;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new SnakeGame(), args);
}
