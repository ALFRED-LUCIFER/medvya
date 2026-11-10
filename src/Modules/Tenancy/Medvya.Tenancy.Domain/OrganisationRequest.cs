using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>The two organisation types Medvya onboards (ADR-016). A Hospital is a Clinic plus extra documents.</summary>
public enum OrganisationType { Clinic, Hospital }

/// <summary>
/// The onboarding request of an organisation (ADR-016, MDV-P1-062): its type, the hospital's IT contact, and the last reason Medvya gave.
/// One per organisation; the state itself is <see cref="Tenant.State"/>. Contains no patient data.
/// </summary>
public sealed class OrganisationRequest
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public OrganisationType Type { get; set; }
    /// <summary>Hospital only: the person Medvya talks to about security. Required to submit a Hospital request.</summary>
    [Classification(DataClass.Personal)] public string? ItContactName { get; set; }
    [Classification(DataClass.Personal)] public string? ItContactEmail { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? SubmittedAt { get; set; }
    /// <summary>The reason of the last "ask for changes", rejection or suspension, shown to the applicant. Plain words, never personal details.</summary>
    [Classification(DataClass.Internal)] public string? LastReason { get; set; }
}

/// <summary>One uploaded document of the request. The file sits in private Blob under <see cref="BlobKey"/>; the original file name is not kept.</summary>
public sealed class OrganisationDocument
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>One of <see cref="OrganisationDocumentRules.Kinds"/>; a new upload of the same kind replaces the old one.</summary>
    [Classification(DataClass.Internal)] public required string Kind { get; set; }
    [Classification(DataClass.Internal)] public required string ContentType { get; set; }
    [Classification(DataClass.Internal)] public long SizeBytes { get; set; }
    [Classification(DataClass.Internal)] public required string BlobKey { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UploadedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid UploadedBy { get; set; }
}

/// <summary>Which documents each type must give (ADR-016 table) and what a file may be. Counsel confirms the lists per country (MDV-P1-044).</summary>
public static class OrganisationDocumentRules
{
    public const string RegistrationCertificate = "RegistrationCertificate", HospitalRegistration = "HospitalRegistration", TaxRegistration = "TaxRegistration",
        OwnerId = "OwnerId", MedicalCouncilRegistration = "MedicalCouncilRegistration", SignatoryLetter = "SignatoryLetter", Dpa = "Dpa";

    public static readonly IReadOnlyList<string> Kinds =
        [RegistrationCertificate, HospitalRegistration, TaxRegistration, OwnerId, MedicalCouncilRegistration, SignatoryLetter, Dpa];

    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// The kinds that must be present to submit. Hospital: its own registration instead of the clinic's, the signatory letter and the signed DPA;
    /// the owner's ID is the signatory's. The council registration is asked only when the owner is a doctor and only of a Clinic.
    /// </summary>
    public static IReadOnlyList<string> Required(OrganisationType type, bool ownerIsDoctor) => type == OrganisationType.Hospital
        ? [HospitalRegistration, TaxRegistration, OwnerId, SignatoryLetter, Dpa]
        : ownerIsDoctor ? [RegistrationCertificate, TaxRegistration, OwnerId, MedicalCouncilRegistration] : [RegistrationCertificate, TaxRegistration, OwnerId];

    static readonly byte[] Pdf = "%PDF-"u8.ToArray(), Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Jpeg = [0xFF, 0xD8, 0xFF];

    /// <summary>The type of a file from its bytes (never the name or the client's header): PDF, PNG or JPEG; null for anything else.</summary>
    public static string? ContentTypeOf(ReadOnlySpan<byte> content) =>
        content.StartsWith(Pdf) ? "application/pdf" : content.StartsWith(Png) ? "image/png" : content.StartsWith(Jpeg) ? "image/jpeg" : null;
}
