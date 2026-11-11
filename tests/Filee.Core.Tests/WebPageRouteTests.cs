using Filee.Core.Conversion;

namespace Filee.Core.Tests;

public class WebPageRouteTests
{
    private static RoutePlanner Planner(out FakeConverter browser)
    {
        var reflow = new FakeConverter("reflow", new ConversionEdge("html", "pdf"), new ConversionEdge("pdf", "png"));
        browser = new FakeConverter("browser", new ConversionEdge("html", "png", DirectOnly: true, NotForImageSources: true, WebPage: true));
        return new RoutePlanner([reflow, browser]);
    }

    [Fact]
    public void Ordinary_html_to_png_keeps_the_reflow_route()
    {
        var route = Planner(out _).Plan("html", "png")!;
        Assert.Equal("reflow", route.Steps[0].Converter.Id);
    }

    [Fact]
    public void A_web_page_slice_takes_only_the_browser()
    {
        var route = Planner(out var browser).Plan("html", "png", webPage: true)!;
        Assert.Single(route.Steps);
        Assert.Equal(browser.Id, route.Steps[0].Converter.Id);
        Assert.True(route.Steps[0].WebPage);
    }

    [Fact]
    public void A_web_page_edge_is_invisible_to_ordinary_routes()
    {
        var browser = new FakeConverter("browser", new ConversionEdge("html", "png", DirectOnly: true, WebPage: true));
        Assert.Null(new RoutePlanner([browser]).Plan("html", "png"));
    }
}
