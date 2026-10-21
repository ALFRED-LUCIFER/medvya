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
    public const int NameMax = 100, OrganisationMax = 150, RoleMax = 100, EmailMax = 254, PhoneMax = 20, MessageMax = 1000;
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
}

/// <summary>Validation of a submitted demo request. Returns field → error code, never the entered values.</summary>
public static partial class DemoRequestRules
{
    public static IReadOnlyDictionary<string, string> Validate(DemoRequest r)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        Required(errors, nameof(r.Name), r.Name, DemoRequest.NameMax);
        Required(errors, nameof(r.Organisation), r.Organisation, DemoRequest.OrganisationMax);
        Required(errors, nameof(r.Role), r.Role, DemoRequest.RoleMax);
        if (!DemoRequest.Countries.Contains(r.Country)) errors[nameof(r.Country)] = "required";
        if (string.IsNullOrWhiteSpace(r.Email) && string.IsNullOrWhiteSpace(r.Phone)) errors["Contact"] = "required";
        if (!string.IsNullOrWhiteSpace(r.Email) && (r.Email.Length > DemoRequest.EmailMax || !MailAddress.TryCreate(r.Email, out var a) || a.Address != r.Email))
            errors[nameof(r.Email)] = "invalid";
        if (!string.IsNullOrWhiteSpace(r.Phone) && !Phone().IsMatch(r.Phone)) errors[nameof(r.Phone)] = "invalid";
        if (r.Message?.Length > DemoRequest.MessageMax) errors[nameof(r.Message)] = "tooLong";
        return errors;
    }

    static void Required(Dictionary<string, string> errors, string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[field] = "required";
        else if (value.Length > max) errors[field] = "tooLong";
    }

    // Digits with optional +, spaces, brackets and dashes; 7 to 20 characters.
    [GeneratedRegex(@"^\+?[0-9][0-9 ()\-]{5,18}[0-9]$")]
    private static partial Regex Phone();
}
