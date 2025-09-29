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

            var sourceContext = BuildSourceCodeContext(fileName, lineNumber, columnNumber,
                DEFAULT_MAX_LINES, DEFAULT_MAX_LENGTH);

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
        int maxLines = 0,
        int maxLength = 0)
    {
        var lines = File.ReadAllLines(absolutePath);
        var lineIndex = lineNumber - 1;

        int start = Math.Max(0, lineIndex - maxLines);
        int end = Math.Min(lines.Length - 1, lineIndex + maxLines);
        var truncatedLines = lines.Select(l => l.Trim('\r', '\n')
            .TruncateByBytes(maxLength)
            .TruncateByCharacters(maxLength))
            .ToArray();

        var pre = truncatedLines.Skip(start).Take(Math.Max(0, lineIndex - start)).ToArray();
        var line = (lineIndex >= 0 && lineNumber <= lines.Length) ? truncatedLines[lineIndex] : "";
        var post = truncatedLines.Skip(lineNumber).Take(Math.Max(0, end - lineNumber + 1)).ToArray();

        return new SourceCodeContext(pre, line, post);
    }
}
