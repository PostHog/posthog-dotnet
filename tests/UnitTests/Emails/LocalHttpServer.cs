using System.Net;
using System.Net.Sockets;

namespace EmailsTests;

/// <summary>
/// Serves an <see cref="HttpMessageHandler"/> on a loopback port, so a real <see cref="PostHogClient"/> reaches it
/// through its real HTTP stack, redirect handling included, which a fake transport would skip.
/// </summary>
sealed class LocalHttpServer : IAsyncDisposable
{
    readonly HttpListener _listener = new();
    readonly HttpMessageInvoker _peer;
    readonly Task _serving;

    LocalHttpServer(HttpMessageHandler peer)
    {
        Origin = $"http://127.0.0.1:{FreePort()}";
        _listener.Prefixes.Add(Origin + "/");
        _listener.Start();
        _peer = new HttpMessageInvoker(peer, disposeHandler: false);
        _serving = Task.Run(ServeAsync);
    }

    /// <summary>The server's scheme, host and port, such as <c>http://127.0.0.1:40123</c>.</summary>
    public string Origin { get; }

    public static LocalHttpServer Serve(HttpMessageHandler peer) => new(peer);

    async Task ServeAsync()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                await AnswerAsync(context);
            }
            catch (Exception e) when (e is HttpListenerException or IOException or InvalidOperationException)
            {
                context.Response.Abort();
            }
        }
    }

    async Task AnswerAsync(HttpListenerContext context)
    {
        var incoming = context.Request;
        using var body = new MemoryStream();
        await incoming.InputStream.CopyToAsync(body);
        using var request = new HttpRequestMessage(new HttpMethod(incoming.HttpMethod), new Uri(Origin + incoming.RawUrl))
        {
            Content = new ByteArrayContent(body.ToArray()),
        };
        foreach (var name in incoming.Headers.AllKeys.OfType<string>())
        {
            var value = incoming.Headers[name];
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await _peer.SendAsync(request, CancellationToken.None);
        var outgoing = context.Response;
        outgoing.StatusCode = (int)response.StatusCode;
        if (response.Headers.Location is { } location)
        {
            outgoing.Headers[HttpResponseHeader.Location] = location.AbsoluteUri;
        }

        var content = await response.Content.ReadAsByteArrayAsync();
        outgoing.ContentType = response.Content.Headers.ContentType?.ToString();
        outgoing.ContentLength64 = content.Length;
#if NET5_0_OR_GREATER
        await outgoing.OutputStream.WriteAsync(content);
#else
        await outgoing.OutputStream.WriteAsync(content, 0, content.Length);
#endif
        outgoing.Close();
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        await _serving;
        _peer.Dispose();
    }
}
