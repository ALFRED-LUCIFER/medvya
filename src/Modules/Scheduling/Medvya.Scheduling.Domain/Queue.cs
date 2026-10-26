using System.Globalization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Scheduling.Domain;

/// <summary>
/// The waiting-room token issued at check-in (MDV-P1-031), e.g. "A-07": a letter per doctor (or resource) for the facility's
/// day and a number per letter. It is what the queue TV shows instead of a name. Unique per facility, day, letter and number.
/// </summary>
public sealed class QueueToken
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>The facility-local day the token belongs to.</summary>
    [Classification(DataClass.Internal)] public DateOnly Date { get; init; }
    [Classification(DataClass.Internal)] public Guid CalendarId { get; init; }
    [Classification(DataClass.Internal)] public required string Prefix { get; init; }
    [Classification(DataClass.Internal)] public int Number { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset IssuedAt { get; init; }

    /// <summary>"A-07".</summary>
    public static string Format(string prefix, int number) => string.Create(CultureInfo.InvariantCulture, $"{prefix}-{number:00}");

    /// <summary>The n-th prefix of the day (0 = "A", 25 = "Z", 26 = "AA" …): one per calendar seen at the facility that day.</summary>
    public static string PrefixAt(int index) => index < 26 ? ((char)('A' + index)).ToString() : PrefixAt(index / 26 - 1) + PrefixAt(index % 26);
}

/// <summary>
/// A waiting-room TV of one facility (MDV-P1-031). It signs in with a display token, never a staff session, and sees only the
/// queue TV of its facility. Only the token's hash is stored; revoking ends the display at its next request.
/// </summary>
public sealed class QueueDisplay
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>Where the screen is, e.g. "Waiting room TV". Free text by staff, never patient data.</summary>
    [Classification(DataClass.Internal)] public required string Name { get; init; }
    [Classification(DataClass.Internal)] public required byte[] TokenHash { get; init; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? RevokedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RevokedAt { get; set; }
}
