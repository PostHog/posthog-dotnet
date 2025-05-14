using System.Text;
using Wasmtime;

namespace PostHog;
#pragma warning disable

public class WasmClient
{
    readonly Instance _instance;
    readonly Func<int, int> _alloc;
    readonly Memory _memory;
    readonly Action<int, int> _dealloc;

    public WasmClient()
    {
        var engine = new Engine();
        var module = Module.FromFile(engine, "posthog_wasm.wasm");
        var linker = new Linker(engine);
        var store = new Store(engine);

        Memory memory = null!;
        Func<int, int> alloc = null!;
        int responseLength = 0;

        linker.Define("env", "http_post", Function.FromCallback<int, int, int>(store, (ptr, len) =>
        {
            var url = memory.ReadString(ptr, len);

            using var httpClient = new HttpClient();
            var response = httpClient.PostAsync(url, new StringContent("")).Result; // ✅ blocking
            var body = response.Content.ReadAsStringAsync().Result;

            var bytes = Encoding.UTF8.GetBytes(body);
            int respPtr = alloc(bytes.Length);
            memory.WriteBytes(respPtr, bytes);
            responseLength = bytes.Length;
            return respPtr;
        }));

        linker.Define("env", "http_post_len", Function.FromCallback(store, () => responseLength));

        // 👇 Now instantiate
        var instance = linker.Instantiate(store, module);

        // 👇 Set fields after instantiation
        memory = instance.GetMemory("memory")!;

        Console.WriteLine($"WASM memory size: {memory.GetSpan().Length} bytes");

        alloc = instance.GetFunction<int, int>("alloc");
        _dealloc = instance.GetFunction("dealloc").WrapAction<int, int>();

        // Save to instance fields
        _memory = memory;
        _alloc = alloc;
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
        _memory.WriteString(inputPtr, input);

        int outputLen = greetLen(inputPtr, inputBytes.Length);
        int outputPtr = greet(inputPtr, inputBytes.Length);

        string result = _memory.ReadString(outputPtr, outputLen);

        _dealloc(outputPtr, outputLen);
        return result;
    }

    public string Capture()
    {
        // Prepare URL string to send
        string url = "https://google.com";
        var urlBytes = Encoding.UTF8.GetBytes(url);
        int urlPtr = _alloc(urlBytes.Length);
        _memory.WriteBytes(urlPtr, urlBytes);

// Call capture()
        var capture = _instance.GetFunction<int, int, int>("capture");
        int resultPtr = capture(urlPtr, urlBytes.Length);

// Read response (assume <2048 bytes for demo)
        return _memory.ReadString(resultPtr, 2048);
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

    public static string ReadString(this Memory memory, int offset, int length)
    {
        var span = memory.GetSpan(offset, length);
        return Encoding.UTF8.GetString(span);
    }
}