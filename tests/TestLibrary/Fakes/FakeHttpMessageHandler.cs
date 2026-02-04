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

    static HttpResponseMessage CreateResponse<TResponseBody>(TResponseBody responseBody, string contentType) =>
        CreateResponse(SerializeObject(responseBody), contentType);

    static HttpResponseMessage CreateResponse(string responseBody, string contentType) =>
        new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseBody, Encoding.UTF8, contentType)
        };

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

    public RequestHandler AddResponse<TRequestBody, TResponseBody>(
        Uri url,
        HttpMethod httpMethod,
        Func<TRequestBody, bool> predicate,
        TResponseBody responseBody)
    {
        var handler = RequestHandler.Create(url, httpMethod, predicate, responseBody);
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

    /// <summary>
    /// Handles a request and returns a response.
    /// </summary>
    /// <param name="requestPredicate">The condition the request must meet to get this response.</param>
    /// <param name="responseHandler">
    /// A func that returns the response if the <paramref name="requestPredicate"/> is <c>true</c>.
    /// </param>
    public class RequestHandler(
        Func<HttpRequestMessage, Task<bool>> requestPredicate,
        Func<Task<HttpResponseMessage>> responseHandler)
    {
        readonly List<HttpRequestMessage> _receivedRequests = new();
        readonly List<string> _receivedBodiesJson = new();

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

        /// <summary>
        /// Creates a <see cref="RequestHandler"/> that responds with the specified <paramref name="responseMessage"/>
        /// when the specified <paramref name="uri"/> is requested with the specified <paramref name="httpMethod"/>.
        /// </summary>
        /// <param name="uri">The URI to request.</param>
        /// <param name="httpMethod">The HTTP method to request with.</param>
        /// <param name="responseMessage">The response to respond with.</param>
        public RequestHandler(Uri uri, HttpMethod httpMethod, HttpResponseMessage responseMessage)
            : this(CreateRequestPredicate(uri, httpMethod), responseMessage)
        {
        }

        /// <summary>
        /// Creates a <see cref="RequestHandler"/> that responds response message returned by the specified
        /// <paramref name="responseHandler"/> when the specified <paramref name="uri"/> is requested with the
        /// specified <paramref name="httpMethod"/>.
        /// </summary>
        /// <param name="uri">The URI to request.</param>
        /// <param name="httpMethod">The HTTP method to request with.</param>
        /// <param name="responseHandler">A func that returns a <see cref="HttpResponseMessage"/>.</param>
        public RequestHandler(Uri uri, HttpMethod httpMethod, Func<Task<HttpResponseMessage>> responseHandler)
            : this(CreateRequestPredicate(uri, httpMethod), responseHandler)
        {
        }

        /// <summary>
        /// Creates a <see cref="RequestHandler"/> that responds with the specified <paramref name="responseMessage"/>
        /// when the specified <paramref name="requestPredicate"/> is true.
        /// </summary>
        /// <param name="requestPredicate">The condition the request must meet to get this response.</param>
        /// <param name="responseMessage">The response message to return.</param>
        public RequestHandler(
            Func<HttpRequestMessage, Task<bool>> requestPredicate,
            HttpResponseMessage responseMessage)
            : this(requestPredicate, () => Task.FromResult(responseMessage))
        {
        }

        public static RequestHandler Create<TRequestBody, TResponseBody>(
            Uri uri,
            HttpMethod httpMethod,
            Func<TRequestBody, bool> requestBodyPredicate,
            TResponseBody responseBody,
            string contentType = "application/json")
        {
            return new RequestHandler(
                CreateRequestPredicate(uri, httpMethod, requestBodyPredicate),
#pragma warning disable CA2000
                CreateResponse(responseBody, contentType));
#pragma warning restore CA2000
        }

        static Func<HttpRequestMessage, Task<bool>> CreateRequestPredicate(Uri uri, HttpMethod httpMethod)
        {
            return request => Task.FromResult(request.RequestUri == uri && request.Method == httpMethod);
        }

        static Func<HttpRequestMessage, Task<bool>> CreateRequestPredicate<TRequestBody>(
            Uri uri,
            HttpMethod httpMethod,
            Func<TRequestBody, bool> requestBodyPredicate)
        {
            return Predicate;

            async Task<bool> Predicate(HttpRequestMessage request) =>
                request.RequestUri == uri
                && request.Method == httpMethod
                && request.Content is not null
                && await ReadContentAsync<TRequestBody>(request.Content) is { } requestBody
                && requestBodyPredicate(requestBody);
        }

        static async Task<T?> ReadContentAsync<T>(HttpContent content)
        {
            var contentString = await content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(contentString);
        }

        public Task<bool> IsMatch(HttpRequestMessage requestMessage) => requestPredicate(requestMessage);

        public async Task<HttpResponseMessage> Respond(HttpRequestMessage requestMessage)
        {
            ArgumentNullException.ThrowIfNull(requestMessage);
            _receivedRequests.Add(requestMessage);
            if (requestMessage.Content is not null)
            {
                _receivedBodiesJson.Add(await requestMessage.Content.ReadAsStringAsync());
            }

            return await responseHandler();
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