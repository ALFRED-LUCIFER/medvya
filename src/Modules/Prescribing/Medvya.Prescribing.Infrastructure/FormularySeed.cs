using System.Security.Cryptography;
using System.Text;
using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The starter list (ADR-015): common generic (INN) names with usual strengths and forms, ATC codes from the WHO ATC index, no brands and no
/// national codes. NOT authoritative: every class below is a placeholder that a clinical or regulatory advisor must confirm before a clinic
/// relies on it, and a licensed national register (CDSCO / NLEM for India, the UAE drug registers) replaces it.
/// shortcut: 22 medicines per country; load the licensed register by a new migration when the licence is signed.
/// </summary>
public static class FormularySeed
{
    public sealed record Row(string Country, string Generic, string Strength, string Form, RegulatoryClass RegClass, string Atc, string? DefaultSig);

    const RegulatoryClass N = RegulatoryClass.None, H = RegulatoryClass.ScheduleH, H1 = RegulatoryClass.ScheduleH1, X = RegulatoryClass.ScheduleX, C = RegulatoryClass.Controlled;

    // Each medicine with its class in India and in the UAE: the two countries' rules differ, so the class is data here, never an if on the country.
    static readonly (string Generic, string Strength, string Form, RegulatoryClass India, RegulatoryClass Uae, string Atc, string? Sig)[] Medicines =
    [
        ("Paracetamol", "500 mg", "Tablet", N, N, "N02BE01", "1 tablet every 6 hours if needed"),
        ("Ibuprofen", "400 mg", "Tablet", N, N, "M01AE01", "1 tablet after food, twice a day"),
        ("Cetirizine", "10 mg", "Tablet", N, N, "R06AX27", "1 tablet at night"),
        ("Salbutamol", "100 mcg/dose", "Inhaler", N, N, "R03AC02", "1-2 puffs when needed"),
        ("Amoxicillin", "500 mg", "Capsule", H, N, "J01CA04", "1 capsule three times a day"),
        ("Azithromycin", "500 mg", "Tablet", H, N, "J01FA10", "1 tablet once a day"),
        ("Ciprofloxacin", "500 mg", "Tablet", H1, N, "J01MA02", "1 tablet twice a day"),
        ("Metformin", "500 mg", "Tablet", H, N, "A10BA02", "1 tablet twice a day with meals"),
        ("Gliclazide", "80 mg", "Tablet", H, N, "A10BB09", "1 tablet before breakfast"),
        ("Atorvastatin", "10 mg", "Tablet", H, N, "C10AA05", "1 tablet at night"),
        ("Atorvastatin", "20 mg", "Tablet", H, N, "C10AA05", "1 tablet at night"),
        ("Amlodipine", "5 mg", "Tablet", H, N, "C08CA01", "1 tablet once a day"),
        ("Losartan", "50 mg", "Tablet", H, N, "C09CA01", "1 tablet once a day"),
        ("Metoprolol", "50 mg", "Tablet", H, N, "C07AB02", "1 tablet twice a day"),
        ("Aspirin", "75 mg", "Tablet", N, N, "B01AC06", "1 tablet after lunch"),
        ("Clopidogrel", "75 mg", "Tablet", H, N, "B01AC04", "1 tablet once a day"),
        ("Omeprazole", "20 mg", "Capsule", H, N, "A02BC01", "1 capsule before breakfast"),
        ("Pantoprazole", "40 mg", "Tablet", H, N, "A02BC02", "1 tablet before breakfast"),
        ("Levothyroxine", "50 mcg", "Tablet", H, N, "H03AA01", "1 tablet on an empty stomach in the morning"),
        ("Tramadol", "50 mg", "Capsule", H1, C, "N02AX02", "1 capsule when needed, up to three times a day"),
        ("Alprazolam", "0.25 mg", "Tablet", H1, C, "N05BA12", "1 tablet at night"),
        ("Methylphenidate", "10 mg", "Tablet", X, C, "N06BA04", null),
    ];

    public static IReadOnlyList<Row> Rows { get; } =
    [
        .. Medicines.Select(m => new Row("IN", m.Generic, m.Strength, m.Form, m.India, m.Atc, m.Sig)),
        .. Medicines.Select(m => new Row("AE", m.Generic, m.Strength, m.Form, m.Uae, m.Atc, m.Sig)),
    ];

    /// <summary>The same row always has the same id, so tests can name a row.</summary>
    public static Guid IdOf(Row r) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"formulary|{r.Country}|{r.Generic}|{r.Strength}|{r.Form}"))[..16]);
}
