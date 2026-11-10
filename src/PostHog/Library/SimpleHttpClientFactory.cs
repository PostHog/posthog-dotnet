using System.Collections.Concurrent;
using PostHog.Emails;

namespace PostHog.Library;

internal class SimpleHttpClientFactory : IHttpClientFactory
{
    readonly ConcurrentDictionary<string, HttpClient> _clients = new();

    public HttpClient CreateClient(string name)
    {
        if (_clients.TryGetValue(name, out var client))
        {
            return client;
        }

        client = name == EmailSender.UploadClientName ? EmailSender.CreateUploadClient() : new HttpClient();
        _clients.TryAdd(name, client);

        return client;
    }
}