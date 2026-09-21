using TrayGames.Core;

namespace TrayGames.TicTacToe;

internal enum Phase { YourTurn, Thinking, Over }

internal sealed class TicTacToeGame : GameControl
{
    const int Empty = 0, You = 1, Ai = 2;
    const int CellPx = 60;

    static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
    };

    readonly int[] board = new int[9];
    readonly System.Windows.Forms.Timer thinkTimer = new() { Interval = 320 };
    readonly Random rng = new();

    readonly Setting opponentSetting;
    readonly Setting firstSetting;

    double blunder;
    bool aiStartsNext;

    Phase phase;
    int winner;
    int[]? winLine;
    int cursor = 4;
    bool cursorVisible;
    int drawStreak, bestStreak;

    public TicTacToeGame() : base(CellPx * 3, CellPx * 3)
    {
        bestStreak = ScoreStore.Load(Title);
        opponentSetting = AddSetting("opponent", "Opponent",
            new[] { "Careless", "Casual", "Sharp", "Perfect" }, 1);
        firstSetting = AddSetting("first", "First move", new[] { "You", "Opponent", "Alternate" }, 0);
        thinkTimer.Tick += (_, _) => { thinkTimer.Stop(); PlayAi(); };
        ApplySettings();
    }

    protected override void ApplySettings()
    {
        // Strength is a chance of throwing the move away rather than a shallower
        // search: at 3x3 a shallow search is still perfect, so the only way to
        // be beatable is to genuinely blunder sometimes.
        blunder = opponentSetting.Index switch
        {
            0 => 0.45,
            1 => 0.22,
            2 => 0.07,
            _ => 0.0,
        };
        aiStartsNext = false;
        NewGame();
    }

    public override string Title => "Tic Tac Toe";

    public override string Status => phase switch
    {
        Phase.Thinking => "thinking...",
        Phase.Over when winner == You => $"you win!      unbeaten {drawStreak}   best {bestStreak}",
        Phase.Over when winner == Ai => $"lost      best unbeaten {bestStreak}",
        Phase.Over => $"draw      unbeaten {drawStreak}   best {bestStreak}",
        _ => $"your turn (X)      unbeaten {drawStreak}   best {bestStreak}",
    };

    public override void NewGame()
    {
        Array.Clear(board);
        winner = Empty;
        winLine = null;
        thinkTimer.Stop();

        bool aiFirst = firstSetting.Index switch
        {
            1 => true,
            2 => aiStartsNext,
            _ => false,
        };
        if (firstSetting.Index == 2) aiStartsNext = !aiStartsNext;

        if (aiFirst)
        {
            phase = Phase.Thinking;
            thinkTimer.Start();
        }
        else
        {
            phase = Phase.YourTurn;
        }

        Announce();
    }

    public override void SuspendForMenu() => thinkTimer.Stop();

    // ---- rules ------------------------------------------------------------

    static int WinnerOf(int[] b, out int[]? line)
    {
        foreach (var l in Lines)
            if (b[l[0]] != Empty && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]])
            {
                line = l;
                return b[l[0]];
            }
        line = null;
        return Empty;
    }

    static bool Full(int[] b) => b.All(v => v != Empty);

    void Place(int index, int player)
    {
        board[index] = player;
        winner = WinnerOf(board, out winLine);

        if (winner != Empty || Full(board))
        {
            phase = Phase.Over;
            if (winner == Empty)
            {
                drawStreak++;
                if (drawStreak > bestStreak) { bestStreak = drawStreak; ScoreStore.Save(Title, bestStreak); }
            }
            else if (winner == Ai)
            {
                drawStreak = 0;
            }
            else
            {
                drawStreak++;
                if (drawStreak > bestStreak) { bestStreak = drawStreak; ScoreStore.Save(Title, bestStreak); }
            }
            Announce();
            return;
        }

        if (player == You)
        {
            phase = Phase.Thinking;
            thinkTimer.Start();
        }
        else
        {
            phase = Phase.YourTurn;
        }
        Announce();
    }

    void PlayAi()
    {
        if (phase != Phase.Thinking) return;
        int move = BestMove();
        if (move >= 0) Place(move, Ai);
    }

    /// Full minimax over a 3x3 board: only a few thousand nodes, so there is no
    /// reason to approximate. The depth term makes it prefer the quickest win
    /// and the most drawn-out loss, which is what reads as "playing properly".
    int BestMove()
    {
        var free = Enumerable.Range(0, 9).Where(i => board[i] == Empty).ToArray();
        if (free.Length == 0) return -1;
        if (blunder > 0 && rng.NextDouble() < blunder) return free[rng.Next(free.Length)];

        int bestScore = int.MinValue, move = -1;
        for (int i = 0; i < 9; i++)
        {
            if (board[i] != Empty) continue;
            board[i] = Ai;
            int score = Minimax(board, You, 1);
            board[i] = Empty;
            if (score > bestScore) { bestScore = score; move = i; }
        }
        return move;
    }

    static int Minimax(int[] b, int turn, int depth)
    {
        int w = WinnerOf(b, out _);
        if (w == Ai) return 10 - depth;
        if (w == You) return depth - 10;
        if (Full(b)) return 0;

        int best = turn == Ai ? int.MinValue : int.MaxValue;
        for (int i = 0; i < 9; i++)
        {
            if (b[i] != Empty) continue;
            b[i] = turn;
            int score = Minimax(b, turn == Ai ? You : Ai, depth + 1);
            b[i] = Empty;
            best = turn == Ai ? Math.Max(best, score) : Math.Min(best, score);
        }
        return best;
    }

    // ---- input ------------------------------------------------------------

    protected override bool OnGameKey(Keys key)
    {
        switch (key)
        {
            case Keys.Left: return MoveCursor(-1, 0);
            case Keys.Right: return MoveCursor(1, 0);
            case Keys.Up: return MoveCursor(0, -1);
            case Keys.Down: return MoveCursor(0, 1);
            case Keys.Space:
            case Keys.Enter:
                if (phase == Phase.Over) NewGame();
                else if (phase == Phase.YourTurn && board[cursor] == Empty)
                {
                    cursorVisible = true;
                    Place(cursor, You);
                }
                return true;
            default:
                return false;
        }
    }

    bool MoveCursor(int dx, int dy)
    {
        cursorVisible = true;
        int x = Math.Clamp(cursor % 3 + dx, 0, 2);
        int y = Math.Clamp(cursor / 3 + dy, 0, 2);
        cursor = y * 3 + x;
        Announce();
        return true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase == Phase.Over) { NewGame(); return; }
        if (phase != Phase.YourTurn) return;

        var (cell, ox, oy) = BoardLayout();
        int x = (e.X - ox) / cell, y = (e.Y - oy) / cell;
        if (x < 0 || y < 0 || x > 2 || y > 2) return;

        int index = y * 3 + x;
        if (board[index] != Empty) return;
        cursor = index;
        Place(index, You);
    }

    (int cell, int ox, int oy) BoardLayout()
    {
        int cell = Math.Max(12, Math.Min(ClientSize.Width, ClientSize.Height) / 3);
        return (cell, (ClientSize.Width - cell * 3) / 2, (ClientSize.Height - cell * 3) / 2);
    }

    // ---- painting ---------------------------------------------------------

    protected override void PaintGame(Graphics g)
    {
        var (cell, ox, oy) = BoardLayout();
        var board3 = new RectangleF(ox, oy, cell * 3, cell * 3);
        Draw.FillRounded(g, board3, 6, Theme.BoardInner);

        using (var grid = new Pen(Theme.Separator, 1.5f))
            for (int i = 1; i < 3; i++)
            {
                g.DrawLine(grid, ox + i * cell, oy + 8, ox + i * cell, oy + cell * 3 - 8);
                g.DrawLine(grid, ox + 8, oy + i * cell, ox + cell * 3 - 8, oy + i * cell);
            }

        for (int i = 0; i < 9; i++)
        {
            float cx = ox + (i % 3) * cell + cell / 2f;
            float cy = oy + (i / 3) * cell + cell / 2f;
            float r = cell * 0.26f;

            if (board[i] == You)
            {
                using var pen = new Pen(Theme.Cyan, 4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                g.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
            }
            else if (board[i] == Ai)
            {
                using var pen = new Pen(Theme.Orange, 4f);
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            }
        }

        if (cursorVisible && phase == Phase.YourTurn && board[cursor] == Empty)
        {
            var r = new RectangleF(ox + (cursor % 3) * cell + 6, oy + (cursor / 3) * cell + 6, cell - 12, cell - 12);
            Draw.DrawRounded(g, r, 4, Theme.TextDim, 1.4f);
        }

        if (winLine is not null)
        {
            float Cx(int i) => ox + (i % 3) * cell + cell / 2f;
            float Cy(int i) => oy + (i / 3) * cell + cell / 2f;
            using var pen = new Pen(Color.FromArgb(220, winner == You ? Theme.Cyan : Theme.Orange), 5f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };
            g.DrawLine(pen, Cx(winLine[0]), Cy(winLine[0]), Cx(winLine[2]), Cy(winLine[2]));
        }

        if (phase == Phase.Over)
        {
            string big = winner == You ? "YOU WIN" : winner == Ai ? "AI WINS" : "DRAW";
            DrawOverlay(g, board3, big, "click or space to play again", "", 150);
        }
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        using (var grid = new Pen(Theme.TextDim, 2 * u))
        {
            g.DrawLine(grid, 12 * u, 3 * u, 12 * u, 29 * u);
            g.DrawLine(grid, 21 * u, 3 * u, 21 * u, 29 * u);
            g.DrawLine(grid, 3 * u, 12 * u, 29 * u, 12 * u);
            g.DrawLine(grid, 3 * u, 21 * u, 29 * u, 21 * u);
        }
        using (var x = new Pen(Theme.Cyan, 3 * u))
        {
            g.DrawLine(x, 5 * u, 5 * u, 10 * u, 10 * u);
            g.DrawLine(x, 10 * u, 5 * u, 5 * u, 10 * u);
        }
        using var o = new Pen(Theme.Orange, 3 * u);
        g.DrawEllipse(o, 23 * u, 23 * u, 6 * u, 6 * u);
    }

    /// Plays whole games against a plausible casual human (take the win, block
    /// the loss, otherwise play at random) so the difficulty dial can be shown
    /// to do something rather than asserted to.
    internal (int wins, int draws, int losses) Simulate(int games, int opponentIndex)
    {
        double savedBlunder = blunder;
        var savedBoard = (int[])board.Clone();
        blunder = opponentIndex switch { 0 => 0.45, 1 => 0.22, 2 => 0.07, _ => 0.0 };

        int wins = 0, draws = 0, losses = 0;
        for (int g = 0; g < games; g++)
        {
            Array.Clear(board);
            int turn = You;
            while (true)
            {
                int w = WinnerOf(board, out _);
                if (w == You) { wins++; break; }
                if (w == Ai) { losses++; break; }
                if (Full(board)) { draws++; break; }

                board[turn == You ? HumanMove() : BestMove()] = turn;
                turn = turn == You ? Ai : You;
            }
        }

        blunder = savedBlunder;
        savedBoard.CopyTo(board, 0);
        return (wins, draws, losses);
    }

    int HumanMove()
    {
        var free = Enumerable.Range(0, 9).Where(i => board[i] == Empty).ToArray();
        foreach (int who in new[] { You, Ai })
            foreach (int i in free)
            {
                board[i] = who;
                bool decisive = WinnerOf(board, out _) == who;
                board[i] = Empty;
                if (decisive) return i;
            }
        return free[rng.Next(free.Length)];
    }

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        var (cell, ox, oy) = BoardLayout();
        Point At(int i) => new(ox + (i % 3) * cell + cell / 2, oy + (i / 3) * cell + cell / 2);

        // Take a corner, then the opposite corner: against a perfect opponent
        // this is the classic double-attack try, and it must be refused.
        await ctx.Click(At(0).X, At(0).Y, settle: 500);
        ctx.Log($"you take 0    : ai replied={board.Count(v => v == Ai)} centre={board[4]}");

        int free = Enumerable.Range(0, 9).First(i => board[i] == Empty);
        await ctx.Click(At(free).X, At(free).Y, settle: 500);
        ctx.Log($"you take {free}    : phase={phase} board=[{string.Join(",", board)}]");
        ctx.Snap("1-midgame");

        // Play the game out by always taking the first free square; a perfect
        // opponent should never let that win.
        for (int guard = 0; guard < 9 && phase != Phase.Over; guard++)
        {
            if (phase != Phase.YourTurn) { await ctx.Delay(200); continue; }
            int next = Enumerable.Range(0, 9).FirstOrDefault(i => board[i] == Empty, -1);
            if (next < 0) break;
            await ctx.Click(At(next).X, At(next).Y, settle: 450);
        }

        ctx.Log($"result        : winner={(winner == You ? "you" : winner == Ai ? "ai" : "draw")} phase={phase}");
        ctx.Log($"board         : [{string.Join(",", board)}]");
        ctx.Snap("2-result");

        foreach (int level in new[] { 0, 1, 2, 3 })
        {
            var (wins, draws, losses) = Simulate(300, level);
            ctx.Log($"vs {opponentSetting.Options[level],-9}: human wins {wins,3}  draws {draws,3}  losses {losses,3}  (300 games)");
        }
    }
}
