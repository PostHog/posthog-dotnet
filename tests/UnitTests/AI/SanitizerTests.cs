using System.Text.Json;

#pragma warning disable CA1707

namespace PostHog.UnitTests.AI;

public class SanitizerTests
{
    [Fact]
    public void Sanitize_ShouldReturnNull_WhenInputIsNull()
    {
        var result = Sanitizer.Sanitize(null);
        Assert.Null(result);
    }

    [Fact]
    public void Sanitize_ShouldRedactBase64DataUrl()
    {
        var input =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var result = Sanitizer.Sanitize(input);
        Assert.Equal("[base64 image redacted]", result);
    }

    [Fact]
    public void Sanitize_ShouldRedactRawBase64()
    {
        var input =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var result = Sanitizer.Sanitize(input);
        Assert.Equal("[base64 image redacted]", result);
    }

    [Fact]
    public void Sanitize_ShouldNotRedactShortStrings()
    {
        var input = "shortstring";
        var result = Sanitizer.Sanitize(input);
        Assert.Equal("shortstring", result);
    }

    [Fact]
    public void Sanitize_ShouldNotRedactUrls()
    {
        var input = "https://example.com/image.png";
        var result = Sanitizer.Sanitize(input);
        Assert.Equal("https://example.com/image.png", result);
    }

    [Fact]
    public void Sanitize_ShouldNotRedactPaths()
    {
        var input = "/path/to/image.png";
        var result = Sanitizer.Sanitize(input);
        Assert.Equal("/path/to/image.png", result);
    }

    [Fact]
    public void Sanitize_ShouldSanitizeDictionary()
    {
        var input = new Dictionary<string, object>
        {
            { "safe", "hello" },
            {
                "image",
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
            },
        };
        var result = (Dictionary<string, object>)Sanitizer.Sanitize(input)!;
        Assert.Equal("hello", result["safe"]);
        Assert.Equal("[base64 image redacted]", result["image"]);
    }

    [Fact]
    public void Sanitize_ShouldSanitizeList()
    {
        var input = new List<object>
        {
            "hello",
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=",
        };
        var result = (List<object>)Sanitizer.Sanitize(input)!;
        Assert.Equal("hello", result[0]);
        Assert.Equal("[base64 image redacted]", result[1]);
    }

    [Fact]
    public void Sanitize_ShouldSanitizeJsonElement()
    {
        var json =
            @"{ ""safe"": ""hello"", ""image"": ""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="" }";
        using var doc = JsonDocument.Parse(json);
        var result = (Dictionary<string, object>)Sanitizer.Sanitize(doc.RootElement)!;

        Assert.Equal("hello", result["safe"]);
        Assert.Equal("[base64 image redacted]", result["image"]);
    }
}
