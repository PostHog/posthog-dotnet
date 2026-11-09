using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
#if NETSTANDARD2_0 || NETSTANDARD2_1
using Convert = PostHog.Library.Polyfills.Convert;
#endif

namespace PostHog.Emails;

/// <summary>
/// The files of one code send, each named by the SHA-256 of its bytes, checked against the limits PostHog sends
/// within: at most 10 files, and at most 40 MB for the whole email after Base64 encoding.
/// </summary>
internal sealed class HashedAttachments
{
    const int MaxFiles = 10;
    const long MaxMessageBytes = 40_000_000;
    const long HeadersAndMimeBoundariesBytes = 64 * 1024;
    const string MessageLimit = "40 MB after Base64 encoding, about 29 MB of files";

    HashedAttachments(IReadOnlyList<HashedAttachment> files) => Files = files;

    public static HashedAttachments None { get; } = new([]);

    public IReadOnlyList<HashedAttachment> Files { get; }

    public IReadOnlyList<HashedAttachment> Distinct => Files
        .GroupBy(file => file.Sha256, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToList();

    public static HashedAttachments Hash(IReadOnlyList<EmailAttachment> attachments, params string?[] bodies)
    {
        if (attachments.Count > MaxFiles)
        {
            throw new EmailException(
                "too_many_attachments",
                $"An email carries at most {MaxFiles} files, so no email was sent.",
                why: $"An email carries at most {MaxFiles} files.",
                fix: $"Attach at most {MaxFiles} files, and link to any others from the email body.",
                field: "attachments");
        }

        var files = attachments
            .Select((file, index) => new HashedAttachment(index, file, Sha256Hex(file.Content)))
            .ToList();
        var sizes = files
            .Select(file => (long)file.Size)
            .Concat(bodies.Select(body => (long)Encoding.UTF8.GetByteCount(body ?? string.Empty)));
        if (EstimateMessageBytes(sizes) > MaxMessageBytes)
        {
            throw new EmailException(
                "message_too_large",
                "These files make the email larger than PostHog can send, so no email was sent.",
                why: "PostHog's email provider takes emails up to 40 MB, and Base64 encoding makes each file about a "
                     + "third larger.",
                fix: $"Keep the email under {MessageLimit}: attach fewer or smaller files, or link to large files "
                     + "from the email body.",
                field: "attachments");
        }

        return new HashedAttachments(files);
    }

    static long EstimateMessageBytes(IEnumerable<long> sizes)
        => sizes.Aggregate(HeadersAndMimeBoundariesBytes, (total, size) => total + Base64TransferBytes(size));

    static long Base64TransferBytes(long size)
    {
        var base64 = (size + 2) / 3 * 4;
        return ((base64 * 78) + 75) / 76;
    }

    static string Sha256Hex(ReadOnlyMemory<byte> content)
    {
#if NET5_0_OR_GREATER
        var hash = SHA256.HashData(content.Span);
#else
        using var sha256 = new SHA256Managed();
        var hash = MemoryMarshal.TryGetArray(content, out var segment)
            ? sha256.ComputeHash(segment.Array!, segment.Offset, segment.Count)
            : sha256.ComputeHash(content.ToArray());
#endif
#pragma warning disable CA1308 // PostHog names files by lowercase hex.
        return Convert.ToHexString(hash).ToLowerInvariant();
#pragma warning restore CA1308
    }
}

internal sealed record HashedAttachment(int Index, EmailAttachment File, string Sha256)
{
    public int Size => File.Content.Length;
}
