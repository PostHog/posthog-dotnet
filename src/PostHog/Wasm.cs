namespace PostHog;

using System.Runtime.InteropServices;
using Wasmtime;

[StructLayout(LayoutKind.Sequential)]
#pragma warning disable CA1815
public struct HttpResponse
{
    public int Status;
    public nint BodyPtr;  // Use nint instead of IntPtr for better WebAssembly compatibility
    public nuint BodyLen;
}
