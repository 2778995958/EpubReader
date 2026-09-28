using EpubReader.Core;

namespace EpubReader.Tests;

public class NaturalSortTests
{
    [Fact]
    public void OrdersNumberedPages()
    {
        var names = new[] { "page10.jpg", "page2.jpg", "page1.jpg" };
        Array.Sort(names, NaturalSort.Compare);
        Assert.Equal(new[] { "page1.jpg", "page2.jpg", "page10.jpg" }, names);
    }
}
