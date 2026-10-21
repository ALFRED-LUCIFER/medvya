namespace Medvya.BuildingBlocks.Domain;

/// <summary>Sensitivity class of a stored field (PRD v2 P0-10). Drives masking, encryption, audit and log redaction.</summary>
public enum DataClass
{
    Public,
    Internal,
    Personal,
    SensitiveHealth,
    NationalId,
}

/// <summary>Required on every public property of a domain type; see docs/03-architecture/data-classification.md.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ClassificationAttribute(DataClass value) : Attribute
{
    public DataClass Value { get; } = value;
}
