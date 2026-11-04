using Medvya.BuildingBlocks.Domain;

namespace Medvya.Patients.Domain;

/// <summary>What a patient agrees to (MDV-P1-019). Recording is asked at each visit (P3), caregiver proxy in the portal (P4): neither is here.</summary>
public enum ConsentType { Treatment, Reminders, InformationExchange, Portal, Marketing }

/// <summary>The channel of a <see cref="ConsentType.Reminders"/> decision; every other type has none.</summary>
public enum ConsentChannel { WhatsApp, Sms }

/// <summary>
/// One decision, immutable (CONSENT_RECEIPT, a ledger table: SQL Server rejects UPDATE and DELETE). Changing a decision adds a new
/// receipt; the current decision is the newest per type and channel, and the older ones stay as history.
/// </summary>
public sealed class ConsentReceipt
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public ConsentType Type { get; init; }
    [Classification(DataClass.Internal)] public ConsentChannel? Channel { get; init; }
    /// <summary>The policy text version the patient was shown (ConsentPolicy.CurrentVersion when written).</summary>
    [Classification(DataClass.Internal)] public required string PolicyVersion { get; init; }
    /// <summary>"IN", "AE", or "AE-DXB" when the emirate (which selects the HIE) is known.</summary>
    [Classification(DataClass.Internal)] public required string Jurisdiction { get; init; }
    [Classification(DataClass.Internal)] public bool Granted { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset DecidedAt { get; init; }
    /// <summary>The staff member who recorded it on the patient's behalf.</summary>
    [Classification(DataClass.Internal)] public Guid? DecidedBy { get; init; }
}
