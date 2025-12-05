# PostHog.AI: AI Observability for .NET

[![NuGet version (PostHog.AI)](https://img.shields.io/nuget/v/PostHog.AI.svg?style=flat-square)](https://www.nuget.org/packages/PostHog.AI/)

`PostHog.AI` provides AI observability features for .NET applications, allowing you to capture detailed information about your AI model interactions and send it to PostHog. This package is designed to be a non-intrusive, easy-to-integrate solution for adding powerful analytics to your AI-powered applications.

## Features

-   **Automatic Capturing**: Automatically capture requests and responses from supported AI client libraries.
-   **Detailed Analytics**: Log model names, token usage, latency, and more.
-   **Error Tracking**: Capture exceptions and error responses from AI API calls.
-   **Seamless Integration**: Simple setup using `IServiceCollection` extension methods.
-   **Library Support**:
    -   Official `OpenAI` library (`openai/openai-dotnet`)
    -   `Azure.AI.OpenAI` library

## Installation

To get started, install the `PostHog.AI` package from NuGet:

```bash
dotnet add package PostHog.AI
```

You will also need to have the core `PostHog` or `PostHog.AspNetCore` package installed and configured.

## Quick Start

### 1. For the Official `OpenAI` Library (`openai/openai-dotnet`)

PostHog.AI provides multiple ways to integrate with the OpenAI client library. Choose the approach that best fits your application.

#### Option A: Typed HTTP Client (Recommended)

Register a typed `PostHogOpenAIHttpClient` that you can inject directly:

```csharp
using OpenAI;
using PostHog.AI;
using System.ClientModel;
using System.ClientModel.Primitives;

var builder = WebApplication.CreateBuilder(args);

// 1. Add the core PostHog client (if not already added)
builder.AddPostHog();

// 2. Add PostHog's AI observability with typed HTTP client
builder.Services.AddPostHogOpenAIHttpClient();

// 3. Register your OpenAIClient using the typed client
builder.Services.AddSingleton(provider =>
{
    var typedClient = provider.GetRequiredService<PostHogOpenAIHttpClient>();
    
    var clientOptions = new OpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(typedClient.HttpClient)
    };
    
    var apiKey = builder.Configuration["OPENAI_API_KEY"];
    var credential = new ApiKeyCredential(apiKey);
    
    return new OpenAIClient(credential, clientOptions);
});
```

#### Option B: HTTP Client Factory Extension

Use the extension method on `IHttpClientFactory` for a more direct approach:

```csharp
using OpenAI;
using PostHog.AI;
using System.ClientModel;
using System.ClientModel.Primitives;

var builder = WebApplication.CreateBuilder(args);

// 1. Add the core PostHog client
builder.AddPostHog();

// 2. Add PostHog's AI observability
builder.Services.AddPostHogOpenAI();

// 3. Register your OpenAIClient using the extension method
builder.Services.AddSingleton(provider =>
{
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.GetPostHogOpenAIHttpClient();
    
    var clientOptions = new OpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(httpClient)
    };
    
    var apiKey = builder.Configuration["OPENAI_API_KEY"];
    var credential = new ApiKeyCredential(apiKey);
    
    return new OpenAIClient(credential, clientOptions);
});
```

#### Option C: Named Client (Original Approach)

You can still use the named client approach if you prefer:

```csharp
builder.Services.AddPostHogOpenAI();

builder.Services.AddSingleton(provider =>
{
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("PostHogOpenAI");
    
    var clientOptions = new OpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(httpClient)
    };
    
    var apiKey = builder.Configuration["OPENAI_API_KEY"];
    var credential = new ApiKeyCredential(apiKey);
    
    return new OpenAIClient(credential, clientOptions);
});
```

### 2. For the `Azure.AI.OpenAI` Library

PostHog.AI provides multiple integration options for Azure OpenAI, similar to the OpenAI client library integration.

#### Option A: Typed HTTP Client (Recommended)

Register a typed `PostHogAzureOpenAIHttpClient` that you can inject directly:

```csharp
using Azure.AI.OpenAI;
using PostHog.AI;

var builder = WebApplication.CreateBuilder(args);

// 1. Add the core PostHog client
builder.AddPostHog();

// 2. Add PostHog's AI observability with typed HTTP client
builder.Services.AddPostHogAzureOpenAIHttpClient();

// 3. Register your OpenAIClient using the typed client
builder.Services.AddSingleton(provider =>
{
    var typedClient = provider.GetRequiredService<PostHogAzureOpenAIHttpClient>();
    
    var clientOptions = new OpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(typedClient.HttpClient)
    };
    
    return new OpenAIClient(
        new Uri(builder.Configuration["AZURE_OPENAI_ENDPOINT"]),
        new AzureKeyCredential(builder.Configuration["AZURE_OPENAI_API_KEY"]),
        clientOptions);
});
```

#### Option B: HTTP Client Factory Extension

Use the extension method on `IHttpClientFactory`:

```csharp
using Azure.AI.OpenAI;
using PostHog.AI;

var builder = WebApplication.CreateBuilder(args);

// 1. Add the core PostHog client
builder.AddPostHog();

// 2. Add PostHog's AI observability
builder.Services.AddPostHogAzureOpenAI();

// 3. Register your OpenAIClient using the extension method
builder.Services.AddSingleton(provider =>
{
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.GetPostHogAzureOpenAIHttpClient();
    
    var clientOptions = new OpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(httpClient)
    };
    
    return new OpenAIClient(
        new Uri(builder.Configuration["AZURE_OPENAI_ENDPOINT"]),
        new AzureKeyCredential(builder.Configuration["AZURE_OPENAI_API_KEY"]),
        clientOptions);
});
```

#### Option C: Direct Handler Attachment

Attach the PostHog handler directly to the Azure OpenAI client:

```csharp
using Azure.AI.OpenAI;
using PostHog.AI;

var builder = WebApplication.CreateBuilder(args);

// 1. Add the core PostHog client
builder.AddPostHog();

// 2. Add PostHog's AI observability
builder.Services.AddPostHogOpenAI(); // Works for both OpenAI and Azure OpenAI

// 3. Register your OpenAIClient and attach PostHog's handler
builder.Services.AddOpenAIClient(new Uri(builder.Configuration["AZURE_OPENAI_ENDPOINT"]),
    new AzureKeyCredential(builder.Configuration["AZURE_OPENAI_API_KEY"]))
    .AddHttpMessageHandler<PostHogOpenAIHandler>();
```

#### Option D: Named Client (Original Approach)

You can still use the named client approach:

```csharp
builder.Services.AddPostHogAzureOpenAI();

builder.Services.AddSingleton(provider =>
{
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("PostHogAzureOpenAI");
    
    var clientOptions = new OpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(httpClient)
    };
    
    return new OpenAIClient(
        new Uri(builder.Configuration["AZURE_OPENAI_ENDPOINT"]),
        new AzureKeyCredential(builder.Configuration["AZURE_OPENAI_API_KEY"]),
        clientOptions);
});
```

Choose the approach that best fits your application architecture.

## How It Works

`PostHog.AI` works by registering a custom `DelegatingHandler` in your application's `HttpClient` pipeline. This handler intercepts HTTP requests made to the OpenAI API, captures the relevant data from both the request and the response, and sends it to your PostHog instance as an AI event (`$ai_generation` or `$ai_embedding`).

This process is entirely automatic and does not require you to change any of your existing code that calls the OpenAI client.

## Configuration

You can configure the `PostHog.AI` integration by providing an `Action<PostHogAIOptions>` when you register the services.

```csharp
builder.Services.AddPostHogOpenAI(options =>
{
    // Set a default distinct_id for all AI events
    options.DefaultDistinctId = "my-default-ai-user";

    // Enable privacy mode to avoid sending input/output content
    options.PrivacyMode = true;

    // Add custom properties to all AI events
    options.Properties = new Dictionary<string, object>
    {
        { "my_custom_property", "value" }
    };

    // Associate events with groups
    options.Groups = new Dictionary<string, object>
    {
        { "company", "my-company-id" }
    };
});
```

### `PostHogAIOptions`

| Property            | Type                       | Description                                                                                             |
| ------------------- | -------------------------- | ------------------------------------------------------------------------------------------------------- |
| `DefaultDistinctId` | `string?`                  | The default `distinct_id` to use for AI events if not specified elsewhere.                              |
| `PrivacyMode`       | `bool`                     | If `true`, the content of inputs and outputs will not be sent to PostHog. Defaults to `false`.          |
| `Properties`        | `Dictionary<string, object>?` | A dictionary of custom properties to include with all AI events.                                        |
| `Groups`            | `Dictionary<string, object>?` | A dictionary of groups to associate with all AI events.                                                 |
| `CaptureImmediate`  | `bool`                     | If `true`, events will be sent immediately instead of being batched. Useful for serverless environments. |

## Questions?

Feel free to open an issue on our [GitHub repository](https://github.com/PostHog/posthog-dotnet) or join our [community Slack](https://posthog.com/slack).
