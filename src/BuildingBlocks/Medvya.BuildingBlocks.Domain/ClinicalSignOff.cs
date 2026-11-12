namespace Medvya.BuildingBlocks.Domain;

/// <summary>Who signed a clinical rule set (ranges, flag lines, drug rules) off: name, registration number and the day. Null on a rule set: a draft.</summary>
public sealed record ClinicalSignOff(
    [property: Classification(DataClass.Personal)] string Reviewer, [property: Classification(DataClass.Personal)] string Registration,
    [property: Classification(DataClass.Internal)] DateOnly SignedOn);
