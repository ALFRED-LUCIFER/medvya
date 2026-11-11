using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Identity.Application;
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
    /// <summary>What the paper prints besides its content: the verify link and its QR rows, the content hash, and the images.</summary>
    public sealed record Paper(string VerifyUrl, string[] Qr, string ContentHash, PrintImages Images);

    /// <exception cref="PdfUnsupportedTextException"/>
    public async Task<PaperPdf> IssueAsync(PaperKind kind, Guid facilityId, Guid sourceId, Guid doctorId, int items, object content, Func<Paper, byte[]> render,
        AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var code = RxVerifyToken.NewCode();
        var url = $"{PrescriptionService.VerifyBase(options.Value)}/verify/paper/{RxVerifyToken.Token(tenantId, code)}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content)));
        var pdf = render(new Paper(url, PrescriptionService.Qr(url), hash, await facilities.PrintImagesAsync(doctorId, facilityId, cancellationToken)));
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
/// The slip and the letter as English PDFs on A4 (MDV-P2-018/019): clinic and patient identifiers on every page, the content, the content hash and the
/// verify link, then the doctor's line with the signature image under it and the QR code. Names and free text in Hindi, Tamil or Arabic print shaped.
/// shortcut: English wording only (owner decision of 11 Nov 2026, as for the prescription); A4 only, add A5 if clinics ask.
/// </summary>
public static class PaperPdfDocument
{
    static string T(string key, params (string, string)[] values) => PaperText.Get(key, values);

    static string Patient(string name, string sex, string mrn, DateOnly dob) =>
        T("rxPrint.patient", ("name", name), ("sex", T($"rxPrint.sex.{sex}")), ("mrn", mrn), ("dob", dob.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));

    static byte[] Write(string title, string[] header, List<(string, bool)> body, string signedLine, PaperIssuer.Paper paper)
    {
        body.AddRange([
            (T("paper.hash", ("hash", paper.ContentHash)), false),
            (T("paper.verify", ("url", paper.VerifyUrl)), false),
            ("", false),
            (signedLine, true), // the signature image follows it
        ]);
        return PlainPdf.Write(header, body, (page, pages) => T("paper.footer", ("title", title), ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("pages", pages.ToString(CultureInfo.InvariantCulture))), PlainPdf.A4, paper.Qr, paper.Images.Logo, paper.Images.Signature);
    }

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Slip(OrderSlip s, string timeZone, PaperIssuer.Paper paper)
    {
        var title = T("orders.slip.title");
        var header = new List<string> { s.FacilityName, title, Patient(s.Patient.FullName, s.Patient.SexAtBirth, s.Patient.Mrn, s.Patient.DateOfBirth) };
        if (s.Destination is { } lab) header.Add(T("paper.slip.lab", ("lab", lab)));
        List<(string, bool)> body = [(T("orders.slip.tests"), true)];
        var n = 0;
        foreach (var o in s.Orders)
        {
            body.Add(($"{++n}. {o.Name} ({T($"orders.kind.{o.Kind}")})", true));
            if (o.Note is { } note) body.Add((note, false));
        }
        body.AddRange([("", false), (T("orders.slip.notice"), true), ("", false)]);
        return Write(title, [.. header], body, T("paper.slip.orderedBy", ("name", s.DoctorName ?? ""), ("when", PaperIssuer.When(s.OrderedAt, timeZone))), paper);
    }

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Letter(ReferralLetter l, string timeZone, PaperIssuer.Paper paper)
    {
        var title = T("referrals.letter.title");
        string[] header = [l.FacilityName, title, Patient(l.Patient.FullName, l.Patient.SexAtBirth, l.Patient.Mrn, l.Patient.DateOfBirth),
            T("paper.letter.to", ("provider", l.Provider), ("urgency", T($"referrals.urgency.{l.Urgency}")))];
        List<(string, bool)> body = [];
        if (l.Reason is { } reason) body.AddRange([(T("referrals.letter.reason"), true), (reason, false), ("", false)]);
        foreach (var section in l.Sections)
        {
            body.Add((T($"referrals.section.{section.Section}"), true));
            if (section.Lines.Count == 0) body.Add((T("referrals.letter.none"), false));
            body.AddRange(section.Lines.Select(x => ($"• {x.Text} [{T($"timeline.source.{x.Source}")}]", false)));
            body.Add(("", false));
        }
        body.AddRange([(T("referrals.letter.notice"), true), ("", false)]);
        return Write(title, header, body, T("paper.letter.from", ("name", l.DoctorName ?? ""), ("when", PaperIssuer.When(l.At, timeZone))), paper);
    }
}
