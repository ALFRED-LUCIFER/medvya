using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<DocumentType>))]
public enum DocumentType { LabReport, Imaging, Prescription, Referral, Other }

/// <summary>Pending: not scanned yet, so not viewable. Clean: the scan passed. Infected: quarantined, never viewable.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScanStatus>))]
public enum ScanStatus { Pending, Clean, Infected }

/// <summary>
/// CLINICAL_DOCUMENT (MDV-P2-006): a scanned or uploaded file in the patient's chart. The file itself lives in private Blob under a random
/// key and is never changed or replaced; only this row's type and date can be edited (audited). Nothing here is the file's name, which is
/// not kept: names are often a patient's name.
/// </summary>
public sealed class ClinicalDocument
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DocumentType Type { get; set; }
    /// <summary>The date on the document itself.</summary>
    [Classification(DataClass.SensitiveHealth)] public DateOnly DocumentDate { get; set; }
    /// <summary>Brought by the patient (PatientReported) or from another provider (Imported); the timeline shows it as its source tag.</summary>
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    /// <summary>From the file's own bytes (pdf, jpeg or png), never from the client's header or file name.</summary>
    [Classification(DataClass.Internal)] public required string ContentType { get; init; }
    [Classification(DataClass.Internal)] public long SizeBytes { get; init; }
    /// <summary>SHA-256 of the original file, hex: shows the original is unchanged.</summary>
    [Classification(DataClass.Internal)] public required string Sha256 { get; init; }
    /// <summary>Random, unrelated to the patient or the file name; no PHI in a blob name.</summary>
    [Classification(DataClass.Internal)] public required string BlobKey { get; init; }
    [Classification(DataClass.Internal)] public ScanStatus ScanStatus { get; set; }
    [Classification(DataClass.Internal)] public Guid UploadedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset UploadedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ChangedAt { get; set; }
}
