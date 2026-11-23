// SPDX-License-Identifier: GPL-3.0-or-later
//
// Grid.cs — pure grid-selection math for wTile (a C# port of kTile's grid.js).
//
// A "selection" is a rectangular block of grid cells expressed as position plus
// span (Col, Row, Cols, Rows) — 0-indexed, north-west origin. Everything here is
// pure and framework-free so it unit-tests without touching Win32.

using System.Text.RegularExpressions;

namespace WTile.Core;

public readonly record struct GridSize(int Cols, int Rows)
{
    public override string ToString() => $"{Cols}x{Rows}";
}

public readonly record struct Selection(int Col, int Row, int Cols, int Rows);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public enum Direction { North, East, South, West }

public static partial class Grid
{
    public static readonly IReadOnlyList<GridSize> DefaultGrids =
    [
        new(8, 6),
        new(6, 4),
        new(4, 4),
    ];

    public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    // JS Math.round semantics (half rounds toward +inf), so results match kTile.
    static int Round(double v) => (int)Math.Floor(v + 0.5);

    public static Selection NewSelection() => new(0, 0, 1, 1);

    /// Slide the block one cell without changing its size, keeping it inside the grid.
    public static Selection Move(Selection sel, GridSize grid, Direction dir)
    {
        int dc = dir == Direction.East ? 1 : dir == Direction.West ? -1 : 0;
        int dr = dir == Direction.South ? 1 : dir == Direction.North ? -1 : 0;
        return sel with
        {
            Col = Clamp(sel.Col + dc, 0, grid.Cols - sel.Cols),
            Row = Clamp(sel.Row + dr, 0, grid.Rows - sel.Rows),
        };
    }

    /// Move one named edge: delta &gt; 0 pushes it outward (grow on that side),
    /// delta &lt; 0 pulls it inward. The other three edges stay put. Stays inside
    /// the grid and at least one cell wide/tall.
    public static Selection ResizeEdge(Selection sel, GridSize grid, Direction edge, int delta)
    {
        int col = sel.Col, row = sel.Row, cols = sel.Cols, rows = sel.Rows;
        switch (edge)
        {
            case Direction.West:
                col = Clamp(sel.Col - delta, 0, sel.Col + sel.Cols - 1);
                cols = sel.Col + sel.Cols - col;
                break;
            case Direction.East:
                cols = Clamp(sel.Col + sel.Cols + delta, sel.Col + 1, grid.Cols) - sel.Col;
                break;
            case Direction.North:
                row = Clamp(sel.Row - delta, 0, sel.Row + sel.Rows - 1);
                rows = sel.Row + sel.Rows - row;
                break;
            case Direction.South:
                rows = Clamp(sel.Row + sel.Rows + delta, sel.Row + 1, grid.Rows) - sel.Row;
                break;
        }
        return new Selection(col, row, cols, rows);
    }

    public static Selection ResizeWidth(Selection sel, GridSize grid, int delta) =>
        ResizeEdge(sel, grid, Direction.East, delta);

    public static Selection ResizeHeight(Selection sel, GridSize grid, int delta) =>
        ResizeEdge(sel, grid, Direction.South, delta);

    /// Cell block → pixel rectangle inside `area`, inset by `gap` px on every side.
    public static PixelRect ToRect(Selection sel, GridSize grid, PixelRect area, int gap = 0)
    {
        double cw = (double)area.Width / grid.Cols;
        double ch = (double)area.Height / grid.Rows;
        return new PixelRect(
            Round(area.X + sel.Col * cw + gap),
            Round(area.Y + sel.Row * ch + gap),
            Round(sel.Cols * cw - 2 * gap),
            Round(sel.Rows * ch - 2 * gap));
    }

    /// Snap an arbitrary pixel rectangle to the nearest cell block (seeds a
    /// selection from a window's current geometry).
    public static Selection FromRect(PixelRect frame, GridSize grid, PixelRect area)
    {
        double cw = (double)area.Width / grid.Cols;
        double ch = (double)area.Height / grid.Rows;
        int col = Clamp(Round((frame.X - area.X) / cw), 0, grid.Cols - 1);
        int row = Clamp(Round((frame.Y - area.Y) / ch), 0, grid.Rows - 1);
        int right = Clamp(Round((frame.X + frame.Width - area.X) / cw), col + 1, grid.Cols);
        int bottom = Clamp(Round((frame.Y + frame.Height - area.Y) / ch), row + 1, grid.Rows);
        return new Selection(col, row, right - col, bottom - row);
    }

    /// Parse "8x6, 6x4, 4x4". Bad entries are skipped; empty or fully-invalid
    /// input falls back to DefaultGrids. Dimensions clamp to [1, 64].
    public static List<GridSize> ParseGrids(string? input)
    {
        var output = new List<GridSize>();
        foreach (var part in (input ?? "").Split(','))
        {
            var m = GridSpec().Match(part.Trim());
            if (m.Success)
                output.Add(new GridSize(ParseDim(m.Groups[1].Value), ParseDim(m.Groups[2].Value)));
        }
        return output.Count > 0 ? output : [.. DefaultGrids];
    }

    static int ParseDim(string digits) => int.TryParse(digits, out int v) ? Clamp(v, 1, 64) : 64;

    [GeneratedRegex(@"^(\d+)\s*[xX]\s*(\d+)$")]
    private static partial Regex GridSpec();

    /// The grid after `current` in `list` (wraps); first entry if not found.
    public static GridSize NextGrid(IReadOnlyList<GridSize> list, GridSize current)
    {
        if (list.Count == 0) return current;
        int i = 0;
        for (; i < list.Count; i++)
            if (list[i] == current) break;
        return list[(i + 1) % list.Count];
    }
}
