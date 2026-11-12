namespace Medvya.Encounters.Domain;

/// <summary>
/// The follow-up choices of a consultation's plan (MDV-P2-043), as ISO 8601 periods, and the day each falls due from today in the visit's facility.
/// shortcut: the three fixed choices of the design; a template's own suggestions (an antenatal week, "2 days if not better") when templates carry them.
/// </summary>
public static class FollowUpRules
{
    public static readonly IReadOnlyList<string> Periods = ["P2W", "P1M", "P3M"];

    /// <summary>Null: not one of <see cref="Periods"/>.</summary>
    public static DateOnly? DueOn(DateOnly today, string period) => period switch
    {
        "P2W" => today.AddDays(14),
        "P1M" => today.AddMonths(1),
        "P3M" => today.AddMonths(3),
        _ => null,
    };
}
