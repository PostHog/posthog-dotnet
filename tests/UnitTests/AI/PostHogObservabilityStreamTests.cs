using System.Text;
using PostHog.AI.OpenAI;
using Xunit;

#pragma warning disable CA1707

namespace PostHog.UnitTests.AI;

public class PostHogObservabilityStreamTests
{
    [Fact]
    public async Task ReadAsync_ShouldCaptureContentAndPassThrough()
    {
        var content = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var memoryStream = new MemoryStream(bytes);
        
        string? capturedContent = null;
        var tcs = new TaskCompletionSource<string>();

        var stream = new PostHogObservabilityStream(memoryStream, async (text) => 
        {
            capturedContent = text;
            tcs.SetResult(text);
            await Task.CompletedTask;
        });

        var buffer = new byte[1024];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));

        Assert.Equal(content.Length, read);
        Assert.Equal(content, Encoding.UTF8.GetString(buffer, 0, read));
        
        // Dispose triggers the callback
        await stream.DisposeAsync();
        
        // Wait for the callback
        var result = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        Assert.Equal(tcs.Task, result);
        Assert.Equal(content, capturedContent);
    }
}
