using TrayGames.Core;

namespace TrayGames.Stacks;

internal static class Program
{
    [STAThread]
    static void Main(string[] args) => TrayGameApp.Run(() => new StacksGame(), args);
}
