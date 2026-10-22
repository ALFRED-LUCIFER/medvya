using Medvya.BuildingBlocks.Domain;

namespace Medvya.Leads.Domain;

/// <summary>
/// A doctor or clinic asking to hear when Medvya opens in a country that has no live deployment yet (F-02, MDV-P1-002).
/// Contact details only, never patient data.
/// </summary>
public sealed class WaitlistEntry
{
    public const int NameMax = 100, PhoneMax = 20;
    public static readonly string[] Countries = ["IN", "AE"];

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.NewGuid();
    [Classification(DataClass.Personal)] public required string Name { get; init; }
    /// <summary>IN or AE: the country the visitor chose on the region selector.</summary>
    [Classification(DataClass.Internal)] public required string Country { get; init; }
    [Classification(DataClass.Personal)] public string? Email { get; init; }
    [Classification(DataClass.Personal)] public string? Phone { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
}

public static class WaitlistRules
{
    public static IReadOnlyDictionary<string, string> Validate(WaitlistEntry e)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        LeadRules.Required(errors, nameof(e.Name), e.Name, WaitlistEntry.NameMax);
        if (!WaitlistEntry.Countries.Contains(e.Country)) errors[nameof(e.Country)] = "required";
        LeadRules.Contact(errors, e.Email, e.Phone);
        return errors;
    }
}
