var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Deploy smoke tests (MDV-P0-007) read the version from here.
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }));

app.Run();
