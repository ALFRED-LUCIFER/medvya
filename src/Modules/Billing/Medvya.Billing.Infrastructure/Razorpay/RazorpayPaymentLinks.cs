using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Medvya.Billing.Application;
using Medvya.BuildingBlocks.Domain;
using Microsoft.Extensions.Logging;

namespace Medvya.Billing.Infrastructure.Razorpay;

/// <summary>
/// Razorpay Payment Links over a plain typed HttpClient (ADR-023; the official SDK is synchronous). Logs carry the operation and HTTP status
/// only: never keys, request bodies (description, notes) or provider error text.
/// </summary>
public sealed partial class RazorpayPaymentLinks(HttpClient http, IRazorpayCredentialStore credentials, ILogger<RazorpayPaymentLinks> log) : IPaymentLinks
{
    public static readonly Uri BaseAddress = new("https://api.razorpay.com/v1/");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    sealed record CreateBody(long Amount, string Currency, string ReferenceId, string Description, long ExpireBy, NotifyBody Notify, IReadOnlyDictionary<string, string> Notes);
    sealed record NotifyBody(bool Sms, bool Email);
    sealed record LinkBody(string Id, string? ShortUrl, string Status, long AmountPaid, string Currency, List<LinkPaymentBody>? Payments);
    sealed record LinkPaymentBody(string PaymentId, string Status);

    public async Task<PaymentLink> CreateAsync(Guid tenantId, PaymentLinkRequest request, CancellationToken ct)
    {
        // notify off: the clinic sends the link itself (SMS, WhatsApp, on screen), so Razorpay never needs the patient's contact.
        var body = new CreateBody(request.Amount.Minor, request.Amount.Currency.Code, request.ReferenceId, request.Description,
            request.ExpiresAt.ToUnixTimeSeconds(), new(false, false), request.Notes);
        var link = await SendAsync(tenantId, "create", () => new(HttpMethod.Post, "payment_links") { Content = JsonContent.Create(body, options: Json) }, retry: false, ct);
        return new(link.Id, link.ShortUrl ?? "", StatusOf(link.Status));
    }

    public async Task<PaymentLinkState> FetchAsync(Guid tenantId, string linkId, CancellationToken ct)
    {
        var link = await SendAsync(tenantId, "fetch", () => new(HttpMethod.Get, Path(linkId)), retry: true, ct);
        var captured = (link.Payments ?? []).Where(p => p.Status == "captured").Select(p => p.PaymentId).ToList();
        return new(link.Id, StatusOf(link.Status), new Money(link.AmountPaid, Currency.From(link.Currency)), captured);
    }

    public async Task<PaymentLinkStatus> CancelAsync(Guid tenantId, string linkId, CancellationToken ct)
    {
        try
        {
            return StatusOf((await SendAsync(tenantId, "cancel", () => new(HttpMethod.Post, Path(linkId) + "/cancel"), retry: false, ct)).Status);
        }
        catch (PaymentProviderException e) when (e.Status == 400)
        {
            // 400 = no longer cancellable (paid, expired, cancelled) or locked by a payment in progress: report what the link is now.
            return (await FetchAsync(tenantId, linkId, ct)).Status;
        }
    }

    static string Path(string linkId) => "payment_links/" + Uri.EscapeDataString(linkId);

    static PaymentLinkStatus StatusOf(string status) => status switch
    {
        "created" => PaymentLinkStatus.Created,
        "partially_paid" => PaymentLinkStatus.PartiallyPaid,
        "paid" => PaymentLinkStatus.Paid,
        "expired" => PaymentLinkStatus.Expired,
        "cancelled" => PaymentLinkStatus.Cancelled,
        _ => throw new PaymentProviderException(PaymentProviderError.Rejected),
    };

    /// <summary>The one place the Authorization header is made: Basic with the clinic's key today, a partner OAuth Bearer token later (ADR-023).</summary>
    static AuthenticationHeaderValue Authorization(RazorpayCredentials c) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.KeyId}:{c.KeySecret}")));

    // GET is retried once on a transient failure; a POST never is, so a create cannot make two links.
    async Task<LinkBody> SendAsync(Guid tenantId, string operation, Func<HttpRequestMessage> build, bool retry, CancellationToken ct)
    {
        var creds = await credentials.GetAsync(tenantId, ct) ?? throw new PaymentProviderException(PaymentProviderError.NotConnected);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = build();
                request.Headers.Authorization = Authorization(creds);
                using var response = await http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<LinkBody>(Json, ct) ?? throw new PaymentProviderException(PaymentProviderError.Unavailable, (int)response.StatusCode);
                var status = (int)response.StatusCode;
                Failed(log, operation, status);
                var error = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PaymentProviderError.Unauthorized,
                    HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError => PaymentProviderError.Unavailable,
                    _ => PaymentProviderError.Rejected,
                };
                if (error == PaymentProviderError.Unavailable && retry && attempt == 1) continue;
                throw new PaymentProviderException(error, status);
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                Unreachable(log, operation, e.GetType().Name);
                if (retry && attempt == 1) continue;
                throw new PaymentProviderException(PaymentProviderError.Unavailable);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Razorpay {Operation} failed with HTTP {StatusCode}")]
    static partial void Failed(ILogger logger, string operation, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Razorpay {Operation} did not answer: {ErrorType}")]
    static partial void Unreachable(ILogger logger, string operation, string errorType);
}
