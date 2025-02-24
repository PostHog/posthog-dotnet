using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PostHog;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Extensions;

namespace HogTied.Web;

/// <summary>
/// Page view filter that captures page views for PostHog. This demonstrates how you might use PostHog to
/// capture page views in every ASP.NET Core Razor page.
/// </summary>
/// <param name="options">PostHog options.</param>
/// <param name="posthog">The PostHog client.</param>
public class PostHogPageViewFilter(IOptions<PostHogOptions> options, IPostHogClient posthog) : IAsyncPageFilter
{
    readonly PostHogOptions _options = options.Value;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (_options.ProjectApiKey is not null)
        {
            var user = context.HttpContext.User;
            var distinctId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (distinctId is not null)
            {
                posthog.CapturePageView(
                    distinctId,
                    pagePath: context.HttpContext.Request.GetDisplayUrl());
            }
        }

        await next();
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
}