using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Host.Api.PublicSite;

/// <summary>Fields every public form shares: who you are and how to reach you, plus the two bot traps.</summary>
public class ContactForm
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Protected time the form was shown; too fast or too old means a bot or a stale page.</summary>
    public string? Started { get; set; }
    /// <summary>Honeypot, hidden from people. Anything typed here is a bot.</summary>
    public string? Website { get; set; }
}

/// <summary>
/// Bot and abuse protection for the public forms (MDV-P1-003): a minimum fill time and a per-client-IP quota per hour, shared by
/// all forms. Checked in the page handler, so reading a page never counts and a refused visitor gets the form back with a message.
/// </summary>
public sealed class PublicFormGuard : IDisposable
{
    readonly IDataProtector protector;
    readonly TimeSpan minimumFillTime;
    readonly PartitionedRateLimiter<string> limiter;

    public PublicFormGuard(IDataProtectionProvider dataProtection, IConfiguration config)
    {
        protector = dataProtection.CreateProtector("PublicSite.Form.Started");
        minimumFillTime = TimeSpan.FromSeconds(config.GetValue("PublicSite:MinimumFillSeconds", 3));
        var perHour = config.GetValue("PublicSite:SubmissionsPerHour", 5);
        limiter = PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetFixedWindowLimiter(ip,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perHour, Window = TimeSpan.FromHours(1) }));
    }

    public string Start() => protector.Protect(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

    public bool StartedInTime(string? token)
    {
        try
        {
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(protector.Unprotect(token ?? ""), CultureInfo.InvariantCulture));
            return age >= minimumFillTime && age <= TimeSpan.FromDays(1);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or OverflowException)
        {
            return false;
        }
    }

    // shortcut: the quota is in memory, so each app instance counts on its own; move it to a shared store before scaling out (MDV-P0-007).
    public bool TryAcquire(HttpContext ctx)
    {
        using var lease = limiter.AttemptAcquire(Client(ctx.Connection.RemoteIpAddress));
        return lease.IsAcquired;
    }

    /// <summary>
    /// The quota's client key. One IPv6 subscriber gets a whole /64, so a per-address quota would be no quota at all; an IPv4
    /// client seen through a dual-stack socket arrives as ::ffff:a.b.c.d and must keep its own key, not share ::/64 with every other.
    /// </summary>
    public static string Client(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    public void Dispose() => limiter.Dispose();
}

/// <summary>A public page with a contact form: the shared submit flow, errors and the no-JavaScript focus rules.</summary>
public abstract class PublicFormPage(SiteText text, PublicFormGuard guard) : PublicPage(text)
{
    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();
    /// <summary>Form-level problem: "rateLimited" or "expired".</summary>
    public string? FormError { get; private set; }
    public string Started { get; private set; } = "";
    public bool Sent => Request.Query.ContainsKey("sent");

    public abstract ContactForm Contact { get; }
    /// <summary>Error fields in page order, with the id of the input each error belongs to.</summary>
    public abstract IReadOnlyList<(string Field, string Id)> Fields { get; }
    /// <summary>Message key for an error on <paramref name="field"/> other than "too long".</summary>
    protected virtual string ErrorKey(string field) => $"site.form.error.{field}";

    /// <summary>Prefix of the form's element ids, e.g. "demo" for demo-name.</summary>
    public abstract string FormId { get; }

    public string? FirstInvalid => Fields.FirstOrDefault(f => Errors.ContainsKey(f.Field)).Id;

    // Attribute values for an input; null leaves the attribute out.
    public string? Invalid(params string[] fields) => fields.Any(Errors.ContainsKey) ? "true" : null;
    /// <summary>Without JavaScript, autofocus is what moves the visitor to the first invalid field.</summary>
    public string? Focus(string id) => id == FirstInvalid ? "autofocus" : null;
    public string? Described(string id, params string[] fields) => fields.Any(Errors.ContainsKey) ? $"{id}-error" : null;

    public string FieldError(string field) =>
        Errors.TryGetValue(field, out var code) ? T(code == "tooLong" ? "site.form.error.tooLong" : ErrorKey(field)) : "";

    protected void NewForm() => Started = guard.Start();

    /// <summary>Bots get the same answer as people and nothing is stored; people get errors or, once stored, <paramref name="sentUrl"/>.</summary>
    protected async Task<IActionResult> SubmitAsync(IReadOnlyDictionary<string, string> errors, Func<Task> store, string sentUrl)
    {
        if (!string.IsNullOrEmpty(Contact.Website)) return LocalRedirect(sentUrl);
        if (!guard.StartedInTime(Contact.Started)) return Again("expired", StatusCodes.Status400BadRequest);
        Errors = errors;
        if (errors.Count > 0) return Again(null, StatusCodes.Status400BadRequest);
        if (!guard.TryAcquire(HttpContext)) return Again("rateLimited", StatusCodes.Status429TooManyRequests);
        await store();
        return LocalRedirect(sentUrl);
    }

    // Shows the form again with what was typed, a fresh start time and the errors.
    PageResult Again(string? formError, int status)
    {
        FormError = formError;
        Response.StatusCode = status;
        NewForm();
        return Page();
    }

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
