using System.Security.Claims;
using HogTied.Web;
using HogTied.Web.Data;
using HogTied.Web.FeatureManagement;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PostHog;
using PostHog.Config;
using PostHog.Library;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddPostHog(options =>
{
    // In general this call is not needed. The default settings are in the "PostHog" configuration section.
    // This is here so I can easily switch testing against my local install and production.
    options.UseConfigurationSection(builder.Configuration.GetSection("PostHogLocal"));

    options.PostConfigure(o =>
    {
        o.SuperProperties.Add("app_name", "HogTied");
    });

    // Logs requests and responses. Fine for a sample project. Probably not good for production.
    options.ConfigureHttpClient(httpClientBuilder => httpClientBuilder.AddHttpMessageHandler<LoggingHttpMessageHandler>());

    // Enables PostHog as a provider for ASP.NET Core's feature management system.
    options.UseFeatureManagement<HogTiedFeatureFlagContextProvider>();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services
    .AddTransient<LoggingHttpMessageHandler>()
    .AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString))
    .AddDatabaseDeveloperPageExceptionFilter()
    .ConfigureApplicationCookie(options =>
    {
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var userPrincipal = context.Principal;

                Console.WriteLine($"Cookie validated for user: {userPrincipal?.Identity?.Name}");

                var userId = userPrincipal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var postHogClient = context.HttpContext.RequestServices.GetRequiredService<IPostHogClient>();
                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                if (userId is not null)
                {
                    var user = await db.Users.FindAsync(userId);
                    if (user is not null)
                    {
                        // This stores information about the user in PostHog.
                        await postHogClient.IdentifyAsync(
                            userId,
                            user.Email,
                            user.UserName,
                            personPropertiesToSet: new()
                            {
                                ["phone"] = user.PhoneNumber ?? "unknown",
                                ["email_confirmed"] = user.EmailConfirmed,
                            },
                            personPropertiesToSetOnce: new()
                            {
                                ["joined"] = DateTime.UtcNow // If this property is already set, it won't be overwritten.
                            },
                            context.HttpContext.RequestAborted);
                    }
                }
            }
        };
    })
    .AddDefaultIdentity<IdentityUser>(
        options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddRazorPages()
    .AddMvcOptions(options => options.Filters.Add<PostHogPageViewFilter>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
