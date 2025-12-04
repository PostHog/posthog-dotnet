// Program.cs

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddPostHog();

var app = builder.Build();

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", (string? email, IPostHogClient postHogClient) =>
{
    if (string.IsNullOrEmpty(email))
    {

        return Results.BadRequest("Please include an email parameter in your request");

    }

    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
#pragma warning disable CA5394 // Not using for security purposes
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
#pragma warning restore CA5394
        ))
        .ToArray();

    postHogClient.Capture(
        distinctId: email,
        eventName: "forecasted weather",
        properties: new Dictionary<string, object>
        {
            ["$set"] = new Dictionary<string, object>
            {
                ["email"] = email
            }
        });

    return Results.Ok(forecast);
})
.WithName("GetWeatherForecast");

app.Run();

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
