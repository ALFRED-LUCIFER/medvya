namespace Host.Api.Middleware;

/// <summary>
/// Retires mobile builds in the field (#1417): both apps send <see cref="Header"/> with their version on every request, and a build below
/// <c>MobileApp:MinimumVersion</c> gets 426 with the problem code <c>app.upgrade_required</c>, which the app shows as "please update".
/// Requests without the header (the web apps) are never touched. An empty setting switches it off; an unreadable one stops the host.
/// </summary>
public static class MobileAppVersion
{
    public const string Header = "X-Medvya-App-Version";

    public static WebApplication UseMobileAppVersion(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Configuration["MobileApp:MinimumVersion"] is not { Length: > 0 } setting) return app;
        if (!Version.TryParse(setting, out var minimum)) throw new InvalidOperationException("MobileApp:MinimumVersion must be a version such as 1.4.0.");
        app.Use((ctx, next) => ctx.Request.Headers[Header].ToString() is { Length: > 0 } sent && TooOld(sent, minimum)
            ? Results.Problem(statusCode: StatusCodes.Status426UpgradeRequired, title: "Update the app to keep using Medvya.",
                extensions: new Dictionary<string, object?> { ["code"] = "app.upgrade_required" }).ExecuteAsync(ctx)
            : next(ctx));
        return app;
    }

    /// <summary>A version the server cannot read counts as too old: the header is the app's claim, so it fails closed.</summary>
    public static bool TooOld(string sent, Version minimum) => !Version.TryParse(sent, out var version) || version < minimum;
}
