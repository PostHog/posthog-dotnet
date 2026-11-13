using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PostHog;
using PostHog.Config;
using PostHog.Emails;

namespace EmailsTests;

public class TheSendAsyncMethodOverTheNetwork
{
    public static TheoryData<string> Wirings => new()
    {
        "new PostHogClient",
        "new PostHogClient with the app's IHttpClientFactory",
        "services.AddPostHog",
    };

    [Theory]
    [MemberData(nameof(Wirings))]
    public async Task NeverFollowsARedirectOfAnUpload(string wiring)
    {
        var elsewhere = new RecordingHandler();
        elsewhere.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        await using var other = LocalHttpServer.Serve(elsewhere);
        var posthog = new FakePostHogEmails();
        await using var app = LocalHttpServer.Serve(posthog);
        await using var storage = LocalHttpServer.Serve(posthog);
        posthog.StorageOrigin = storage.Origin;
        posthog.RedirectStorage(new Uri(other.Origin + "/copy"));
        await using var client = Client(wiring, new Uri(app.Origin));

        var error = await Assert.ThrowsAsync<EmailException>(() => client.Emails.SendAsync(Invoice()));

        Assert.Empty(elsewhere.Requests);
        Assert.Empty(posthog.Delivered);
        Assert.Equal(0, posthog.StorageWrites);
        Assert.Equal("attachment_upload_failed", error.Code);
        Assert.Equal("attachments__0", error.Field);
        Assert.Contains("invoice.pdf", error.Message, StringComparison.Ordinal);
        var shown = string.Join("\n", error.Message, error.Why, error.Fix, error.ToString());
        Assert.DoesNotContain(other.Origin, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(storage.Origin, shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeliversASendWithFilesInFlightWhenTheHostShutsDown()
    {
        var posthog = new FakePostHogEmails();
        await using var app = LocalHttpServer.Serve(posthog);
        await using var storage = LocalHttpServer.Serve(posthog);
        posthog.StorageOrigin = storage.Origin;
        posthog.DelayStorage(TimeSpan.FromMilliseconds(300));
        await using var host = Client("services.AddPostHog", new Uri(app.Origin));

        var send = host.Emails.SendAsync(Invoice());
        await posthog.StorageHeld.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        await host.DisposeAsync();

        await send;
        Assert.Equal("invoice.pdf", Assert.Single(Assert.Single(posthog.Delivered).Attachments).Filename);
        Assert.Equal(1, posthog.StorageWrites);
    }

    static SendCodeEmailOptions Invoice() => new()
    {
        To = "user@example.com",
        TemplateKey = "invoice",
        Subject = "Your invoice",
        Html = "<p>Thanks!</p>",
        Attachments = [new EmailAttachment { Filename = "invoice.pdf", Content = new byte[] { 0x25, 0x50, 0x44, 0x46 } }],
    };

    static WiredClient Client(string wiring, Uri appHost)
    {
        void Configure(PostHogOptions options)
        {
            options.ProjectToken = "fake-project-token";
            options.SecretKey = FakePostHogEmails.SecretKey;
            options.AppHost = appHost;
            options.AllowInsecureAppHost = true;
            options.EmailRequestMaxRetries = 0;
        }

        if (wiring == "new PostHogClient")
        {
            var options = new PostHogOptions();
            Configure(options);
            var client = new PostHogClient(options);
            return new WiredClient(client.Emails, client);
        }

        if (wiring == "new PostHogClient with the app's IHttpClientFactory")
        {
            var options = new PostHogOptions();
            Configure(options);
            var appServices = new ServiceCollection().AddHttpClient().BuildServiceProvider();
            var factoryWhoseClientsFollowRedirects = appServices.GetRequiredService<IHttpClientFactory>();
            var client = new PostHogClient(options, httpClientFactory: factoryWhoseClientsFollowRedirects);
            return new WiredClient(client.Emails, client, appServices);
        }

        var services = new ServiceCollection();
        services.AddPostHog(builder => builder.PostConfigure(Configure));
        var provider = services.BuildServiceProvider();
        return new WiredClient(provider.GetRequiredService<IPostHogEmails>(), provider);
    }

    sealed class WiredClient(IPostHogEmails emails, params IAsyncDisposable[] owners) : IAsyncDisposable
    {
        public IPostHogEmails Emails { get; } = emails;

        public async ValueTask DisposeAsync()
        {
            foreach (var owner in owners)
            {
                await owner.DisposeAsync();
            }
        }
    }
}
