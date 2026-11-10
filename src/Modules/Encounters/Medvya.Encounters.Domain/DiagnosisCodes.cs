using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>One diagnosis code and its plain description.</summary>
public sealed record DiagnosisCode([property: Classification(DataClass.Internal)] string Code, [property: Classification(DataClass.Internal)] string Title);

/// <summary>A versioned code set a problem is coded from. The system and version are stored on every coded problem (MDV-P2-010).</summary>
public sealed record DiagnosisCodeSet(
    [property: Classification(DataClass.Internal)] string System, [property: Classification(DataClass.Internal)] string Version, [property: Classification(DataClass.Internal)] IReadOnlyList<DiagnosisCode> Codes)
{
    readonly Dictionary<string, DiagnosisCode> byCode = Codes.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The listed code, or null: a code outside the set is never accepted or guessed.</summary>
    public DiagnosisCode? Find(string code) => byCode.GetValueOrDefault(code.Trim());
}

/// <summary>
/// Which diagnosis code set a country uses (AGENTS.md 3.3): one subclass per country, no country checks elsewhere. The decision and its
/// open questions are recorded in ADR-017.
/// </summary>
public abstract class DiagnosisCodeRules
{
    public static readonly DiagnosisCodeRules India = new IndiaCodes();
    public static readonly DiagnosisCodeRules Uae = new UaeCodes();

    public static DiagnosisCodeRules For(string countryCode) => countryCode switch
    {
        "IN" => India,
        "AE" => Uae,
        _ => throw new ArgumentOutOfRangeException(nameof(countryCode), "Unsupported country."),
    };

    /// <summary>The set a doctor codes from in this country (a method: domain properties are stored fields and carry a data classification).</summary>
    public abstract DiagnosisCodeSet CodeSet();

    // India records WHO ICD-10 (the national standard for morbidity reporting).
    sealed class IndiaCodes : DiagnosisCodeRules
    {
        public override DiagnosisCodeSet CodeSet() => WhoStarter;
    }

    // shortcut: UAE claims (DHA, DOH) need ICD-10-CM, but claims start in P5; until a licensed ICD-10-CM set is loaded the UAE codes from the
    // same WHO ICD-10 starter list, and every problem records that system, so a later claim can tell. Upgrade with the P5 claims work (ADR-017).
    sealed class UaeCodes : DiagnosisCodeRules
    {
        public override DiagnosisCodeSet CodeSet() => WhoStarter;
    }

    // shortcut: a curated starter list of common primary-care codes, not the full classification (about 12,000 codes, WHO licence for
    // commercial use). Codes are identifiers; the titles are Medvya's own short wording, not WHO text. Replace with the licensed full set,
    // by a new version string, before a clinic needs a code that is not here (ADR-017). Until then a doctor leaves the code blank.
    static readonly DiagnosisCodeSet WhoStarter = new("ICD-10", "2019-medvya-starter-1",
    [
        new("A09.9", "Gastroenteritis and colitis, origin not specified"),
        new("A01.0", "Typhoid fever"),
        new("A16.2", "Pulmonary tuberculosis, not confirmed by tests"),
        new("B01.9", "Chickenpox (varicella) without complication"),
        new("B34.9", "Viral infection, not specified"),
        new("B35.4", "Ringworm of the body (tinea corporis)"),
        new("B54", "Malaria, not specified"),
        new("D50.9", "Iron deficiency anaemia, not specified"),
        new("D64.9", "Anaemia, not specified"),
        new("E03.9", "Hypothyroidism, not specified"),
        new("E05.9", "Hyperthyroidism (thyrotoxicosis), not specified"),
        new("E10.9", "Type 1 diabetes mellitus without complications"),
        new("E11.9", "Type 2 diabetes mellitus without complications"),
        new("E28.2", "Polycystic ovary syndrome (PCOS)"),
        new("E55.9", "Vitamin D deficiency, not specified"),
        new("E66.9", "Obesity, not specified"),
        new("E78.0", "High cholesterol (pure hypercholesterolaemia)"),
        new("E78.5", "Hyperlipidaemia, not specified"),
        new("E86", "Dehydration (volume depletion)"),
        new("F10.2", "Alcohol dependence"),
        new("F17.2", "Tobacco dependence"),
        new("F32.9", "Depressive episode, not specified"),
        new("F41.1", "Generalised anxiety disorder"),
        new("F41.9", "Anxiety disorder, not specified"),
        new("G40.9", "Epilepsy, not specified"),
        new("G43.9", "Migraine, not specified"),
        new("G44.2", "Tension-type headache"),
        new("G47.0", "Insomnia (difficulty starting or staying asleep)"),
        new("H10.9", "Conjunctivitis, not specified"),
        new("H25.9", "Age-related cataract, not specified"),
        new("H40.9", "Glaucoma, not specified"),
        new("H52.1", "Short sight (myopia)"),
        new("H61.2", "Impacted ear wax (cerumen)"),
        new("H66.9", "Middle ear infection (otitis media), not specified"),
        new("I10", "Essential (primary) hypertension, high blood pressure"),
        new("I20.9", "Angina, not specified"),
        new("I25.1", "Coronary artery disease (atherosclerotic heart disease)"),
        new("I48.9", "Atrial fibrillation or flutter, not specified"),
        new("I50.9", "Heart failure, not specified"),
        new("I63.9", "Stroke due to cerebral infarction, not specified"),
        new("I83.9", "Varicose veins of the legs, without ulcer or inflammation"),
        new("J00", "Common cold (acute nasopharyngitis)"),
        new("J02.9", "Sore throat (acute pharyngitis), not specified"),
        new("J03.9", "Acute tonsillitis, not specified"),
        new("J06.9", "Upper respiratory tract infection, acute, not specified"),
        new("J11.1", "Influenza (flu), virus not identified"),
        new("J18.9", "Pneumonia, not specified"),
        new("J20.9", "Acute bronchitis, not specified"),
        new("J30.4", "Allergic rhinitis (hay fever), not specified"),
        new("J32.9", "Chronic sinusitis, not specified"),
        new("J44.9", "Chronic obstructive pulmonary disease (COPD), not specified"),
        new("J45.9", "Asthma, not specified"),
        new("K21.9", "Acid reflux (GORD) without oesophagitis"),
        new("K29.7", "Gastritis, not specified"),
        new("K30", "Indigestion (functional dyspepsia)"),
        new("K59.0", "Constipation"),
        new("K76.0", "Fatty liver, not elsewhere classified"),
        new("K80.2", "Gallstones without cholecystitis"),
        new("L20.9", "Atopic dermatitis (eczema), not specified"),
        new("L30.9", "Dermatitis, not specified"),
        new("L50.9", "Urticaria (hives), not specified"),
        new("L70.0", "Acne vulgaris"),
        new("M06.9", "Rheumatoid arthritis, not specified"),
        new("M10.9", "Gout, not specified"),
        new("M17.9", "Knee osteoarthritis, not specified"),
        new("M25.5", "Joint pain"),
        new("M54.2", "Neck pain (cervicalgia)"),
        new("M54.5", "Low back pain"),
        new("M79.1", "Muscle pain (myalgia)"),
        new("M81.9", "Osteoporosis, not specified"),
        new("N18.9", "Chronic kidney disease, not specified"),
        new("N20.0", "Kidney stone (calculus of kidney)"),
        new("N39.0", "Urinary tract infection, site not specified"),
        new("N40", "Enlarged prostate (prostatic hyperplasia)"),
        new("N94.6", "Painful periods (dysmenorrhoea), not specified"),
        new("O24.4", "Diabetes arising in pregnancy (gestational diabetes)"),
        new("R05", "Cough"),
        new("R07.4", "Chest pain, not specified"),
        new("R10.4", "Abdominal pain, other and not specified"),
        new("R11", "Nausea and vomiting"),
        new("R42", "Dizziness and giddiness"),
        new("R50.9", "Fever, not specified"),
        new("R51", "Headache"),
        new("R53", "Tiredness (malaise and fatigue)"),
        new("R73.0", "Prediabetes (abnormal glucose tolerance)"),
        new("S93.4", "Ankle sprain or strain"),
        new("T78.4", "Allergy, not specified"),
        new("U07.1", "COVID-19, virus identified"),
        new("Z00.0", "General medical examination"),
        new("Z34.9", "Supervision of normal pregnancy, not specified"),
        new("Z72.0", "Tobacco use"),
    ]);
}
