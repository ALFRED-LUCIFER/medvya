using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

/// <summary>One decision as the desk records it. Reminders carry a channel; no other type does.</summary>
public sealed record ConsentDecisionInput(ConsentType Type, ConsentChannel? Channel, bool Granted);

/// <summary>The decisions and the policy version the patient was shown (the screen reads it from the same wording).</summary>
public sealed record ConsentSet(string PolicyVersion, IReadOnlyList<ConsentDecisionInput> Decisions);

/// <summary>The current decision (newest receipt) for one type and channel, with when it was made and under which wording.</summary>
public sealed record ConsentState(ConsentType Type, ConsentChannel? Channel, bool Granted, DateTimeOffset DecidedAt, string PolicyVersion);

public static class ConsentPolicy
{
    /// <summary>
    /// The currently published consent wording (also web/clinic consents.ts). A decision made against another version is refused.
    /// shortcut: DRAFT wording, not legally reviewed (MDV-P1-044), and one built-in version instead of a per-tenant published
    /// privacy notice; go-live needs the reviewed en/hi/ar text and a publish flow.
    /// </summary>
    public const string CurrentVersion = "2026-11-draft";
}

public static class ConsentRules
{
    /// <summary>
    /// Registration needs the treatment decision granted (locked on, not a choice) and the current policy version; every other
    /// decision is optional. Each (type, channel) is decided once per submission.
    /// </summary>
    public static ConsentSet ForRegistration(ConsentSet? consents) => Check(consents, treatmentRequired: true);

    /// <summary>A later change: same shape rules, but treatment cannot be withdrawn here (that ends the relationship, not a toggle).</summary>
    public static ConsentSet ForChange(ConsentSet? consents) => Check(consents, treatmentRequired: false);

    /// <summary>"IN", "AE", or "AE-DXB" when a UAE patient's emirate (which selects the HIE) is known.</summary>
    public static string Jurisdiction(string countryCode, string? region) =>
        countryCode == "AE" && !string.IsNullOrEmpty(region) ? $"AE-{region}" : countryCode;

    static ConsentSet Check(ConsentSet? consents, bool treatmentRequired)
    {
        if (consents is null || consents.Decisions.Count == 0) throw new PatientValidationException([new("consents", "required")]);
        List<ValidationError> errors = [];
        if (consents.PolicyVersion != ConsentPolicy.CurrentVersion) errors.Add(new("consents.policyVersion", "stale"));
        foreach (var d in consents.Decisions)
        {
            var validChannel = d.Type == ConsentType.Reminders ? d.Channel is { } c && Enum.IsDefined(c) : d.Channel is null;
            if (!Enum.IsDefined(d.Type) || !validChannel) errors.Add(new("consents", "invalid"));
        }
        if (consents.Decisions.GroupBy(d => (d.Type, d.Channel)).Any(g => g.Count() > 1)) errors.Add(new("consents", "duplicate"));
        var treatment = consents.Decisions.Where(d => d.Type == ConsentType.Treatment).ToList();
        if (treatment.Any(d => !d.Granted) || (treatmentRequired && treatment.Count == 0)) errors.Add(new("consents.treatment", "required"));
        if (errors.Count > 0) throw new PatientValidationException(errors);
        return consents;
    }
}

/// <summary>
/// Consent receipts of the current tenant's patients. Other modules (reminders, MDV-P1-032/033) ask through this contract and never
/// read the tables.
/// </summary>
public interface IPatientConsents
{
    /// <summary>
    /// Whether the newest receipt for this type and channel is a grant. No receipt means no consent (fail closed). Call before every
    /// send: a withdrawal takes effect on the next call.
    /// </summary>
    Task<bool> IsGrantedAsync(Guid patientId, ConsentType type, ConsentChannel? channel = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current decision per type and channel for a patient of the current facility (what the staff screen shows); a type with no
    /// receipt is absent, which means no consent. Null when there is no such patient at this facility. Newest wins, a tie counts as withdrawn.
    /// </summary>
    Task<IReadOnlyList<ConsentState>?> CurrentAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records changed decisions as new receipts (history stays) for a patient of the current facility, audited in the same
    /// transaction. Null when there is no such patient at this facility (also another facility's): nothing is written.
    /// </summary>
    /// <exception cref="PatientValidationException">See <see cref="ConsentRules.ForChange"/>.</exception>
    Task<IReadOnlyList<ConsentDecisionInput>?> ChangeAsync(Guid patientId, ConsentSet consents, CancellationToken cancellationToken = default);
}
