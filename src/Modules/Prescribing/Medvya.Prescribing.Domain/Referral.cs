using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ReferralDirection>))]
public enum ReferralDirection { Out, In }

[JsonConverter(typeof(JsonStringEnumConverter<ReferralUrgency>))]
public enum ReferralUrgency { Routine, Urgent }

/// <summary>How the letter reaches the other provider. P2 delivery is manual: the channel is what the doctor did, recorded with the time.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferralChannel>))]
public enum ReferralChannel { Print, Email, PatientCarries }

/// <summary>What a letter may carry from the chart. Only the sections the doctor selects are in the letter.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferralSection>))]
public enum ReferralSection { Allergies, Problems, Medicines }

/// <summary>Out: Draft, Sent, AppointmentBooked, ReportReceived, Closed. In: Received, Closed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferralState>))]
public enum ReferralState { Draft, Sent, AppointmentBooked, ReportReceived, Closed, Received }

/// <summary>
/// REFERRAL (MDV-P2-019): a patient sent to another provider (Out, from a consultation) or a letter received from one (In, filed with its
/// document). Open until Closed. Delivery is manual in P2; the record says how and when.
/// </summary>
public sealed class Referral
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    /// <summary>The consultation a referral out was written in; none for one received.</summary>
    [Classification(DataClass.Internal)] public Guid? EncounterId { get; init; }
    [Classification(DataClass.Internal)] public ReferralDirection Direction { get; init; }
    /// <summary>The doctor or facility referred to (Out) or received from (In), as free text in P2.</summary>
    [Classification(DataClass.SensitiveHealth)] public string Provider { get; init; } = "";
    [Classification(DataClass.Internal)] public ReferralUrgency Urgency { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Reason { get; init; }
    /// <summary>Comma-separated <see cref="ReferralSection"/> names the letter may carry.</summary>
    [Classification(DataClass.Internal)] public string Sections { get; init; } = "";
    [Classification(DataClass.Internal)] public ReferralState State { get; set; }
    [Classification(DataClass.Internal)] public ReferralChannel? Channel { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? SentAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? SentBy { get; set; }
    /// <summary>When the patient's sharing consent was decided, for a channel that needed it: the receipt the send relied on.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset? ConsentDecidedAt { get; set; }
    /// <summary>The filed letter of a referral received.</summary>
    [Classification(DataClass.Internal)] public Guid? LetterDocumentId { get; init; }
    [Classification(DataClass.Internal)] public Guid CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One step of a referral's status, with its evidence (a document the report came in) and a short note. Append-only.</summary>
public sealed class ReferralEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid ReferralId { get; init; }
    [Classification(DataClass.Internal)] public ReferralState State { get; init; }
    [Classification(DataClass.Internal)] public Guid? DocumentId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Note { get; init; }
    [Classification(DataClass.Internal)] public Guid By { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset At { get; init; }
}

/// <summary>
/// The rules of a referral. Which moves exist, and when the patient's consent to share is needed.
/// shortcut: one policy for both countries (an emailed letter needs the patient's information-exchange consent; a printed one or one the patient carries does not,
/// because the clinic discloses nothing); country wording and exceptions come with the legal review (P1-044).
/// </summary>
public static class ReferralRules
{
    static readonly Dictionary<ReferralState, ReferralState[]> Moves = new()
    {
        [ReferralState.Draft] = [ReferralState.Sent, ReferralState.Closed], // a draft the doctor does not send is closed, not deleted
        [ReferralState.Sent] = [ReferralState.AppointmentBooked, ReferralState.ReportReceived, ReferralState.Closed],
        [ReferralState.AppointmentBooked] = [ReferralState.ReportReceived, ReferralState.Closed],
        [ReferralState.ReportReceived] = [ReferralState.Closed],
        [ReferralState.Received] = [ReferralState.Closed],
    };

    public static bool CanMove(ReferralState from, ReferralState to) => Moves.TryGetValue(from, out var next) && next.Contains(to);

    public static bool NeedsConsent(ReferralChannel channel) => channel == ReferralChannel.Email;

    public static bool IsOpen(ReferralState state) => state != ReferralState.Closed;
}
