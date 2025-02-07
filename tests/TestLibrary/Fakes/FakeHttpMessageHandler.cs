using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// Useful class for testing HTTP clients.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    readonly List<RequestHandler> _handlers = [];

    public static HttpResponseMessage CreateResponse<TResponseBody>(TResponseBody responseBody, string contentType = "application/json")
        => CreateResponse(SerializeObject(responseBody), contentType);

    public static HttpResponseMessage CreateResponse(string responseBody, string contentType) =>
        new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseBody, Encoding.UTF8, contentType)
        };

    public RequestHandler AddResponse(Uri url, HttpResponseMessage responseMessage) =>
        AddResponse(url, HttpMethod.Get, responseMessage);

    public RequestHandler AddResponseException(Uri url, HttpMethod httpMethod, Exception responseException)
    {
        var handler = new RequestHandler(url, httpMethod, responseException);
        _handlers.Add(handler);
        return handler;
    }

    public RequestHandler AddResponse(Uri url, HttpMethod httpMethod, HttpResponseMessage responseMessage)
    {
        var handler = new RequestHandler(url, httpMethod, responseMessage);
        _handlers.Add(handler);
        return handler;
    }

    public RequestHandler AddResponse(
        Uri url,
        HttpMethod httpMethod,
        object responseBody,
        string contentType = "application/json")
        => AddResponse(url, httpMethod, SerializeObject(responseBody), contentType);

    public RequestHandler AddResponse(
        Uri url,
        HttpMethod httpMethod,
        string responseBody,
        string contentType = "application/json")
    {
#pragma warning disable CA2000
        var responseMessage = CreateResponse(responseBody, contentType);
#pragma warning restore CA2000
        var handler = new RequestHandler(url, httpMethod, responseMessage);
        _handlers.Add(handler);
        return handler;
    }

    public void AddRepeatedResponses(
        int count,
        Uri url,
        HttpMethod httpMethod,
        Func<int, string> responseBodyFunc,
        string contentType = "application/json")
    {
        for (var i = 0; i < count; i++)
        {
            AddResponse(url, httpMethod, responseBodyFunc(i), contentType);
        }
    }

    public RequestHandler AddResponse(Uri url, HttpMethod httpMethod, Func<Task<HttpResponseMessage>> responseHandler)
    {
        var handler = new RequestHandler(url, httpMethod, responseHandler);
        _handlers.Add(handler);
        return handler;
    }

    public RequestHandler AddStreamResponse(Func<HttpRequestMessage, Task<bool>> requestPredicate, Stream responseStream)
    {
        var content = new StreamContent(responseStream);
#pragma warning disable CA2000
        var responseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = content
        };
#pragma warning restore CA2000
        var handler = new RequestHandler(requestPredicate, responseMessage);
        _handlers.Add(handler);
        return handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        foreach (var handler in _handlers)
        {
            if (await handler.IsMatch(request))
            {
                _handlers.Remove(handler); // Pop the handler so we can simulate multiple requests with different responses.
                return await handler.Respond(request);
            }
        }

        return new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound };
    }

    public class RequestHandler
    {
        readonly Func<HttpRequestMessage, Task<bool>> _requestPredicate;
        readonly List<HttpRequestMessage> _receivedRequests = new();
        readonly List<string> _receivedBodiesJson = new();
        readonly Func<Task<HttpResponseMessage>> _responseHandler;

        /// <summary>
        /// Constructs a <see cref="RequestHandler"/> that throws an exception when the specified url
        /// is requested.
        /// </summary>
        /// <param name="uri">The URL to request.</param>
        /// <param name="httpMethod">The HTTP Method.</param>
        /// <param name="exception">The exception to throw.</param>
        public RequestHandler(Uri uri, HttpMethod httpMethod, Exception exception)
            : this(CreateRequestPredicate(uri, httpMethod), () => throw exception)
        {
        }

        public RequestHandler(Uri uri, HttpResponseMessage responseMessage)
            : this(CreateRequestPredicate(uri, HttpMethod.Get), responseMessage)
        {
        }

        public RequestHandler(Uri uri, HttpMethod httpMethod, HttpResponseMessage responseMessage)
            : this(CreateRequestPredicate(uri, httpMethod), responseMessage)
        {
        }

        public RequestHandler(Uri uri, Func<Task<HttpResponseMessage>> responseHandler)
            : this(CreateRequestPredicate(uri, HttpMethod.Get), responseHandler)
        {
        }

        public RequestHandler(Uri uri, HttpMethod httpMethod, Func<Task<HttpResponseMessage>> responseHandler)
            : this(CreateRequestPredicate(uri, httpMethod), responseHandler)
        {
        }

        public RequestHandler(
            Func<HttpRequestMessage, Task<bool>> requestPredicate,
            HttpResponseMessage responseMessage)
            : this(requestPredicate, () => Task.FromResult(responseMessage))
        {
        }

        public RequestHandler(
            Func<HttpRequestMessage, Task<bool>> requestPredicate,
            Func<Task<HttpResponseMessage>> responseHandler)
        {
            _requestPredicate = requestPredicate;
            _responseHandler = responseHandler;
        }

        static Func<HttpRequestMessage, Task<bool>> CreateRequestPredicate(Uri uri, HttpMethod httpMethod)
        {
            return request => Task.FromResult(request.RequestUri == uri && request.Method == httpMethod);
        }

        public Task<bool> IsMatch(HttpRequestMessage requestMessage) => _requestPredicate(requestMessage);

        public async Task<HttpResponseMessage> Respond(HttpRequestMessage requestMessage)
        {
            Debug.Assert(requestMessage != null, nameof(requestMessage) + " != null");
            _receivedRequests.Add(requestMessage);
            if (requestMessage.Content is not null)
            {
                _receivedBodiesJson.Add(await requestMessage.Content.ReadAsStringAsync());
            }

            return await _responseHandler();
        }

        public IReadOnlyList<HttpRequestMessage> ReceivedRequests => _receivedRequests;

        public HttpRequestMessage ReceivedRequest => _receivedRequests.Single();

        public string GetReceivedRequestBody(bool indented)
        {
            var json = _receivedBodiesJson.Single();
            return indented ? FormatJson(json) : json;
        }
    }

    static string FormatJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        return JsonSerializer.Serialize(doc.RootElement, options);
    }

    static string SerializeObject<T>(T obj)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        return JsonSerializer.Serialize(obj, options);
    }
}