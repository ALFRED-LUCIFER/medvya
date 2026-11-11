using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<PaperKind>))]
public enum PaperKind { InvestigationSlip, ReferralLetter }

/// <summary>
/// A server-made investigation slip (MDV-P2-018) or referral letter (MDV-P2-019) handed out as a PDF: its QR code's random half, the SHA-256 of the
/// content it printed and of the PDF bytes, and the copy written once to Blob. Append-only and hash-chained: the record of each paper given out.
/// No patient id of its own (merges move patients, and this row is never edited): the patient is the source's, the encounter or the referral.
/// </summary>
public sealed class PrintedPaper
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public PaperKind Kind { get; init; }
    /// <summary>The encounter (slip) or the referral (letter).</summary>
    [Classification(DataClass.Internal)] public Guid SourceId { get; init; }
    /// <summary>The doctor the paper names and whose signature it carries.</summary>
    [Classification(DataClass.Internal)] public Guid DoctorId { get; init; }
    /// <summary>How many tests or letter sections it lists: what the verify page may say, never which.</summary>
    [Classification(DataClass.Internal)] public int Items { get; init; }
    [Classification(DataClass.Internal)] public required string VerifyCode { get; init; }
    [Classification(DataClass.Internal)] public required string ContentHash { get; init; }
    [Classification(DataClass.Internal)] public required string Sha256 { get; init; }
    [Classification(DataClass.Internal)] public required string BlobKey { get; init; }
    [Classification(DataClass.Internal)] public Guid RenderedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RenderedAt { get; init; }
}
