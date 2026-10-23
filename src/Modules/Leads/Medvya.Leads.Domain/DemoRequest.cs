using System.Net.Mail;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Leads.Domain;

/// <summary>
/// A hospital or clinic asking for a demo from the public trust centre (MDV-P1-003). Contact details of a buyer, never patient data:
/// the form has no patient fields and warns against typing any into the message.
/// </summary>
public sealed class DemoRequest
{
    public const int NameMax = 100, OrganisationMax = 150, RoleMax = 100, EmailMax = LeadRules.EmailMax, PhoneMax = 20, MessageMax = 1000;
    public static readonly string[] Countries = ["IN", "AE", "OTHER"];

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.NewGuid();
    [Classification(DataClass.Personal)] public required string Name { get; init; }
    [Classification(DataClass.Internal)] public required string Organisation { get; init; }
    [Classification(DataClass.Internal)] public required string Role { get; init; }
    /// <summary>IN, AE or OTHER.</summary>
    [Classification(DataClass.Internal)] public required string Country { get; init; }
    [Classification(DataClass.Personal)] public string? Email { get; init; }
    [Classification(DataClass.Personal)] public string? Phone { get; init; }
    /// <summary>Free text. Never logged or emailed, because a visitor could ignore the warning and type patient details.</summary>
    [Classification(DataClass.Personal)] public string? Message { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Proof of consent: when the visitor ticked the box under the privacy notice. Null only on rows stored before the notice existed.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset? ConsentedAt { get; init; }
    /// <summary>The <see cref="LeadRules.NoticeVersion"/> the visitor agreed to.</summary>
    [Classification(DataClass.Internal)] public string? NoticeVersion { get; init; }
}

/// <summary>Validation of a submitted demo request. Returns field → error code, never the entered values.</summary>
public static class DemoRequestRules
{
    public static IReadOnlyDictionary<string, string> Validate(DemoRequest r)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        LeadRules.Required(errors, nameof(r.Name), r.Name, DemoRequest.NameMax);
        LeadRules.Required(errors, nameof(r.Organisation), r.Organisation, DemoRequest.OrganisationMax);
        LeadRules.Required(errors, nameof(r.Role), r.Role, DemoRequest.RoleMax);
        if (!DemoRequest.Countries.Contains(r.Country)) errors[nameof(r.Country)] = "required";
        LeadRules.Contact(errors, r.Email, r.Phone);
        if (r.Message?.Length > DemoRequest.MessageMax) errors[nameof(r.Message)] = "tooLong";
        LeadRules.Consent(errors, r.ConsentedAt, r.NoticeVersion);
        return errors;
    }
}

/// <summary>Rules shared by the public-site forms: required text with a length limit, and an email or phone to reply to.</summary>
public static partial class LeadRules
{
    public const int EmailMax = 254;
    /// <summary>
    /// Version of the privacy notice shown above both forms (site.notice.* in packages/i18n). Change it whenever that wording changes,
    /// so each stored consent points at the text the visitor saw. shortcut: legal review of the notice wording (MDV-P1-044).
    /// </summary>
    public const string NoticeVersion = "2026-10-23";

    /// <summary>Required single-line text. A browser never sends a line break from a text input, so one means a crafted post.</summary>
    public static void Required(Dictionary<string, string> errors, string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[field] = "required";
        else if (value.Length > max) errors[field] = "tooLong";
        // Line breaks would also break the founder email: the organisation is in its subject line.
        else if (value.Any(char.IsControl)) errors[field] = "invalid";
    }

    public static void Contact(Dictionary<string, string> errors, string? email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone)) errors["Contact"] = "required";
        // The bare address only: "Name <a@b>" would let a display name through.
        if (!string.IsNullOrWhiteSpace(email) && (email.Length > EmailMax || !MailAddress.TryCreate(email, out var a) || a.Address != email))
            errors["Email"] = "invalid";
        if (!string.IsNullOrWhiteSpace(phone) && !Phone().IsMatch(phone)) errors["Phone"] = "invalid";
    }

    /// <summary>DPDP Act s.6 and UAE PDPL Art. 6: consent is a clear affirmative act we can prove, so nothing is stored without it.</summary>
    public static void Consent(Dictionary<string, string> errors, DateTimeOffset? consentedAt, string? noticeVersion)
    {
        if (consentedAt is null || string.IsNullOrEmpty(noticeVersion)) errors["Consent"] = "consent_required";
    }

    // Digits with optional +, spaces, brackets and dashes; 7 to 20 characters.
    [GeneratedRegex(@"^\+?[0-9][0-9 ()\-]{5,18}[0-9]$")]
    private static partial Regex Phone();
}
