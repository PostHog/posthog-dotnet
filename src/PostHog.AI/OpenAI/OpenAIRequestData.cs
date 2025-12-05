using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PostHog.AI.OpenAI;

public class OpenAIRequestData
{
    public string? Model { get; set; }
    public object? Messages { get; set; }
    public object? Input { get; set; }
    public object? Tools { get; set; }
    public Uri? RequestUri { get; set; }
    public OpenAIEndpointType EndpointType { get; set; }

    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? ModelParameters { get; set; }
}
