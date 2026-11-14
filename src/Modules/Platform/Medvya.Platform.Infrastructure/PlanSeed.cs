using System.Text.Json;
using Medvya.Platform.Domain;

namespace Medvya.Platform.Infrastructure;

/// <summary>
/// Version 1 of each plan (ADR-019). Prices come from the approved price list, src/Host.Api/PublicSite/pricing.json (embedded here, so there is
/// one list); limits, trial days and features from the 6 and 15 Nov 2026 decisions, which pricing.json does not hold.
/// </summary>
public static class PlanSeed
{
    const string Assist = "ai.ambient_notes";

    // Fixed ids, so the migration inserts the same rows wherever it runs.
    static readonly (Guid Id, string Code, int? Doctors, int? Staff, int? Locations, int? AssistVisits, int TrialDays, string[] Features)[] Decided =
    [
        (Guid.Parse("019a5b40-0000-7000-8000-000000000001"), "solo", 1, 2, 1, 300, 14, ["patient_app.booking"]),
        (Guid.Parse("019a5b40-0000-7000-8000-000000000002"), "clinic", null, 15, null, 300, 14, [Assist, "patient_app.booking"]),
        (Guid.Parse("019a5b40-0000-7000-8000-000000000003"), "hospital", null, null, null, null, 0, [Assist, "auth.sso", "integrations.his", "patient_app.booking"]),
    ];

    public static IReadOnlyList<PlanVersion> Versions { get; } = Read();

    static List<PlanVersion> Read()
    {
        using var stream = typeof(PlanSeed).Assembly.GetManifestResourceStream("Medvya.Platform.pricing.json")
            ?? throw new InvalidOperationException("pricing.json is not embedded.");
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var pricing = doc.RootElement.GetProperty("Pricing");
        var approved = new DateTimeOffset(DateOnly.Parse(pricing.GetProperty("Approved").GetString()!, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.MinValue, TimeSpan.Zero);
        var reason = $"Approved price list {pricing.GetProperty("Version").GetString()} (ADR-019)";
        var assist = pricing.GetProperty("Assist");
        return [.. pricing.GetProperty("Plans").EnumerateArray().Select(plan =>
        {
            var code = plan.GetProperty("Code").GetString()!;
            var d = Decided.Single(x => x.Code == code);
            var version = new PlanVersion
            {
                Id = d.Id, Code = code, Version = 1, PerDoctor = Flag(plan, "PerDoctor"), Custom = Flag(plan, "Custom"), Doctors = d.Doctors, Staff = d.Staff,
                Locations = d.Locations, AssistVisitsPerDoctorMonth = d.AssistVisits, TrialDays = d.TrialDays, Features = d.Features, CreatedAt = approved, Reason = reason,
            };
            foreach (var (country, currency) in PlanPrice.Currencies)
            {
                var price = plan.GetProperty("Prices").GetProperty(country);
                if (price.GetProperty("Currency").GetString() != currency) throw new InvalidOperationException($"pricing.json: {code} in {country} is not in {currency}.");
                version.Prices.Add(new PlanPrice
                {
                    PlanVersionId = d.Id, Country = country, Currency = currency, Monthly = Amount(price, "Monthly"), Yearly = Amount(price, "Yearly"),
                    // The add-on is sold only where the plan does not include Assist.
                    AssistMonthly = d.Features.Contains(Assist) ? null : Amount(assist.GetProperty(country), "Monthly"),
                });
            }
            return version;
        })];
    }

    static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.GetBoolean();

    static long? Amount(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetInt64() : null;
}
