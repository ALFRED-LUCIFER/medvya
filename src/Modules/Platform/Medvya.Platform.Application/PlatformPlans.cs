namespace Medvya.Platform.Application;

/// <summary>A price in integer minor units (paise, fils) with its ISO currency; null where no price is approved.</summary>
public sealed record PlanPriceView(string Country, string Currency, long? Monthly, long? Yearly, long? AssistMonthly);

/// <summary>One plan version. <see cref="CreatedBy"/> is the console member's name, null for the seeded version 1. A null limit is unlimited.</summary>
public sealed record PlanVersionView(Guid Id, string Code, int Version, bool PerDoctor, bool Custom, int? Doctors, int? Staff, int? Locations,
    int? AssistVisitsPerDoctorMonth, int TrialDays, IReadOnlyList<string> Features, IReadOnlyList<PlanPriceView> Prices, DateTimeOffset CreatedAt,
    string? CreatedBy, string? Reason);

/// <summary>A country's prices in an edit; the currency is the country's (the server sets it).</summary>
public sealed record PlanPriceEdit(string? Country, long? Monthly, long? Yearly, long? AssistMonthly);

/// <summary>
/// The next version of a plan: every field, so a version reads on its own. <see cref="BaseVersion"/> is the version the editor started from;
/// when another edit was saved in between, the save is refused rather than silently overwriting what the other person decided.
/// </summary>
public sealed record PlanEdit(int BaseVersion, int? Doctors, int? Staff, int? Locations, int? AssistVisitsPerDoctorMonth, int? TrialDays,
    IReadOnlyList<string>? Features, IReadOnlyList<PlanPriceEdit>? Prices, string? Reason) : IReasoned;

/// <summary>Stale: the plan has a newer version than the edit's <see cref="PlanEdit.BaseVersion"/>.</summary>
public enum PlanOutcome { Saved, NotFound, Stale }

/// <summary>Versioned subscription plans (MDV-P5-042, ADR-019). Versions are only ever added; a saved version and its audit entry are written together.</summary>
public interface IPlatformPlans
{
    /// <summary>Every version of every plan, by code, newest version first.</summary>
    Task<IReadOnlyList<PlanVersionView>> ListAsync(CancellationToken cancellationToken = default);
    Task<(PlanOutcome Outcome, PlanVersionView? Saved)> SaveAsync(string code, PlanEdit edit, string reason, CancellationToken cancellationToken = default);
}
