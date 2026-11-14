using System.Security.Cryptography;
using System.Text;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>How far Medvya has released a feature in one country (MDV-P5-041). Each stage reaches everyone the one before it reached.</summary>
public enum ReleaseStage { Off, Internal, Beta, Percentage, Everyone }

/// <summary>
/// One feature Medvya can switch (MDV-P5-041). <see cref="Clinical"/>: reaching everyone needs a recorded clinical sign-off. <see cref="ClinicSwitch"/>: the
/// practice owner also turns it on (a <see cref="FeatureFlag"/> row); without one, Medvya's release alone decides. <see cref="AlwaysOn"/>: locked on, never
/// switched (core records). <see cref="Initial"/>: the stage while Medvya has recorded none for a country.
/// </summary>
public sealed record Feature(
    [property: Classification(DataClass.Internal)] string Key, [property: Classification(DataClass.Internal)] bool Clinical,
    [property: Classification(DataClass.Internal)] bool ClinicSwitch, [property: Classification(DataClass.Internal)] bool AlwaysOn,
    [property: Classification(DataClass.Internal)] ReleaseStage Initial, [property: Classification(DataClass.Internal)] IReadOnlyList<string> Countries);

/// <summary>The one list of features (MDV-P5-041). A gate, a clinic switch or a release can only name a feature listed here.</summary>
public static class Features
{
    static readonly string[] Both = ["IN", "AE"];

    /// <summary>The nurse step before the doctor (MDV-P2-007): intake endpoints exist, and a visit leaves the nurse only by the nurse's handoff.</summary>
    public static readonly Feature NurseIntake = new("clinical.nurse-intake", Clinical: true, ClinicSwitch: true, AlwaysOn: false, ReleaseStage.Everyone, Both);

    /// <summary>Billing as a visit step (P4): payment on the board's Billing cards.</summary>
    public static readonly Feature Billing = new("billing.invoices", Clinical: false, ClinicSwitch: true, AlwaysOn: false, ReleaseStage.Everyone, Both);

    // Keys that existed before Medvya's release switch start at Everyone, so nothing disappears for a clinic; a new key starts Off.
    // Always on: the core of the record, shown locked in the console. Nothing gates them; they are listed so they can never be switched off.
    public static readonly IReadOnlyList<Feature> All =
    [
        NurseIntake, Billing,
        Locked("patients.record", clinical: true), Locked("clinical.allergies", clinical: true), Locked("prescribing.signing", clinical: true),
        Locked("audit.log", clinical: false), Locked("data.export", clinical: false),
    ];

    public static Feature? Find(string key) => All.FirstOrDefault(f => f.Key == key);

    static Feature Locked(string key, bool clinical) => new(key, clinical, ClinicSwitch: false, AlwaysOn: true, ReleaseStage.Everyone, Both);
}

/// <summary>
/// Medvya's release of one feature in one country (MDV-P5-041). Not a tenant row: each country's database holds its own, written only by the platform
/// console's narrow login (medvya_features) and read by the clinic application. No row: the feature's <see cref="Feature.Initial"/> stage.
/// </summary>
public sealed class FeatureRelease
{
    public const int SignOffMax = 500, OrganisationsMax = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    [Classification(DataClass.Internal)] public required string Country { get; init; }
    [Classification(DataClass.Internal)] public required string Key { get; init; }
    [Classification(DataClass.Internal)] public ReleaseStage Stage { get; set; }
    /// <summary>At <see cref="ReleaseStage.Percentage"/>: the share of organisations reached, 1 to 99.</summary>
    [Classification(DataClass.Internal)] public int Percentage { get; set; }
    /// <summary>Named organisations (tenant ids) reached from <see cref="ReleaseStage.Beta"/> on.</summary>
    [Classification(DataClass.Internal)] public Guid[] Organisations { get; set; } = [];
    /// <summary>The clinical sign-off a clinical feature needs before it reaches everyone: who signed and the reference, as the operator recorded it.</summary>
    [Classification(DataClass.Personal)] public string? ClinicalSignOff { get; set; }
    /// <summary>The console member (platform member id) who recorded the sign-off.</summary>
    [Classification(DataClass.Internal)] public Guid? SignOffByMemberId { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? SignOffAt { get; set; }
    /// <summary>The console member (platform member id) who made the last change; the reason is in the platform audit log.</summary>
    [Classification(DataClass.Internal)] public Guid ChangedByMemberId { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }

    /// <summary>
    /// Whether Medvya's release reaches this organisation. <paramref name="internalOrganisations"/>: Medvya's own test organisations in this country.
    /// The percentage bucket is fixed per feature and organisation, so raising the share only adds organisations.
    /// </summary>
    public static bool Reaches(Feature feature, FeatureRelease? release, Guid tenantId, string country, IReadOnlySet<Guid> internalOrganisations)
    {
        if (feature.AlwaysOn) return true;
        if (!feature.Countries.Contains(country)) return false;
        var stage = release?.Stage ?? feature.Initial;
        return stage switch
        {
            ReleaseStage.Everyone => true,
            ReleaseStage.Off => false,
            _ when internalOrganisations.Contains(tenantId) => true,
            ReleaseStage.Internal => false,
            _ when release?.Organisations.Contains(tenantId) == true => true,
            ReleaseStage.Beta => false,
            _ => Bucket(feature.Key, tenantId) < (release?.Percentage ?? 0),
        };
    }

    static int Bucket(string key, Guid tenantId) =>
        (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{tenantId:N}")), 0) % 100);
}
