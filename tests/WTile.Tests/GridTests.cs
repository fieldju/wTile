// SPDX-License-Identifier: GPL-3.0-or-later
// Port of kTile's test/grid.test.js.
using WTile.Core;
using static WTile.Core.Direction;

namespace WTile.Tests;

public class GridTests
{
    static readonly GridSize G = new(8, 6);
    static readonly PixelRect Area = new(0, 0, 1600, 1200);
    static Selection S(int col, int row, int cols, int rows) => new(col, row, cols, rows);

    [Theory]
    [MemberData(nameof(MoveCases))]
    public void Move_slides_and_clamps(Selection from, Direction dir, Selection expected) =>
        Assert.Equal(expected, Grid.Move(from, G, dir));

    public static TheoryData<Selection, Direction, Selection> MoveCases => new()
    {
        { S(0, 0, 1, 1), East, S(1, 0, 1, 1) },
        { S(0, 0, 1, 1), West, S(0, 0, 1, 1) },   // clamps at 0
        { S(7, 0, 1, 1), East, S(7, 0, 1, 1) },   // clamps at right edge
        { S(6, 0, 2, 2), East, S(6, 0, 2, 2) },   // keeps span, stops at edge
        { S(2, 2, 2, 2), South, S(2, 3, 2, 2) },
    };

    [Fact]
    public void ResizeWidth_and_Height()
    {
        Assert.Equal(S(0, 0, 2, 1), Grid.ResizeWidth(S(0, 0, 1, 1), G, 1));
        Assert.Equal(S(0, 0, 2, 1), Grid.ResizeWidth(S(0, 0, 3, 1), G, -1));
        Assert.Equal(S(0, 0, 1, 1), Grid.ResizeWidth(S(0, 0, 1, 1), G, -1));
        Assert.Equal(S(6, 0, 2, 1), Grid.ResizeWidth(S(6, 0, 2, 1), G, 5));
        Assert.Equal(S(0, 0, 1, 2), Grid.ResizeHeight(S(0, 0, 1, 1), G, 1));
        Assert.Equal(S(0, 4, 1, 2), Grid.ResizeHeight(S(0, 4, 1, 2), G, 9));
    }

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void ResizeEdge_moves_one_edge(Selection from, Direction edge, int delta, Selection expected) =>
        Assert.Equal(expected, Grid.ResizeEdge(from, G, edge, delta));

    public static TheoryData<Selection, Direction, int, Selection> EdgeCases => new()
    {
        { S(3, 1, 2, 2), West, 1, S(2, 1, 3, 2) },
        { S(3, 1, 2, 2), West, -1, S(4, 1, 1, 2) },
        { S(3, 1, 2, 2), East, 1, S(3, 1, 3, 2) },
        { S(3, 1, 2, 2), East, -1, S(3, 1, 1, 2) },
        { S(3, 2, 2, 2), North, 1, S(3, 1, 2, 3) },
        { S(3, 2, 2, 2), North, -1, S(3, 3, 2, 1) },
        { S(3, 2, 2, 2), South, 1, S(3, 2, 2, 3) },
        { S(3, 2, 2, 2), South, -1, S(3, 2, 2, 1) },
        // can't escape the grid
        { S(0, 0, 2, 2), West, 1, S(0, 0, 2, 2) },
        { S(0, 0, 2, 2), North, 1, S(0, 0, 2, 2) },
        { S(6, 0, 2, 2), East, 1, S(6, 0, 2, 2) },
        { S(0, 4, 2, 2), South, 1, S(0, 4, 2, 2) },
        // can't collapse below one cell
        { S(2, 0, 1, 2), West, -1, S(2, 0, 1, 2) },
        { S(2, 0, 1, 2), East, -1, S(2, 0, 1, 2) },
        { S(0, 2, 2, 1), North, -1, S(0, 2, 2, 1) },
        { S(0, 2, 2, 1), South, -1, S(0, 2, 2, 1) },
    };

    [Fact]
    public void ResizeEdge_pins_untouched_edges()
    {
        var s = S(3, 1, 2, 2);
        var w = Grid.ResizeEdge(s, G, West, 1);
        Assert.Equal(s.Col + s.Cols, w.Col + w.Cols);
        Assert.Equal(s.Row, Grid.ResizeEdge(s, G, South, -1).Row);
    }

    [Fact]
    public void ToRect_math()
    {
        Assert.Equal(new PixelRect(0, 0, 1600, 1200), Grid.ToRect(S(0, 0, 8, 6), G, Area));
        Assert.Equal(new PixelRect(0, 0, 200, 200), Grid.ToRect(S(0, 0, 1, 1), G, Area));
        Assert.Equal(new PixelRect(110, 60, 180, 180),
            Grid.ToRect(S(0, 0, 1, 1), G, new PixelRect(100, 50, 1600, 1200), 10));
    }

    [Fact]
    public void FromRect_round_trips_and_floors_at_one_cell()
    {
        var original = S(2, 1, 3, 3);
        Assert.Equal(original, Grid.FromRect(Grid.ToRect(original, G, Area), G, Area));
        var tiny = Grid.FromRect(new PixelRect(5, 5, 3, 3), G, Area);
        Assert.True(tiny.Cols >= 1 && tiny.Rows >= 1);
    }

    [Fact]
    public void ParseGrids()
    {
        Assert.Equal(Grid.DefaultGrids, Grid.ParseGrids("8x6, 6x4,4x4"));
        Assert.Equal(3, Grid.ParseGrids("").Count);
        Assert.Equal(new GridSize(64, 64), Grid.ParseGrids("600x800")[0]);
        Assert.Equal(2, Grid.ParseGrids("8x6, nope, 4x4").Count);
        Assert.Equal(new GridSize(64, 64), Grid.ParseGrids("99999999999x99999999999")[0]);
        Assert.Equal(3, Grid.ParseGrids("-1x4").Count); // invalid → defaults
    }

    [Fact]
    public void NextGrid_rotates()
    {
        var d = Grid.DefaultGrids;
        Assert.Equal(d[0], Grid.NextGrid(d, d[2]));
        Assert.Equal(d[1], Grid.NextGrid(d, d[0]));
    }
}
