#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace System.Net.Http;

internal static class HttpResponseMessageExtensions
{
    public static Task<Stream> ReadAsStreamAsync(this HttpContent httpContent, CancellationToken cancellationToken)
    {
        return httpContent.ReadAsStreamAsync();
    }
}
#endif