using PostHog.Library;
using PostHog.Versioning;
using System.Runtime.InteropServices;
using System.Text;

namespace PostHog.ErrorTracking;

internal class ExceptionPropertiesBuilder
{
    private const int DEFAULT_MAX_LENGTH = 1024;
    private const int DEFAULT_MAX_LINES = 5;

    public static Dictionary<string, object> Build(
        Dictionary<string, object> properties,
        Exception exception)
    {
        properties["$exception_type"] = exception.GetType().FullName ?? exception.GetType().Name;
        properties["$exception_message"] = exception.Message;
        properties["$exception_level"] = "error";
        properties["$lib"] = "posthog-dotnet";
        properties["$lib_version"] = VersionConstants.Version;
        properties["$os"] = OperatingSystemInfo.Name;
        properties["$os_version"] = OperatingSystemInfo.Version;
        properties["$net_runtime"] = RuntimeInformation.FrameworkDescription;

        properties["$exception_list"] = BuildExceptionList(exception);

        return properties;
    }

    private static List<Dictionary<string, object>> BuildExceptionList(Exception exception)
    {
        var list = new List<Dictionary<string, object>>();
        var seen = new HashSet<Exception>();

        var stack = new Stack<Exception>();
        stack.Push(exception);

        while (stack.Count > 0)
        {
            var ex = stack.Pop();
            if (!seen.Add(ex)) continue;

            list.Add(new Dictionary<string, object>
            {
                ["type"] = ex.GetType().FullName ?? ex.GetType().Name,
                ["value"] = ex.Message,
                ["mechanism"] = new Dictionary<string, object>
                {
                    ["type"] = "generic",
                    ["handled"] = true,
                    ["source"] = "",
                    ["synthetic"] = false
                },
                ["stacktrace"] = new Dictionary<string, object>
                {
                    ["frames"] = BuildStackFrameList(ex),
                    ["type"] = "raw"
                }
            });

            if (ex is AggregateException aex)
            {
                foreach (var inner in aex.Flatten().InnerExceptions.Reverse())
                    if (inner is not null) stack.Push(inner);
            }
            else if (ex.InnerException is not null)
            {
                stack.Push(ex.InnerException);
            }
        }

        return list;
    }

    private static List<Dictionary<string, object>> BuildStackFrameList(Exception exception)
    {
        var stackFrames = new List<Dictionary<string, object>>();
        var stackTrace = new System.Diagnostics.StackTrace(exception, true);

        foreach (var frame in stackTrace.GetFrames() ?? [])
        {
            var method = frame.GetMethod();
            var fileName = frame.GetFileName();
            var lineNumber = frame.GetFileLineNumber();
            var columnNumber = frame.GetFileColumnNumber();

            var frameDetails = new Dictionary<string, object>
            {
                ["platform"] = "custom",
                ["lang"] = "dotnet",
                ["filename"] = Path.GetFileName(fileName) ?? "",
                ["abs_path"] = fileName ?? "",
                ["function"] = method?.Name ?? "",
                ["module"] = method?.DeclaringType?.FullName ?? "",
                ["lineno"] = lineNumber,
                ["colno"] = columnNumber
            };

            if (string.IsNullOrEmpty(fileName))
            {
                stackFrames.Add(frameDetails);
                continue;
            }

            var sourceContext = BuildSourceCodeContext(fileName, lineNumber, columnNumber);

            if (!string.IsNullOrEmpty(sourceContext.ContextLine))
            {
                frameDetails["pre_context"] = sourceContext.PreContext ?? [];
                frameDetails["context_line"] = sourceContext.ContextLine;
                frameDetails["post_context"] = sourceContext.PostContext ?? [];
            }

            stackFrames.Add(frameDetails);
        }

        return stackFrames;
    }

    private static SourceCodeContext BuildSourceCodeContext(
        string absolutePath,
        int lineNumber,
        int columnNumber,
        int maxLengthBytes = 0)
    {
        const int maxLines = DEFAULT_MAX_LINES;
        var lines = File.ReadAllLines(absolutePath);
        var lineIndex = lineNumber - 1;

        int start = Math.Max(0, lineIndex - maxLines);
        int end = Math.Min(lines.Length - 1, lineIndex + maxLines);

        var pre = lines.Skip(start).Take(Math.Max(0, lineIndex - start))
                        .Select(l => TrimToMaxLength(l.Trim('\r', '\n'), maxLengthBytes)).ToList();
        var line = (lineIndex >= 0 && lineNumber <= lines.Length)
                        ? TrimToMaxLength(lines[lineIndex].Trim('\r', '\n'), maxLengthBytes)
                        : "";
        var post = lines.Skip(lineNumber).Take(Math.Max(0, end - lineNumber + 1))
                        .Select(l => TrimToMaxLength(l.Trim('\r', '\n'), maxLengthBytes)).ToList();

        return new SourceCodeContext(pre, line, post);
    }

    // Similar to implementation in posthog-python/posthog/exception_utils.py  strip_string()
    private static string TrimToMaxLength(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str))
        {
            return str;
        }

        if (maxLength <= 0)
        {
            maxLength = DEFAULT_MAX_LENGTH;
        }

        var utf8 = Encoding.UTF8;
        int byteCount = utf8.GetByteCount(str);
        int charCount = str.Length;

        if (byteCount <= maxLength && charCount <= maxLength)
        {
            return str;
        }

        const string ellipsis = "...";
        int ellipsisBytes = utf8.GetByteCount(ellipsis);

        if (maxLength <= ellipsis.Length)
        {
            return ellipsis[..Math.Min(ellipsis.Length, maxLength)];
        }

        if (byteCount > maxLength)
        {
            int cut = maxLength - ellipsisBytes;
            byte[] bytes = utf8.GetBytes(str);

            // back up to avoid cutting inside a multi-byte UTF-8 char
            while (cut > 0 && (bytes[cut] & 0b1100_0000) == 0b1000_0000)
            {
                cut--;
            }

            string head = utf8.GetString(bytes, 0, cut);

            if (head.Length > maxLength - ellipsis.Length)
            {
                head = head[..(maxLength - ellipsis.Length)];
            }

            return head + ellipsis;
        }

        return str[..(maxLength - ellipsis.Length)] + ellipsis;
    }
}
