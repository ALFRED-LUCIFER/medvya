using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<OrderKind>))]
public enum OrderKind { Lab, Imaging }

/// <summary>
/// Draft: composed, not placed. Ordered: placed by the doctor on purpose (never by signing a note). NotIncluded: the doctor switched it off
/// before placing; it stays on the record and can be switched back while the consultation is open.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderState>))]
public enum OrderState { Draft, Ordered, NotIncluded }

/// <summary>
/// CLINICAL_ORDER (MDV-P2-018): one investigation ordered in one consultation. Results arrive in P4; until then the order is the record that
/// the test was asked for, and the slip is what the patient takes to the lab.
/// </summary>
public sealed class ClinicalOrder
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>Set while a patient merge holds this order on the surviving record: the record it came from.</summary>
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    /// <summary>Opaque id of the encounter in the Encounters module; an order belongs to exactly one.</summary>
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public OrderKind Kind { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string Name { get; init; } = "";
    /// <summary>Instructions for the lab or the patient, such as "fasting 10 h".</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Note { get; init; }
    [Classification(DataClass.Internal)] public OrderState State { get; set; }
    /// <summary>The lab or centre the doctor named when placing (free text in P2).</summary>
    [Classification(DataClass.Internal)] public string? Destination { get; set; }
    [Classification(DataClass.Internal)] public Guid CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? OrderedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? OrderedAt { get; set; }
}
