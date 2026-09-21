using TrayGames.Core;

namespace TrayGames.ConnectFour;

internal enum Phase { YourTurn, Dropping, Thinking, Over }

internal sealed class ConnectFourGame : GameControl
{
    const int Empty = 0, You = 1, Ai = 2;
    const int Cols = 7, Rows = 6;
    const int CellPx = 26;
    const int TopPx = 20;

    /// Centre columns first: a connect four position is decided in the middle,
    /// so this ordering is what makes alpha-beta cut anything at all.
    static readonly int[] ColumnOrder = { 3, 2, 4, 1, 5, 0, 6 };

    readonly int[,] cells = new int[Cols, Rows];
    readonly System.Windows.Forms.Timer dropTimer = new() { Interval = 16 };
    readonly Random rng = new();

    readonly Setting opponentSetting;
    readonly Setting firstSetting;

    int searchDepth = 4;
    double blunder;

    Phase phase;
    int winner;
    List<Point>? winCells;
    int cursor = 3;
    int wins, best;

    int dropCol, dropTargetRow, dropPlayer;
    float dropY;

    public ConnectFourGame() : base(Cols * CellPx, Rows * CellPx + TopPx)
    {
        best = ScoreStore.Load(Title);
        opponentSetting = AddSetting("opponent", "Opponent",
            new[] { "Careless", "Casual", "Sharp", "Ruthless" }, 1);
        firstSetting = AddSetting("first", "First move", new[] { "You", "Opponent", "Alternate" }, 0);
        dropTimer.Tick += (_, _) => StepDrop();
        ApplySettings();
    }

    protected override void ApplySettings()
    {
        // Depth alone is a poor difficulty dial here: even two ply blocks every
        // immediate threat, so the weaker settings also throw moves away.
        (searchDepth, blunder) = opponentSetting.Index switch
        {
            0 => (2, 0.40),
            1 => (4, 0.15),
            2 => (6, 0.03),
            _ => (8, 0.0),
        };
        aiStartsNext = false;
        NewGame();
    }

    public override string Title => "Connect Four";

    public override string Status => phase switch
    {
        Phase.Thinking => "thinking...",
        Phase.Over when winner == You => $"you win!      wins {wins}   best {best}",
        Phase.Over when winner == Ai => $"lost      wins {wins}   best {best}",
        Phase.Over => $"draw      wins {wins}   best {best}",
        _ => $"your turn      wins {wins}   best {best}",
    };

    bool aiStartsNext;

    public override void NewGame()
    {
        Array.Clear(cells);
        winner = Empty;
        winCells = null;
        dropTimer.Stop();

        bool aiFirst = firstSetting.Index switch
        {
            1 => true,
            2 => aiStartsNext,
            _ => false,
        };
        if (firstSetting.Index == 2) aiStartsNext = !aiStartsNext;

        phase = Phase.YourTurn;
        Announce();
        if (aiFirst) StartThinking();
    }

    public override void SuspendForMenu() => dropTimer.Stop();

    // ---- rules ------------------------------------------------------------

    bool CanDrop(int col) => col >= 0 && col < Cols && cells[col, 0] == Empty;

    int LandingRow(int col)
    {
        for (int y = Rows - 1; y >= 0; y--)
            if (cells[col, y] == Empty) return y;
        return -1;
    }

    static List<Point>? FourFrom(int[,] b, int x, int y, int dx, int dy)
    {
        int who = b[x, y];
        if (who == Empty) return null;

        var run = new List<Point> { new(x, y) };
        for (int i = 1; i < 4; i++)
        {
            int nx = x + dx * i, ny = y + dy * i;
            if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows || b[nx, ny] != who) return null;
            run.Add(new Point(nx, ny));
        }
        return run;
    }

    static int FindWinner(int[,] b, out List<Point>? line)
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                {
                    var run = FourFrom(b, x, y, dx, dy);
                    if (run is not null) { line = run; return b[x, y]; }
                }
        line = null;
        return Empty;
    }

    bool BoardFull()
    {
        for (int x = 0; x < Cols; x++)
            if (cells[x, 0] == Empty) return false;
        return true;
    }

    // ---- dropping ---------------------------------------------------------

    void BeginDrop(int col, int player)
    {
        int row = LandingRow(col);
        if (row < 0) return;

        dropCol = col;
        dropTargetRow = row;
        dropPlayer = player;
        dropY = -1f;
        phase = Phase.Dropping;
        dropTimer.Start();
        Announce();
    }

    void StepDrop()
    {
        // Accelerating fall, so a disc into an empty column visibly gathers
        // speed instead of sliding down at a constant rate.
        dropY += 0.28f + Math.Max(0f, dropY) * 0.16f;
        if (dropY < dropTargetRow) { Invalidate(); return; }

        dropTimer.Stop();
        dropY = dropTargetRow;
        cells[dropCol, dropTargetRow] = dropPlayer;

        winner = FindWinner(cells, out winCells);
        if (winner != Empty || BoardFull())
        {
            phase = Phase.Over;
            if (winner == You)
            {
                wins++;
                if (wins > best) { best = wins; ScoreStore.Save(Title, best); }
            }
            Announce();
            return;
        }

        if (dropPlayer == You) StartThinking();
        else { phase = Phase.YourTurn; Announce(); }
    }

    async void StartThinking()
    {
        phase = Phase.Thinking;
        Announce();

        // The search runs off the UI thread: a frozen dropdown during the AI's
        // turn would look like the whole menu had hung.
        var snapshot = (int[,])cells.Clone();
        int depth = searchDepth;
        int move = await Task.Run(() => BestColumn(snapshot, depth));

        if (blunder > 0 && rng.NextDouble() < blunder)
        {
            var open = Enumerable.Range(0, Cols).Where(c => snapshot[c, 0] == Empty).ToArray();
            if (open.Length > 0) move = open[rng.Next(open.Length)];
        }

        if (phase != Phase.Thinking) return;
        if (move < 0) { phase = Phase.Over; Announce(); return; }
        BeginDrop(move, Ai);
    }

    // ---- search -----------------------------------------------------------

    static int BestColumn(int[,] b, int searchDepth)
    {
        int bestCol = -1, bestScore = int.MinValue;
        foreach (int col in ColumnOrder)
        {
            if (b[col, 0] != Empty) continue;
            int row = LandingRowIn(b, col);
            b[col, row] = Ai;
            int score = Search(b, searchDepth - 1, int.MinValue, int.MaxValue, false);
            b[col, row] = Empty;
            if (score > bestScore) { bestScore = score; bestCol = col; }
        }
        return bestCol;
    }

    static int LandingRowIn(int[,] b, int col)
    {
        for (int y = Rows - 1; y >= 0; y--)
            if (b[col, y] == Empty) return y;
        return -1;
    }

    static int Search(int[,] b, int depth, int alpha, int beta, bool aiTurn)
    {
        int w = FindWinner(b, out _);
        // Depth is folded into the score so a win now beats a win later, and a
        // loss later beats a loss now - otherwise the AI dawdles when winning
        // and gives up early when losing.
        if (w == Ai) return 1_000_000 + depth;
        if (w == You) return -1_000_000 - depth;

        bool anyMove = false;
        for (int x = 0; x < Cols; x++) if (b[x, 0] == Empty) { anyMove = true; break; }
        if (!anyMove) return 0;
        if (depth == 0) return Evaluate(b);

        int best = aiTurn ? int.MinValue : int.MaxValue;
        foreach (int col in ColumnOrder)
        {
            if (b[col, 0] != Empty) continue;
            int row = LandingRowIn(b, col);
            b[col, row] = aiTurn ? Ai : You;
            int score = Search(b, depth - 1, alpha, beta, !aiTurn);
            b[col, row] = Empty;

            if (aiTurn)
            {
                best = Math.Max(best, score);
                alpha = Math.Max(alpha, score);
            }
            else
            {
                best = Math.Min(best, score);
                beta = Math.Min(beta, score);
            }
            if (beta <= alpha) break;
        }
        return best;
    }

    static int Evaluate(int[,] b)
    {
        int score = 0;

        for (int y = 0; y < Rows; y++)
            if (b[Cols / 2, y] != Empty)
                score += b[Cols / 2, y] == Ai ? 6 : -6;

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                {
                    int ex = x + dx * 3, ey = y + dy * 3;
                    if (ex < 0 || ey < 0 || ex >= Cols || ey >= Rows) continue;

                    int ai = 0, you = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int v = b[x + dx * i, y + dy * i];
                        if (v == Ai) ai++;
                        else if (v == You) you++;
                    }

                    if (ai > 0 && you > 0) continue;
                    if (ai == 3) score += 60;
                    else if (ai == 2) score += 12;
                    else if (you == 3) score -= 80;   // blocking is worth more
                    else if (you == 2) score -= 14;   // than building
                }

        return score;
    }

    // ---- input ------------------------------------------------------------

    protected override bool OnGameKey(Keys key)
    {
        switch (key)
        {
            case Keys.Left:
            case Keys.Q:
            case Keys.A:
                cursor = Math.Max(0, cursor - 1); Announce(); return true;
            case Keys.Right:
            case Keys.D:
                cursor = Math.Min(Cols - 1, cursor + 1); Announce(); return true;
            case Keys.Space:
            case Keys.Enter:
            case Keys.Down:
                if (phase == Phase.Over) NewGame();
                else if (phase == Phase.YourTurn && CanDrop(cursor)) BeginDrop(cursor, You);
                return true;
            default:
                return false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int col = Math.Clamp(e.X / CellPx, 0, Cols - 1);
        if (col == cursor) return;
        cursor = col;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (phase == Phase.Over) { NewGame(); return; }
        if (phase != Phase.YourTurn) return;

        int col = Math.Clamp(e.X / CellPx, 0, Cols - 1);
        if (CanDrop(col)) BeginDrop(col, You);
    }

    // ---- painting ---------------------------------------------------------

    static Color DiscColor(int who) => who == You ? Theme.Yellow : Theme.Red;

    protected override void PaintGame(Graphics g)
    {
        var board = new RectangleF(0, TopPx, Cols * CellPx, Rows * CellPx);

        if (phase == Phase.YourTurn && CanDrop(cursor))
        {
            using var hint = new SolidBrush(Color.FromArgb(40, Theme.Yellow));
            g.FillRectangle(hint, cursor * CellPx, TopPx, CellPx, Rows * CellPx);
            using var disc = new SolidBrush(Theme.Yellow);
            g.FillEllipse(disc, cursor * CellPx + 6, 3, CellPx - 12, CellPx - 12);
        }

        Draw.FillRounded(g, board, 6, Theme.Tile);

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                var hole = new RectangleF(x * CellPx + 3, TopPx + y * CellPx + 3, CellPx - 6, CellPx - 6);
                int who = cells[x, y];
                using var brush = new SolidBrush(who == Empty ? Theme.BoardInner : DiscColor(who));
                g.FillEllipse(brush, hole);
            }

        if (phase == Phase.Dropping)
        {
            float y = TopPx + Math.Max(-1f, dropY) * CellPx;
            using var brush = new SolidBrush(DiscColor(dropPlayer));
            g.FillEllipse(brush, dropCol * CellPx + 3, y + 3, CellPx - 6, CellPx - 6);
        }

        if (winCells is not null)
            foreach (var p in winCells)
            {
                var hole = new RectangleF(p.X * CellPx + 3, TopPx + p.Y * CellPx + 3, CellPx - 6, CellPx - 6);
                using var pen = new Pen(Theme.Text, 2f);
                g.DrawEllipse(pen, hole);
            }

        if (phase == Phase.Over)
        {
            string big = winner == You ? "YOU WIN" : winner == Ai ? "AI WINS" : "DRAW";
            DrawOverlay(g, board, big, "click or space to play again", "", 150);
        }
    }

    public override void PaintIcon(Graphics g, int size)
    {
        float u = size / 32f;
        Draw.FillRounded(g, new RectangleF(2 * u, 4 * u, 28 * u, 24 * u), 5 * u, Theme.Tile);
        var slots = new[]
        {
            (4f, 6f, Theme.BoardInner), (13f, 6f, Theme.BoardInner), (22f, 6f, Theme.Red),
            (4f, 15f, Theme.BoardInner), (13f, 15f, Theme.Red), (22f, 15f, Theme.Yellow),
        };
        foreach (var (x, y, c) in slots)
        {
            using var b = new SolidBrush(c);
            g.FillEllipse(b, x * u, y * u, 7 * u, 7 * u);
        }
        using var last = new SolidBrush(Theme.Yellow);
        g.FillEllipse(last, 4 * u, 22 * u, 7 * u, 7 * u);
    }

    /// Head-to-head between two difficulty settings. The weaker side reuses the
    /// real search on a colour-swapped board, so this measures the shipped AI
    /// rather than a stand-in written for the test.
    internal (int weakWins, int strongWins, int draws) SimulateMatch(
        int games, int weakDepth, double weakBlunder, int strongDepth)
    {
        var localRng = new Random(7);
        int weakWins = 0, strongWins = 0, draws = 0;

        for (int g = 0; g < games; g++)
        {
            var b = new int[Cols, Rows];
            int turn = g % 2 == 0 ? You : Ai;   // alternate who opens

            while (true)
            {
                int w = FindWinner(b, out _);
                if (w == You) { weakWins++; break; }
                if (w == Ai) { strongWins++; break; }

                var open = Enumerable.Range(0, Cols).Where(c => b[c, 0] == Empty).ToArray();
                if (open.Length == 0) { draws++; break; }

                int move;
                if (turn == You)
                {
                    move = localRng.NextDouble() < weakBlunder
                        ? open[localRng.Next(open.Length)]
                        : BestColumn(Swap(b), weakDepth);
                }
                else
                {
                    move = BestColumn(b, strongDepth);
                }

                if (move < 0 || b[move, 0] != Empty) move = open[localRng.Next(open.Length)];
                b[move, LandingRowIn(b, move)] = turn;
                turn = turn == You ? Ai : You;
            }
        }

        return (weakWins, strongWins, draws);
    }

    static int[,] Swap(int[,] b)
    {
        var flipped = new int[Cols, Rows];
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                flipped[x, y] = b[x, y] switch { You => Ai, Ai => You, _ => Empty };
        return flipped;
    }

    public override async Task SelfTestAsync(SelfTestContext ctx)
    {
        int X(int col) => col * CellPx + CellPx / 2;
        int Y(int row) => TopPx + row * CellPx + CellPx / 2;

        await ctx.Click(X(0), Y(3), settle: 900);
        ctx.Log($"you drop c0   : phase={phase} aiDiscs={Count(Ai)}");

        await ctx.Click(X(1), Y(3), settle: 900);
        await ctx.Click(X(2), Y(3), settle: 1100);
        ctx.Log($"three in a row: bottom row=[{string.Join(",", Enumerable.Range(0, Cols).Select(c => cells[c, Rows - 1]))}]");
        ctx.Log($"ai must block at column 3 -> got {cells[3, Rows - 1]} (2 = ai)");
        ctx.Snap("1-blocked");

        for (int guard = 0; guard < 14 && phase != Phase.Over; guard++)
        {
            if (phase != Phase.YourTurn) { await ctx.Delay(250); continue; }
            int col = Enumerable.Range(0, Cols).FirstOrDefault(CanDrop, -1);
            if (col < 0) break;
            await ctx.Click(X(col), Y(3), settle: 700);
        }

        ctx.Log($"result        : winner={(winner == You ? "you" : winner == Ai ? "ai" : "draw/unfinished")} phase={phase}");
        ctx.Snap("2-result");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (weak, strong, drawn) = SimulateMatch(6, weakDepth: 2, weakBlunder: 0.40, strongDepth: 8);
        ctx.Log($"Careless vs Ruthless over 6 games: careless {weak}, ruthless {strong}, draws {drawn} " +
                $"({sw.ElapsedMilliseconds}ms)");
    }

    int Count(int who)
    {
        int n = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (cells[x, y] == who) n++;
        return n;
    }
}
