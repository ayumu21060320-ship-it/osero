// 立体オセロの CPU。Unity に依存しない C#。
// ブラウザ（WebGL）でも動くよう、別スレッドは使わず、Step を毎フレーム呼んで少しずつ考える。
//
// 強さ:
//   1 弱い  … たくさん裏返せる手を選ぶ（ときどき適当に打つ）
//   2 普通  … 1 手先を読む。マスの価値（角・辺・面）と、相手の置ける所の少なさで選ぶ
//   3 強い  … 2 手先を読む（自分が置いた後、相手が一番良い手を返してくると考えて選ぶ）
//
// マスの価値: 立方体の角（8 か所）は二度と裏返らないので一番高い。辺・面がその次。
//             まだ空いている角のとなりは、相手に角を取られやすいので低い。
using System;
using System.Collections.Generic;
using System.Diagnostics;

public class OseroAI
{
    const int N = OseroBoard.N;

    public bool Done { get; private set; }
    public int Result { get; private set; } = -1;   // 選んだマスの番号

    readonly OseroBoard b;
    readonly Stone me, opp;
    readonly int level;
    readonly Random rng;
    readonly List<int> moves = new List<int>();
    readonly List<int> replies = new List<int>();
    readonly List<int> flips = new List<int>();
    readonly List<int> flips2 = new List<int>();
    readonly List<int> best = new List<int>();
    int index;
    double bestScore = double.NegativeInfinity;

    static readonly int[] BaseWeight = new int[OseroBoard.CellCount];
    static readonly int[] Corners = new int[8];
    static readonly int[][] CornerNeighbors = new int[8][];

    static OseroAI()
    {
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            OseroBoard.FromIndex(i, out int x, out int y, out int z);
            int onEdge = (x == 0 || x == N - 1 ? 1 : 0) + (y == 0 || y == N - 1 ? 1 : 0) + (z == 0 || z == N - 1 ? 1 : 0);
            BaseWeight[i] = onEdge == 3 ? 100 : onEdge == 2 ? 10 : onEdge == 1 ? 3 : 1;
        }
        int k = 0;
        for (int cx = 0; cx < N; cx += N - 1)
            for (int cy = 0; cy < N; cy += N - 1)
                for (int cz = 0; cz < N; cz += N - 1)
                {
                    Corners[k] = OseroBoard.Index(cx, cy, cz);
                    var list = new List<int>();
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                int x = cx + dx, y = cy + dy, z = cz + dz;
                                if ((dx | dy | dz) == 0 || !OseroBoard.InBounds(x, y, z)) continue;
                                list.Add(OseroBoard.Index(x, y, z));
                            }
                    CornerNeighbors[k] = list.ToArray();
                    k++;
                }
    }

    // board の今の手番の人として考える。board は写して使うので、考えている間に元の盤が変わってもよい
    public OseroAI(OseroBoard board, int level, int seed)
    {
        b = board.Clone();
        me = b.Turn;
        opp = OseroBoard.Opponent(me);
        this.level = level;
        rng = new Random(seed);
        b.LegalMoves(me, moves);
        if (moves.Count == 0) { Done = true; return; }
        if (moves.Count == 1) { Result = moves[0]; Done = true; return; }
        // 弱いときは、ときどき適当な手を打つ
        if (level <= 1 && rng.NextDouble() < 0.25)
        {
            Result = moves[rng.Next(moves.Count)];
            Done = true;
        }
    }

    // 最大 budgetMs ミリ秒だけ考える。考え終わったら Done が true になる
    public void Step(double budgetMs)
    {
        if (Done) return;
        var sw = Stopwatch.StartNew();
        while (index < moves.Count)
        {
            int m = moves[index++];
            double s = Score(m) + rng.NextDouble() * 0.5; // 同じくらいの手は、ばらけさせる
            if (s > bestScore) { bestScore = s; best.Clear(); best.Add(m); }
            if (sw.Elapsed.TotalMilliseconds >= budgetMs) break;
        }
        if (index >= moves.Count)
        {
            Result = best.Count > 0 ? best[0] : moves[0];
            Done = true;
        }
    }

    double Score(int m)
    {
        if (level <= 1)
        {
            OseroBoard.FromIndex(m, out int x, out int y, out int z);
            return b.Flips(x, y, z, me, flips).Count + rng.Next(4);
        }

        b.Apply(m, me, flips);
        double score;
        if (level == 2)
        {
            score = Evaluate(me) - 3 * b.CountLegalMoves(opp);
        }
        else
        {
            b.LegalMoves(opp, replies);
            if (replies.Count == 0)
                score = Evaluate(me) + 30; // 相手がパスになる手は良い
            else
            {
                score = double.PositiveInfinity;
                foreach (int r in replies)
                {
                    b.Apply(r, opp, flips2);
                    double s = Evaluate(me);
                    b.Undo(r, opp, flips2);
                    if (s < score) score = s;
                }
            }
        }
        b.Undo(m, me, flips);
        return score;
    }

    // 盤の良さ（color から見て）。石のあるマスの価値の合計（自分は足し、相手は引く）
    int Evaluate(Stone color)
    {
        int s = 0;
        for (int i = 0; i < OseroBoard.CellCount; i++)
        {
            Stone c = b.Get(i);
            if (c == Stone.Empty) continue;
            s += c == color ? BaseWeight[i] : -BaseWeight[i];
        }
        // 空いている角のとなりの石は、相手に角を取られやすいので減らす
        for (int k = 0; k < 8; k++)
        {
            if (b.Get(Corners[k]) != Stone.Empty) continue;
            foreach (int i in CornerNeighbors[k])
            {
                Stone c = b.Get(i);
                if (c == Stone.Empty) continue;
                s += c == color ? -25 : 25;
            }
        }
        // 終わりが近いときは、石の数そのものを重く見る
        int empty = 0;
        for (int i = 0; i < OseroBoard.CellCount; i++) if (b.Get(i) == Stone.Empty) empty++;
        if (empty < 40) s += 4 * (b.Count(color) - b.Count(OseroBoard.Opponent(color)));
        return s;
    }
}
