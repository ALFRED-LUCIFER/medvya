using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure;

/// <summary>One row of a starter list: the clinic's code, the name, the category, the usual length, the clinic default price (minor units) and the tax treatment.</summary>
public sealed record StarterService(string Code, string Name, ServiceCategory Category, int DurationMinutes, long PriceMinor, TaxTreatment Tax);

/// <summary>
/// The dental services and fees starter list for India (MDV-P8-010): 76 dental procedures with a usual length and an editable INR default price, loaded into
/// the catalogue as ordinary services the owner then edits and prices. Clinical dental care is GST-exempt; cosmetic items (whitening, veneers, smile design)
/// carry GST at 18 %. Every row is SAC 999312 ("medical and dental services").
/// Source: the India procedure catalogue of DentalERP (github.com/abinauv/dental-erp, prisma/seed-data/IN/procedures.ts), MIT licence,
/// copyright (c) 2026 Abinauv Selvaraj; names, lengths and prices as published there, codes prefixed "DN-".
/// shortcut: a dentist has not reviewed the names and default fees and a chartered accountant has not confirmed the GST treatment of each row (MDV-P8-010,
/// MDV-P4-031); the screen says the list is a starting point. A UAE list (VAT and payer codes) waits for the licence check of a UAE dental code set.
/// </summary>
public static class DentalStarterList
{
    public const string Sac = "999312";
    public const int CosmeticGstBasisPoints = 1800;
    public const string Currency = "INR";

    public static readonly IReadOnlyList<StarterService> India =
    [
        new("DN-DGN001", "Dental consultation", ServiceCategory.Consultation, 15, 30000, TaxTreatment.Exempt),
        new("DN-DGN002", "Comprehensive Oral Examination", ServiceCategory.Procedure, 30, 50000, TaxTreatment.Exempt),
        new("DN-DGN003", "Single X-Ray (IOPA)", ServiceCategory.Test, 5, 20000, TaxTreatment.Exempt),
        new("DN-DGN004", "Panoramic X-Ray (OPG)", ServiceCategory.Test, 10, 50000, TaxTreatment.Exempt),
        new("DN-DGN005", "Bitewing X-Rays", ServiceCategory.Test, 10, 40000, TaxTreatment.Exempt),
        new("DN-DGN006", "CBCT Scan", ServiceCategory.Procedure, 20, 300000, TaxTreatment.Exempt),
        new("DN-PRV001", "Dental Cleaning (Scaling)", ServiceCategory.Procedure, 30, 150000, TaxTreatment.Exempt),
        new("DN-PRV002", "Deep Cleaning (Scaling & Root Planing)", ServiceCategory.Procedure, 60, 300000, TaxTreatment.Exempt),
        new("DN-PRV003", "Fluoride Treatment", ServiceCategory.Procedure, 15, 50000, TaxTreatment.Exempt),
        new("DN-PRV004", "Pit and Fissure Sealant", ServiceCategory.Procedure, 20, 80000, TaxTreatment.Exempt),
        new("DN-PRV005", "Sports Mouthguard", ServiceCategory.Procedure, 30, 250000, TaxTreatment.Exempt),
        new("DN-RST001", "Composite Filling (Anterior)", ServiceCategory.Procedure, 30, 150000, TaxTreatment.Exempt),
        new("DN-RST002", "Composite Filling (Posterior)", ServiceCategory.Procedure, 45, 200000, TaxTreatment.Exempt),
        new("DN-RST003", "Amalgam Filling", ServiceCategory.Procedure, 30, 100000, TaxTreatment.Exempt),
        new("DN-RST004", "Glass Ionomer Filling", ServiceCategory.Procedure, 30, 120000, TaxTreatment.Exempt),
        new("DN-RST005", "Core Build-up", ServiceCategory.Procedure, 30, 200000, TaxTreatment.Exempt),
        new("DN-RST006", "Inlay (Ceramic)", ServiceCategory.Procedure, 60, 800000, TaxTreatment.Exempt),
        new("DN-RST007", "Onlay (Ceramic)", ServiceCategory.Procedure, 60, 1000000, TaxTreatment.Exempt),
        new("DN-END001", "Root Canal Treatment (Anterior)", ServiceCategory.Procedure, 45, 500000, TaxTreatment.Exempt),
        new("DN-END002", "Root Canal Treatment (Premolar)", ServiceCategory.Procedure, 60, 700000, TaxTreatment.Exempt),
        new("DN-END003", "Root Canal Treatment (Molar)", ServiceCategory.Procedure, 90, 1000000, TaxTreatment.Exempt),
        new("DN-END004", "Re-Root Canal Treatment", ServiceCategory.Procedure, 90, 1200000, TaxTreatment.Exempt),
        new("DN-END005", "Pulp Capping", ServiceCategory.Procedure, 30, 150000, TaxTreatment.Exempt),
        new("DN-END006", "Pulpotomy", ServiceCategory.Procedure, 30, 200000, TaxTreatment.Exempt),
        new("DN-END007", "Apicoectomy", ServiceCategory.Procedure, 60, 800000, TaxTreatment.Exempt),
        new("DN-PER001", "Periodontal Scaling", ServiceCategory.Procedure, 45, 300000, TaxTreatment.Exempt),
        new("DN-PER002", "Curettage", ServiceCategory.Procedure, 30, 200000, TaxTreatment.Exempt),
        new("DN-PER003", "Flap Surgery", ServiceCategory.Procedure, 90, 800000, TaxTreatment.Exempt),
        new("DN-PER004", "Bone Grafting", ServiceCategory.Procedure, 60, 1500000, TaxTreatment.Exempt),
        new("DN-PER005", "Gum Grafting", ServiceCategory.Procedure, 60, 1000000, TaxTreatment.Exempt),
        new("DN-PER006", "Crown Lengthening", ServiceCategory.Procedure, 45, 600000, TaxTreatment.Exempt),
        new("DN-PER007", "Gingivectomy", ServiceCategory.Procedure, 30, 400000, TaxTreatment.Exempt),
        new("DN-PRS001", "Metal Crown", ServiceCategory.Procedure, 45, 500000, TaxTreatment.Exempt),
        new("DN-PRS002", "PFM Crown", ServiceCategory.Procedure, 45, 800000, TaxTreatment.Exempt),
        new("DN-PRS003", "Ceramic Crown", ServiceCategory.Procedure, 45, 1200000, TaxTreatment.Exempt),
        new("DN-PRS004", "Zirconia Crown", ServiceCategory.Procedure, 45, 1500000, TaxTreatment.Exempt),
        new("DN-PRS005", "Dental Bridge (per unit)", ServiceCategory.Procedure, 60, 1000000, TaxTreatment.Exempt),
        new("DN-PRS006", "Complete Denture (Upper)", ServiceCategory.Procedure, 60, 1500000, TaxTreatment.Exempt),
        new("DN-PRS007", "Complete Denture (Lower)", ServiceCategory.Procedure, 60, 1500000, TaxTreatment.Exempt),
        new("DN-PRS008", "Partial Denture (Acrylic)", ServiceCategory.Procedure, 45, 800000, TaxTreatment.Exempt),
        new("DN-PRS009", "Partial Denture (Cast Metal)", ServiceCategory.Procedure, 60, 2000000, TaxTreatment.Exempt),
        new("DN-PRS010", "Flexible Denture", ServiceCategory.Procedure, 45, 1500000, TaxTreatment.Exempt),
        new("DN-PRS011", "Dental Implant", ServiceCategory.Procedure, 90, 3500000, TaxTreatment.Exempt),
        new("DN-PRS012", "Implant Crown", ServiceCategory.Procedure, 45, 2000000, TaxTreatment.Exempt),
        new("DN-PRS013", "Implant Abutment", ServiceCategory.Procedure, 30, 800000, TaxTreatment.Exempt),
        new("DN-ORT001", "Orthodontic Consultation", ServiceCategory.Procedure, 30, 50000, TaxTreatment.Exempt),
        new("DN-ORT002", "Metal Braces (Full)", ServiceCategory.Procedure, 60, 4500000, TaxTreatment.Exempt),
        new("DN-ORT003", "Ceramic Braces (Full)", ServiceCategory.Procedure, 60, 6000000, TaxTreatment.Exempt),
        new("DN-ORT004", "Lingual Braces", ServiceCategory.Procedure, 90, 12000000, TaxTreatment.Exempt),
        new("DN-ORT005", "Clear Aligners", ServiceCategory.Procedure, 60, 15000000, TaxTreatment.Exempt),
        new("DN-ORT006", "Orthodontic Adjustment", ServiceCategory.Procedure, 20, 100000, TaxTreatment.Exempt),
        new("DN-ORT007", "Retainer (Removable)", ServiceCategory.Procedure, 30, 500000, TaxTreatment.Exempt),
        new("DN-ORT008", "Retainer (Fixed)", ServiceCategory.Procedure, 30, 400000, TaxTreatment.Exempt),
        new("DN-ORT009", "Space Maintainer", ServiceCategory.Procedure, 30, 300000, TaxTreatment.Exempt),
        new("DN-SRG001", "Simple Extraction", ServiceCategory.Procedure, 30, 100000, TaxTreatment.Exempt),
        new("DN-SRG002", "Surgical Extraction", ServiceCategory.Procedure, 45, 300000, TaxTreatment.Exempt),
        new("DN-SRG003", "Wisdom Tooth Extraction (Simple)", ServiceCategory.Procedure, 45, 300000, TaxTreatment.Exempt),
        new("DN-SRG004", "Wisdom Tooth Extraction (Impacted)", ServiceCategory.Procedure, 60, 800000, TaxTreatment.Exempt),
        new("DN-SRG005", "Incision and Drainage", ServiceCategory.Procedure, 30, 200000, TaxTreatment.Exempt),
        new("DN-SRG006", "Frenectomy", ServiceCategory.Procedure, 30, 500000, TaxTreatment.Exempt),
        new("DN-SRG007", "Biopsy", ServiceCategory.Procedure, 30, 300000, TaxTreatment.Exempt),
        new("DN-SRG008", "Cyst Removal", ServiceCategory.Procedure, 60, 1000000, TaxTreatment.Exempt),
        new("DN-COS001", "Teeth Whitening (In-Office)", ServiceCategory.Procedure, 60, 800000, TaxTreatment.Taxable),
        new("DN-COS002", "Teeth Whitening (Take-Home)", ServiceCategory.Procedure, 30, 500000, TaxTreatment.Taxable),
        new("DN-COS003", "Porcelain Veneer", ServiceCategory.Procedure, 45, 1500000, TaxTreatment.Taxable),
        new("DN-COS004", "Composite Veneer", ServiceCategory.Procedure, 30, 500000, TaxTreatment.Taxable),
        new("DN-COS005", "Tooth Reshaping", ServiceCategory.Procedure, 20, 150000, TaxTreatment.Taxable),
        new("DN-COS006", "Dental Bonding", ServiceCategory.Procedure, 30, 300000, TaxTreatment.Taxable),
        new("DN-COS007", "Smile Makeover Consultation", ServiceCategory.Procedure, 45, 100000, TaxTreatment.Taxable),
        new("DN-COS008", "Gum Contouring", ServiceCategory.Procedure, 45, 600000, TaxTreatment.Taxable),
        new("DN-EMR001", "Emergency Consultation", ServiceCategory.Procedure, 30, 50000, TaxTreatment.Exempt),
        new("DN-EMR002", "Pain Relief Treatment", ServiceCategory.Procedure, 30, 100000, TaxTreatment.Exempt),
        new("DN-EMR003", "Temporary Filling", ServiceCategory.Procedure, 20, 50000, TaxTreatment.Exempt),
        new("DN-EMR004", "Re-cementation of Crown", ServiceCategory.Procedure, 15, 50000, TaxTreatment.Exempt),
        new("DN-EMR005", "Tooth Reimplantation", ServiceCategory.Procedure, 45, 300000, TaxTreatment.Exempt),
        new("DN-EMR006", "Broken Tooth Repair", ServiceCategory.Procedure, 30, 200000, TaxTreatment.Exempt),
    ];
}

/// <summary>Loading a starter list into the catalogue (MDV-P8-010).</summary>
public sealed partial class Catalogue
{
    public const string StarterAction = "billing.service.dental_starter";

    /// <summary>
    /// Adds every row of the dental starter list whose code the clinic does not use yet (archived services count, so a removed row is never brought
    /// back), each with its clinic default price from now. All rows and prices are saved in one transaction with one audit event (all or nothing), and
    /// each row passes the same checks as a service typed in Settings. Idempotent: a second load adds nothing and is still audited.
    /// </summary>
    /// <exception cref="BillingValidationException">"country:unsupported" when the clinic does not bill in rupees.</exception>
    public async Task<StarterLoadResult> LoadDentalStarterAsync(CancellationToken cancellationToken = default)
    {
        var currency = await CurrencyAsync(cancellationToken);
        if (currency != DentalStarterList.Currency) throw new BillingValidationException([("country", "unsupported")]);
        var taken = (await db.Services.AsNoTracking().Where(s => s.Code != null).Select(s => s.Code!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = clock.GetUtcNow();
        var services = new List<Service>();
        var prices = new List<PriceVersion>();
        foreach (var row in DentalStarterList.India.Where(r => !taken.Contains(r.Code)))
        {
            var service = new Service { Id = Guid.CreateVersion7(), TenantId = TenantId, Name = "", CreatedBy = user.UserId, CreatedAt = now };
            await ApplyAsync(service, new SaveService
            {
                Name = row.Name, Category = row.Category, Code = row.Code, Sac = DentalStarterList.Sac, TaxTreatment = row.Tax, DurationMinutes = row.DurationMinutes,
                GstRateBasisPoints = row.Tax == TaxTreatment.Taxable ? DentalStarterList.CosmeticGstBasisPoints : null,
            }, cancellationToken);
            services.Add(service);
            prices.Add(new PriceVersion
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, ServiceId = service.Id, AmountMinor = row.PriceMinor, Currency = currency, EffectiveFrom = now,
                CreatedBy = user.UserId, CreatedAt = now,
            });
        }
        await SaveAuditedAsync(() => { db.Services.AddRange(services); db.PriceVersions.AddRange(prices); }, StarterAction, TenantId, cancellationToken);
        return new StarterLoadResult(services.Count, DentalStarterList.India.Count - services.Count);
    }
}
