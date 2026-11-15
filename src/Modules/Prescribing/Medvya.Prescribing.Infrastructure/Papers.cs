using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// Hands out a server-made slip or letter (MDV-P2-018/019, ADR-018), the same way as the prescription: a new QR verify code, the SHA-256 of the content
/// it prints, the clinic logo and the doctor's signature, the PDF written once to Blob before its append-only row, and the audit event in the same
/// transaction. Each copy handed out is its own paper with its own code.
/// </summary>
public sealed class PaperIssuer(
    PrescribingDbContext db, IFacilityDirectory facilities, IDocumentStore files, IAuditLog audit, ICurrentTenant tenant, ICurrentUser user, TimeProvider clock,
    IOptions<PrescribingOptions> options)
{
    /// <summary>
    /// What the paper prints besides its content: the verify link and its QR rows, the content hash, the images, the clinic's address and phone, and the
    /// doctor's qualifications and registration (null: no doctor profile at this facility).
    /// </summary>
    public sealed record Paper(string VerifyUrl, string[] Qr, string ContentHash, PrintImages Images, FacilityInfo Facility, PrescriberInfo? Doctor);

    /// <exception cref="PdfUnsupportedTextException"/>
    public async Task<PaperPdf> IssueAsync(PaperKind kind, Guid facilityId, Guid sourceId, Guid doctorId, int items, object content, Func<Paper, byte[]> render,
        AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var code = RxVerifyToken.NewCode();
        var url = $"{PrescriptionService.VerifyBase(options.Value)}/verify/paper/{RxVerifyToken.Token(tenantId, code)}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content)));
        var facility = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.");
        var pdf = render(new Paper(url, QrRows.Of(url), hash, await facilities.PrintImagesAsync(doctorId, facilityId, cancellationToken), facility,
            await facilities.PrescriberAsync(doctorId, facilityId, cancellationToken)));
        var id = Guid.CreateVersion7();
        var key = $"{tenantId}/paper/{id}"; // ids only, no names in blob paths
        // Written before the row: a failed save leaves only an unreachable blob, never a row without its copy.
        await using (var stream = new MemoryStream(pdf, writable: false)) await files.PutAsync(key, stream, "application/pdf", cancellationToken);
        var row = new PrintedPaper
        {
            Id = id, TenantId = tenantId, FacilityId = facilityId, Kind = kind, SourceId = sourceId, DoctorId = doctorId, Items = items, VerifyCode = code,
            ContentHash = hash, Sha256 = Convert.ToHexStringLower(SHA256.HashData(pdf)), BlobKey = key,
            RenderedBy = user.UserId ?? throw new InvalidOperationException("No user."), RenderedAt = clock.GetUtcNow(),
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.PrintedPapers.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(auditEvent, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new PaperPdf(pdf, row.Sha256);
    }

    /// <summary>A date and time on the paper, in the facility's zone.</summary>
    public static string When(DateTimeOffset at, string timeZone) =>
        TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The patient's age in whole years on the day the paper was issued, by the facility's calendar.</summary>
    public static int Age(DateTimeOffset at, string timeZone, DateOnly dateOfBirth) =>
        CountryRules.AgeOn(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime), dateOfBirth);
}

/// <summary>
/// The public QR check of a slip or letter, inside the token's tenant (row-level security on). It answers what the paper is, when, from which clinic
/// and doctor, and how many items: never the patient or what it lists. Each answer is audited with the patient (no actor: the scanner is not signed in).
/// </summary>
public sealed class PaperVerification(
    PrescribingDbContext db, IEncounterDirectory encounters, IFacilityDirectory facilities, IStaffUsers staffUsers, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock)
    : IPaperVerification
{
    public const string VerifyAction = "paper.verify", Resource = "paper";

    public async Task<PaperVerifyView?> VerifyAsync(string verifyCode, CancellationToken cancellationToken = default)
    {
        if (await db.PrintedPapers.AsNoTracking().SingleOrDefaultAsync(p => p.VerifyCode == verifyCode, cancellationToken) is not { } paper) return null;
        var facility = await facilities.FindAsync(paper.FacilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.");
        var doctor = (await staffUsers.DisplayNamesAsync([paper.DoctorId], cancellationToken)).GetValueOrDefault(paper.DoctorId);
        var patient = paper.Kind == PaperKind.InvestigationSlip
            ? (await encounters.FindAsync(paper.SourceId, cancellationToken))?.PatientId
            : await db.Referrals.AsNoTracking().Where(r => r.Id == paper.SourceId).Select(r => (Guid?)r.PatientId).SingleOrDefaultAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), Action = VerifyAction, ResourceType = Resource,
            ResourceId = paper.Id, PatientId = patient, FacilityId = paper.FacilityId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed,
            OccurredAt = clock.GetUtcNow(), Detail = $"kind:{paper.Kind}",
        }, cancellationToken);
        return new PaperVerifyView(paper.Kind, paper.RenderedAt, facility.TimeZone, facility.Name, doctor ?? "", paper.Items);
    }
}

/// <summary>
/// The slip and the letter as English PDFs on A4 (MDV-P2-018/019) in the print family layout (MDV-P1-111): the clinic and doctor, the title and the
/// patient's identifiers on every page, the content as labelled rows, the signature box, and the footer with the verify QR code, link and content hash.
/// Names and free text in Hindi, Tamil or Arabic print shaped.
/// shortcut: English wording only (owner decision of 11 Nov 2026, as for the prescription); A4 only, add A5 if clinics ask.
/// </summary>
public static class PaperPdfDocument
{
    static string T(string key, params (string, string)[] values) => PaperText.Get(key, values);

    static byte[] Write(string title, string check, string when, string? doctorName, Action<QuestPDF.Infrastructure.IContainer> patient, List<PlainPdf.Row> rows,
        PaperIssuer.Paper paper)
    {
        var doctor = paper.Doctor?.FullName ?? doctorName ?? "";
        var mast = new PlainPdf.Masthead(paper.Facility.Name, paper.Facility.Address, paper.Facility.Phone, paper.Images.Logo, doctor, paper.Doctor is { } d
            ? [.. new[] { d.Qualifications }.OfType<string>(), T("printFamily.reg", ("number", d.LicenceNumber), ("authority", d.LicenceAuthority))] : []);
        var sign = new PlainPdf.Signature(paper.Images.Signature, doctor,
            [.. paper.Doctor is { } r ? [T("printFamily.regShort", ("number", r.LicenceNumber))] : Array.Empty<string>(), when]);
        var footer = new PlainPdf.Footer(paper.Qr, check, T("printFamily.scanOrOpen", ("url", paper.VerifyUrl)), T("paper.hash", ("hash", paper.ContentHash)), null,
            (page, pages) => T("printFamily.page", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        return PlainPdf.Write(PlainPdf.A4, mast, new PlainPdf.Meta(title, null, when), new PlainPdf.Row(T("printFamily.label.patient"), patient), rows, sign, footer);
    }

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Slip(OrderSlip s, string timeZone, PaperIssuer.Paper paper)
    {
        List<PlainPdf.Row> rows = [];
        if (s.Destination is { } lab) rows.Add(new(T("orders.slip.lab"), PlainPdf.Lines((lab, PlainPdf.Tone.Body))));
        rows.Add(new(T("orders.slip.tests"), PlainPdf.Items([.. s.Orders.Select(o => new PlainPdf.Item(o.Name, T($"orders.kind.{o.Kind}"), o.Note is { } note ? [note] : [], []))], PlainPdf.A4)));
        rows.Add(new(T("printFamily.label.bring"), PlainPdf.Lines((T("orders.slip.notice"), PlainPdf.Tone.Strong))));
        return Write(T("orders.slip.title"), T("printFamily.checkPaper"), T("printFamily.orderedAt", ("when", PaperIssuer.When(s.OrderedAt, timeZone))), s.DoctorName,
            RxPdfDocument.Patient(s.Patient.FullName, s.Patient.SexAtBirth, s.Patient.Mrn, s.Patient.DateOfBirth, PaperIssuer.Age(s.OrderedAt, timeZone, s.Patient.DateOfBirth)), rows, paper);
    }

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Letter(ReferralLetter l, string timeZone, PaperIssuer.Paper paper)
    {
        List<PlainPdf.Row> rows = [new(T("referrals.letter.to"), PlainPdf.Pairs([(l.Provider, $"· {T($"referrals.urgency.{l.Urgency}")}")]))];
        if (l.Reason is { } reason) rows.Add(new(T("printFamily.label.reason"), PlainPdf.Lines((reason, PlainPdf.Tone.Body))));
        foreach (var section in l.Sections)
        {
            var lines = section.Lines.Select(x => $"{x.Text} [{T($"timeline.source.{x.Source}")}]").ToList();
            rows.Add(new(T($"referrals.section.{section.Section}"), lines.Count == 0 ? PlainPdf.Lines((T("referrals.letter.none"), PlainPdf.Tone.Body))
                : section.Section == ReferralSection.Allergies ? PlainPdf.Allergy(T("printFamily.allergy"), string.Join("; ", lines))
                : PlainPdf.Lines([.. lines.Select(x => (x, PlainPdf.Tone.Body))])));
        }
        rows.Add(new(T("printFamily.label.note"), PlainPdf.Lines((T("referrals.letter.notice"), PlainPdf.Tone.Muted))));
        return Write(T("referrals.letter.title"), T("printFamily.checkLetter"), PaperIssuer.When(l.At, timeZone), l.DoctorName,
            RxPdfDocument.Patient(l.Patient.FullName, l.Patient.SexAtBirth, l.Patient.Mrn, l.Patient.DateOfBirth, PaperIssuer.Age(l.At, timeZone, l.Patient.DateOfBirth)), rows, paper);
    }
}
