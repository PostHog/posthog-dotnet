using System.Text;
using Wasmtime;
using System.Net.Http; // Added for HttpClient, HttpMethod, etc.

namespace PostHog;
#pragma warning disable

public class WasmClient
{
    readonly Instance _instance;
    readonly Func<int, int> _alloc;
    readonly Memory _memory;
    readonly Action<int, int> _dealloc;
    int _latestHttpRequestResponseLength = 0;

    public WasmClient()
    {
        var engine = new Engine();
        var module = Module.FromFile(engine, "posthog_wasm.wasm");
        var linker = new Linker(engine);
        var store = new Store(engine);

        Memory memory = null!;
        Func<int, int> allocFunc = null!; // Renamed to avoid conflict if used before assignment

        // Define http_request and http_request_len imports
        linker.Define("env", "http_request", Function.FromCallback<int, int, int, int, int, int, int>(store,
            (urlPtr, urlLen, methodPtr, methodLen, bodyPtr, bodyLen) =>
            {
                var url = memory.ReadString(urlPtr, (uint)urlLen); // Use local 'memory'
                var method = memory.ReadString(methodPtr, (uint)methodLen); // Use local 'memory'

                using var httpClient = new HttpClient();
                var httpMethod = new HttpMethod(method.Trim().ToUpperInvariant());
                var requestMessage = new HttpRequestMessage(httpMethod, url);

                if (bodyLen > 0)
                {
                    var requestBodyBytes = new byte[bodyLen];
                    memory.ReadBytes(bodyPtr, requestBodyBytes); // Use local 'memory'
                    requestMessage.Content = new ByteArrayContent(requestBodyBytes);
                    requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                }
                else if (httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod == HttpMethod.Patch)
                {
                    requestMessage.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                var response = httpClient.SendAsync(requestMessage).Result; // ✅ blocking
                response.EnsureSuccessStatusCode();
                var responseBody = response.Content.ReadAsStringAsync().Result;
                var responseBodyBytes = Encoding.UTF8.GetBytes(responseBody);

                // 'allocFunc' (local variable) will be assigned after instantiation but captured by the lambda.
                int responseBodyPtrInWasm = allocFunc(responseBodyBytes.Length);
                memory.WriteBytes(responseBodyPtrInWasm, responseBodyBytes); // Use local 'memory'

                _latestHttpRequestResponseLength = responseBodyBytes.Length; // Access field
                return responseBodyPtrInWasm;
            }
        ));

        linker.Define("env", "http_request_len", Function.FromCallback<int>(store,
            () => _latestHttpRequestResponseLength // Access field
        ));

        // 👇 Now instantiate
        var instance = linker.Instantiate(store, module);

        // 👇 Set fields after instantiation
        // The local 'memory' and 'allocFunc' variables used in the lambdas get their values here.
        memory = instance.GetMemory("memory")!;

        Console.WriteLine($"WASM memory size: {memory.GetSpan().Length} bytes");

        allocFunc = instance.GetFunction<int, int>("alloc")!;
        _dealloc = instance.GetFunction("dealloc").WrapAction<int, int>();

        // Save to instance fields
        _memory = memory;
        _alloc = allocFunc;
        _instance = instance;
    }

    public int Add(int a, int b)
    {
        var add = _instance.GetFunction<int, int, int>("add");
        if (add is null)
        {
            throw new InvalidOperationException("Could not find function add");
        }
        return add.Invoke(a, b);
    }

    public string Greet(string input)
    {
        var greet = _instance.GetFunction<int, int, int>("greet")
                    ?? throw new InvalidOperationException("Could not find function greet");
        var greetLen = _instance.GetFunction<int, int, int>("greet_len")
                       ?? throw new InvalidOperationException("Could not find function greet_len");

        byte[] inputBytes = Encoding.UTF8.GetBytes(input);

        int inputPtr = _alloc(inputBytes.Length);
        _memory.WriteString(inputPtr, input); // Assuming WriteString extension

        int outputLen = greetLen(inputPtr, inputBytes.Length);
        int outputPtr = greet(inputPtr, inputBytes.Length);

        string result = _memory.ReadString(outputPtr, (uint)outputLen);

        _dealloc(outputPtr, outputLen);
        return result;
    }

    // Example of how you might call the new 'request' function from C#
    // This is a new method, not 'Capture'
    public string MakeHttpRequest(string url, string method, string? body = null)
    {
        var requestFunc = _instance.GetFunction<int, int, int, int, int, int, int>("request")
            ?? throw new InvalidOperationException("Wasm 'request' function not found.");

        byte[] urlBytes = Encoding.UTF8.GetBytes(url);
        int urlPtr = _alloc(urlBytes.Length);
        _memory.WriteBytes(urlPtr, urlBytes);

        byte[] methodBytes = Encoding.UTF8.GetBytes(method);
        int methodPtr = _alloc(methodBytes.Length);
        _memory.WriteBytes(methodPtr, methodBytes);

        int bodyPtr = 0;
        int bodyLen = 0;
        byte[]? requestBodyBytes = null;
        if (!string.IsNullOrEmpty(body))
        {
            requestBodyBytes = Encoding.UTF8.GetBytes(body);
            bodyPtr = _alloc(requestBodyBytes.Length);
            _memory.WriteBytes(bodyPtr, requestBodyBytes);
            bodyLen = requestBodyBytes.Length;
        }

        int responseBodyPtrInWasm = requestFunc(urlPtr, urlBytes.Length, methodPtr, methodBytes.Length, bodyPtr, bodyLen);

        // We need the length of the response. The C# host callback for http_request_len stores this.
        // So, _latestHttpRequestResponseLength should have the correct value after the Wasm 'request' call,
        // because 'request' internally calls the 'http_request' host import.
        int responseBodyLen = _latestHttpRequestResponseLength;

        string responseString = _memory.ReadString(responseBodyPtrInWasm, (uint)responseBodyLen);

        // Deallocate all allocated memory in Wasm
        _dealloc(urlPtr, urlBytes.Length);
        _dealloc(methodPtr, methodBytes.Length);
        if (requestBodyBytes != null)
        {
            _dealloc(bodyPtr, requestBodyBytes.Length);
        }
        _dealloc(responseBodyPtrInWasm, responseBodyLen); // The Wasm 'request' function allocates this

        return responseString;
    }

    public string Capture(string eventName, string distinctId, string apiKey)
    {
        var eventBytes = Encoding.UTF8.GetBytes(eventName);
        int eventPtr = _alloc(eventBytes.Length);
        _memory.WriteBytes(eventPtr, eventBytes);

        var distinctIdBytes = Encoding.UTF8.GetBytes(distinctId);
        int distinctIdPtr = _alloc(distinctIdBytes.Length);
        _memory.WriteBytes(distinctIdPtr, distinctIdBytes);

        var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);
        int apiKeyPtr = _alloc(apiKeyBytes.Length);
        _memory.WriteBytes(apiKeyPtr, apiKeyBytes);

        var captureFunc = _instance.GetFunction<int, int, int, int, int, int, int>("capture")
            ?? throw new InvalidOperationException("Wasm 'capture' function not found.");

        int resultPtr = captureFunc(
            eventPtr,
            eventBytes.Length,
            distinctIdPtr,
            distinctIdBytes.Length,
            apiKeyPtr,
            apiKeyBytes.Length);

        int responseLen = _latestHttpRequestResponseLength; // This is a guess, depends on http_post_len behavior
        if (responseLen == 0) responseLen = 2048; // Fallback to original risky assumption

        string result =  _memory.ReadString(resultPtr, (uint)responseLen);
        _dealloc(eventPtr, eventBytes.Length);
        _dealloc(distinctIdPtr, distinctIdBytes.Length);
        _dealloc(apiKeyPtr, apiKeyBytes.Length);
        _dealloc(resultPtr, responseLen); // Deallocating the result from capture
        return result;
    }
}

public static class WasmMemoryExtensions
{
    public static void WriteBytes(this Memory memory, int offset, byte[] data)
    {
        var memorySpan = memory.GetSpan();

        if (offset + data.Length > memorySpan.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), $"Not enough space in WASM memory. {offset} + {data.Length} > {memorySpan.Length}");

        var destination = memorySpan.Slice(offset, data.Length);
        data.CopyTo(destination);
    }

    // Added WriteString for convenience, used in Greet
    public static void WriteString(this Memory memory, int offset, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        memory.WriteBytes(offset, bytes);
    }

    public static string ReadString(this Memory memory, int offset, uint length) // Changed length to uint to match ReadString more closely
    {
        var span = memory.GetSpan(offset, (int)length);
        return Encoding.UTF8.GetString(span);
    }

    // Overload for ReadBytes used in http_request callback
    public static void ReadBytes(this Memory memory, int offset, byte[] buffer)
    {
        var memorySpan = memory.GetSpan();
        if (offset + buffer.Length > memorySpan.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), $"Not enough space in WASM memory to read {buffer.Length} bytes from {offset}.");

        var source = memorySpan.Slice(offset, buffer.Length);
        source.CopyTo(buffer);
    }
}
