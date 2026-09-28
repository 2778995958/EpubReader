using EpubReader.Core;

namespace EpubReader.Tests;

public class SpreadPairTests
{
    [Fact]
    public void HorizontalPairsLeftThenRightNotRightThenNextLeft()
    {
        using var session = Session(
            WritingMode.Horizontal,
            PageProgression.Ltr,
            PageSpreadKind.Left, PageSpreadKind.Right,
            PageSpreadKind.Left, PageSpreadKind.Right);
        Assert.True(session.VisualLtr);
        session.PageIndex = 1;
        Assert.Equal([0, 1], session.VisiblePageIndices());
        session.PageIndex = 2;
        Assert.Equal([2, 3], session.VisiblePageIndices());
    }

    [Fact]
    public void VerticalPairsRightThenLeft()
    {
        using var session = Session(
            WritingMode.Vertical,
            PageProgression.Rtl,
            PageSpreadKind.Right, PageSpreadKind.Left,
            PageSpreadKind.Right, PageSpreadKind.Left);
        Assert.False(session.VisualLtr);
        session.PageIndex = 1;
        Assert.Equal([0, 1], session.VisiblePageIndices());
        session.PageIndex = 2;
        Assert.Equal([2, 3], session.VisiblePageIndices());
    }

    [Fact]
    public void RightLeftPairingDoesNotLetLeadingLeftStealNextRight()
    {
        using var session = Session(
            WritingMode.Vertical,
            PageProgression.Rtl,
            PageSpreadKind.Left, PageSpreadKind.Right, PageSpreadKind.Left);
        session.PageIndex = 1;
        Assert.Equal([1, 2], session.VisiblePageIndices());
    }

    static ReaderSession Session(WritingMode writing, PageProgression ppd, params PageSpreadKind[] spreads)
    {
        var spine = spreads.Select((sp, i) => new SpineItem
        {
            Index = i,
            Key = $"p{i}",
            Title = $"p{i}",
            Kind = SpineKind.Image,
            Spread = sp,
            ImageKey = $"p{i}",
            ImageWidth = 100,
            ImageHeight = 100
        }).ToList();
        var book = new Book
        {
            FilePath = "x",
            Title = "t",
            DefaultWritingMode = writing,
            Progression = ppd,
            HasPageSpreads = true,
            PairLeftToRight = writing == WritingMode.Horizontal,
            Spine = spine
        };
        var session = new ReaderSession(book);
        session.Relayout(new DummyMeasurer());
        return session;
    }
}
