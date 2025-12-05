using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PostHog.AI.OpenAI;

public class PosthogParams
{
    public string? DistinctId { get; set; }
    public string TraceId { get; set; } = Guid.NewGuid().ToString();
    public bool PrivacyMode { get; set; }

    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? Properties { get; set; }

    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? Groups { get; set; }
    public string? ModelOverride { get; set; }
    public string? ProviderOverride { get; set; }
    public string? CostOverride { get; set; }
    public int? WebSearchCount { get; set; }
    public bool CaptureImmediate { get; set; }
}
