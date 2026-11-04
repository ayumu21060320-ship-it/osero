// 立体オセロ（8×8×8）のルール。Unity に依存しない C#。
// ルールの正は docs/決めたこと.md の「立体オセロのルール」。ここを変えたらそちらも合わせる。
//
// 座標: x（左右）, y（段。0 が一番下、7 が一番上）, z（奥行き）。どれも 0〜7。
// 置いた石から 26 方向（上下左右前後と、そのななめ）に、相手の石が続いた先に自分の石があれば、間の石を全部裏返す。
using System.Collections.Generic;

public enum Stone : byte { Empty = 0, Black = 1, White = 2 }

public class OseroBoard
{
    public const int N = 8;
    public const int CellCount = N * N * N;

    readonly Stone[] cells = new Stone[CellCount];

    public Stone Turn { get; private set; }
    public bool GameOver { get; private set; }

    // 26 方向（(0,0,0) 以外の、各軸 -1/0/+1 の組み合わせ）
    static readonly int[] DX, DY, DZ;

    static OseroBoard()
    {
        var dx = new List<int>(); var dy = new List<int>(); var dz = new List<int>();
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    dx.Add(x); dy.Add(y); dz.Add(z);
                }
        DX = dx.ToArray(); DY = dy.ToArray(); DZ = dz.ToArray();
    }

    public OseroBoard() { Reset(); }

    public static int Index(int x, int y, int z) => (y * N + z) * N + x;
    public static void FromIndex(int i, out int x, out int y, out int z)
    {
        x = i % N; z = (i / N) % N; y = i / (N * N);
    }
    public static bool InBounds(int x, int y, int z) =>
        x >= 0 && x < N && y >= 0 && y < N && z >= 0 && z < N;
    public static Stone Opponent(Stone s) => s == Stone.Black ? Stone.White : Stone.Black;

    public Stone Get(int x, int y, int z) => cells[Index(x, y, z)];
    public Stone Get(int i) => cells[i];

    // 最初の配置: 真ん中の 2×2×2 に、黒と白を市松模様（となり同士が別の色）で 4 個ずつ。先手は黒
    public void Reset()
    {
        for (int i = 0; i < CellCount; i++) cells[i] = Stone.Empty;
        for (int x = 3; x <= 4; x++)
            for (int y = 3; y <= 4; y++)
                for (int z = 3; z <= 4; z++)
                    cells[Index(x, y, z)] = (x + y + z) % 2 == 0 ? Stone.Black : Stone.White;
        Turn = Stone.Black;
        GameOver = false;
    }

    // (x,y,z) に color を置いたら裏返る石の番号。置けない（空いていない・1 個も裏返らない）なら空
    public List<int> Flips(int x, int y, int z, Stone color, List<int> result = null)
    {
        if (result == null) result = new List<int>();
        result.Clear();
        if (!InBounds(x, y, z) || cells[Index(x, y, z)] != Stone.Empty) return result;
        Stone opp = Opponent(color);
        for (int d = 0; d < DX.Length; d++)
        {
            int cx = x + DX[d], cy = y + DY[d], cz = z + DZ[d];
            int start = result.Count;
            while (InBounds(cx, cy, cz) && cells[Index(cx, cy, cz)] == opp)
            {
                result.Add(Index(cx, cy, cz));
                cx += DX[d]; cy += DY[d]; cz += DZ[d];
            }
            // 相手の石の先に自分の石が無ければ、この方向は裏返らない
            if (!(InBounds(cx, cy, cz) && cells[Index(cx, cy, cz)] == color))
                result.RemoveRange(start, result.Count - start);
        }
        return result;
    }

    public bool IsLegal(int x, int y, int z, Stone color)
    {
        if (!InBounds(x, y, z) || cells[Index(x, y, z)] != Stone.Empty) return false;
        Stone opp = Opponent(color);
        for (int d = 0; d < DX.Length; d++)
        {
            int cx = x + DX[d], cy = y + DY[d], cz = z + DZ[d];
            int run = 0;
            while (InBounds(cx, cy, cz) && cells[Index(cx, cy, cz)] == opp)
            {
                run++;
                cx += DX[d]; cy += DY[d]; cz += DZ[d];
            }
            if (run > 0 && InBounds(cx, cy, cz) && cells[Index(cx, cy, cz)] == color) return true;
        }
        return false;
    }

    public bool HasLegalMove(Stone color)
    {
        for (int i = 0; i < CellCount; i++)
        {
            if (cells[i] != Stone.Empty) continue;
            FromIndex(i, out int x, out int y, out int z);
            if (IsLegal(x, y, z, color)) return true;
        }
        return false;
    }

    public int Count(Stone color)
    {
        int n = 0;
        for (int i = 0; i < CellCount; i++) if (cells[i] == color) n++;
        return n;
    }

    // 置ける場所を全部 result に入れる
    public List<int> LegalMoves(Stone color, List<int> result)
    {
        result.Clear();
        for (int i = 0; i < CellCount; i++)
        {
            if (cells[i] != Stone.Empty) continue;
            FromIndex(i, out int x, out int y, out int z);
            if (IsLegal(x, y, z, color)) result.Add(i);
        }
        return result;
    }

    public int CountLegalMoves(Stone color)
    {
        int n = 0;
        for (int i = 0; i < CellCount; i++)
        {
            if (cells[i] != Stone.Empty) continue;
            FromIndex(i, out int x, out int y, out int z);
            if (IsLegal(x, y, z, color)) n++;
        }
        return n;
    }

    public OseroBoard Clone()
    {
        var b = new OseroBoard();
        System.Array.Copy(cells, b.cells, CellCount);
        b.Turn = Turn;
        b.GameOver = GameOver;
        return b;
    }

    // CPU が先読みするときに使う: 手番を変えずに石を置いて裏返す／元に戻す。
    // Apply は置けないマスなら false を返し、何も変えない
    public bool Apply(int cell, Stone color, List<int> flipped)
    {
        FromIndex(cell, out int x, out int y, out int z);
        Flips(x, y, z, color, flipped);
        if (flipped.Count == 0) return false;
        cells[cell] = color;
        foreach (int i in flipped) cells[i] = color;
        return true;
    }

    public void Undo(int cell, Stone color, List<int> flipped)
    {
        cells[cell] = Stone.Empty;
        Stone opp = Opponent(color);
        foreach (int i in flipped) cells[i] = opp;
    }

    public enum After { Normal, Passed, GameOver }

    // 手番の人が (x,y,z) に置く。置けないなら false。
    // 置いた後: 相手に置ける所があれば相手の番。無ければパスでもう一度自分の番。どちらも無ければ終わり
    public bool Play(int x, int y, int z, List<int> flipped, out After after)
    {
        after = After.Normal;
        if (GameOver) return false;
        Flips(x, y, z, Turn, flipped);
        if (flipped.Count == 0) return false;
        cells[Index(x, y, z)] = Turn;
        foreach (int i in flipped) cells[i] = Turn;

        Stone next = Opponent(Turn);
        if (HasLegalMove(next)) Turn = next;
        else if (HasLegalMove(Turn)) after = After.Passed;
        else { GameOver = true; after = After.GameOver; }
        return true;
    }
}
