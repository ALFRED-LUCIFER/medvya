using Medvya.BuildingBlocks.Domain;

namespace Medvya.Identity.Domain;

/// <summary>
/// A person's unfinished sign-up wizard (F-04, MDV-P1-004). It belongs to the person, not a clinic: no tenant exists until they
/// finish. Kept here, not in Tenancy, because it holds personal details no reporting role may read. When the practice is created
/// the data is emptied and <see cref="CompletedAt"/> set (this module never deletes rows); starting again reuses the row.
/// </summary>
public sealed class OnboardingDraft
{
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    /// <summary>"IN" or "AE": the country deployment the practice will be bound to.</summary>
    [Classification(DataClass.Internal)] public required string Country { get; set; }
    /// <summary>The step to resume at (1-5).</summary>
    [Classification(DataClass.Internal)] public int Step { get; set; } = 1;
    /// <summary>JSON of what the person typed so far (names, licence and clinic details).</summary>
    [Classification(DataClass.Personal)] public required string Data { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? CompletedAt { get; set; }
}
