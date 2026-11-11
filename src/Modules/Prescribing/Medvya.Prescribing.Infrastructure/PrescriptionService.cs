using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Patients.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using QRCoder;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>Settings (section Prescribing).</summary>
public sealed class PrescribingOptions
{
    /// <summary>Origin of the clinic app the QR verify link opens, e.g. https://in.medvya.example/app. Defaults to Invitations:LinkBaseUrl; never the request's Host header.</summary>
    public string? VerifyBaseUrl { get; set; }
}

/// <summary>
/// The draft prescription composer (MDV-P2-015). Clinical access first (Prescriptions and orders at Sign, care relationship, the encounter's
/// facility), then the work in one transaction with its audit event (purpose treatment, with the patient; codes only, never a medicine). Lines are
/// changed only while the consultation is open. Discontinue lines are not stored: they are the reconciliation's current stop decisions, so a
/// changed decision changes the line. Authorising (MDV-P2-017) is its own step, separate from signing the note: it freezes the draft as an
/// append-only version, and only an authorised, unchanged prescription prints.
/// </summary>
public sealed class PrescriptionService(
    PrescribingDbContext db, IEncounterDirectory encounters, IReconciliationStops stops, IPrescribingChart chart, IClinicalAccess clinical, IStaffAccess staff,
    ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IPatientDirectory patients, IFacilityDirectory facilities,
    IOptions<PrescribingOptions> options, IPatientContacts contacts, ISecureLinkMessages links, IDocumentStore files) : IPrescriptions
{
    public const string ReadAction = "rx.read", AddAction = "rx.add", UpdateAction = "rx.update", IncludeAction = "rx.include",
        CheckAction = "rx.check", OverrideAction = "rx.override", AuthorisationAction = "rx.authorisation", AuthoriseAction = "rx.authorise", CancelAction = "rx.cancel",
        PrintAction = "rx.print", PdfAction = "rx.pdf", LayoutApproveAction = "rx.layout.approve", ShareAction = "rx.share", ShareReadAction = "rx.share.read",
        DispatchAction = "rx.dispatch", AdviceReadAction = "rx.advice.read", AdviceSetAction = "rx.advice.set", Resource = "prescription";
    public const int MaxMedicine = 200, MaxStrength = 50, MaxReason = 200;
    static readonly Access[] Doctors = [Access.Sign];

    public async Task<IReadOnlyList<RxLineView>?> ListAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ReadAction, encounter, encounterId), cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxLineView>?> AddAsync(Guid encounterId, AddRxLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        string medicine;
        string? strength, form;
        if (command.FormularyItemId is { } itemId)
        {
            var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
            if (await db.FormularyItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.Country == country && i.Active, cancellationToken) is not { } item)
                throw new PrescriptionValidationException([("formularyItemId", "unknown")]);
            (medicine, strength, form) = (item.Generic, item.Strength, item.Form);
        }
        else
        {
            // Free text: the doctor's words, flagged for review because no formulary entry (and so no interaction data) stands behind it.
            medicine = Text(command.Medicine, "medicine", MaxMedicine, true, errors) ?? "";
            strength = Text(command.Strength, "strength", MaxStrength, false, errors);
            form = Text(command.Form, "form", 40, false, errors);
            if (form is not null && !FormularyRules.Forms.Contains(form)) errors.Add(("form", "unknown"));
        }
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);

        var (id, now) = (Guid.CreateVersion7(), clock.GetUtcNow());
        await SaveAsync(encounter, AddAction, id, command.FormularyItemId is null ? "free_text" : "formulary", () => db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = id, TenantId = TenantId, FacilityId = encounter.FacilityId, PatientId = encounter.PatientId, EncounterId = encounter.Id, FormularyItemId = command.FormularyItemId,
            Medicine = medicine, Strength = strength, Form = form, CreatedBy = Me, CreatedAt = now, UpdatedAt = now,
        }), cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxLineView>?> UpdateAsync(Guid itemId, UpdateRxLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.PrescriptionItems.SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken) is not { } item) return null;
        if (await RequireAsync(item.EncounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var sig = new SigParts(Blank(command.Dose), Blank(command.DoseUnit), Blank(command.Route), Blank(command.Frequency), Blank(command.Timing),
            command.DurationDays, command.Quantity, Blank(command.Instructions));
        if (Sig.Validate(sig) is { Count: > 0 } errors) throw new PrescriptionValidationException(errors);

        await SaveAsync(encounter, UpdateAction, itemId, null, () =>
        {
            (item.Dose, item.DoseUnit, item.Route, item.Frequency, item.Timing) = (sig.Dose, sig.DoseUnit, sig.Route, sig.Frequency, sig.Timing);
            (item.DurationDays, item.Quantity, item.Instructions, item.UpdatedAt) = (sig.DurationDays, sig.Quantity, sig.Instructions, clock.GetUtcNow());
        }, cancellationToken);
        return await ViewsAsync(item.EncounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxLineView>?> IncludeAsync(Guid itemId, IncludeRxLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.PrescriptionItems.SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken) is not { } item) return null;
        if (await RequireAsync(item.EncounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        if (command.Included is not { } included) throw new PrescriptionValidationException([("included", "required")]);
        await SaveAsync(encounter, IncludeAction, itemId, $"included:{included}", () => (item.Included, item.UpdatedAt) = (included, clock.GetUtcNow()), cancellationToken);
        return await ViewsAsync(item.EncounterId, cancellationToken);
    }

    public async Task<RxCheckView?> CheckAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var view = await ChecksAsync(encounter, cancellationToken);
        // The result is recorded with the rule set that gave it: version and counts only, never a medicine or an allergy.
        await audit.WriteAsync(Event(CheckAction, encounter, encounterId,
            $"rules:{view.RuleSetVersion};severe:{view.Alerts.Count(a => a.Severity == RxSeverity.Severe)};moderate:{view.Alerts.Count(a => a.Severity == RxSeverity.Moderate)};unresolved:{view.Unresolved}"), cancellationToken);
        return view;
    }

    public async Task<RxCheckView?> OverrideAsync(Guid encounterId, OverrideRxAlert command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        var reason = Text(command.Reason, "reason", MaxReason, true, errors);
        // Only a severe alert of the check as it stands now: an old key (the dose changed, the line went) needs the new finding's reason.
        var alert = (await ChecksAsync(encounter, cancellationToken)).Alerts.FirstOrDefault(a => a.Key == command.Key && a.Severity == RxSeverity.Severe);
        if (alert is null) errors.Add(("key", "unknown"));
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);

        var id = Guid.CreateVersion7();
        await SaveAsync(encounter, OverrideAction, id, $"kind:{alert!.Kind};rules:{SafetyRules.Version}", () => db.RxAlertOverrides.Add(new RxAlertOverride
        {
            Id = id, TenantId = TenantId, FacilityId = encounter.FacilityId, PatientId = encounter.PatientId, EncounterId = encounter.Id, AlertKey = alert.Key,
            Kind = alert.Kind, Reason = reason!, RuleSetVersion = SafetyRules.Version, OverriddenBy = Me, OverriddenAt = clock.GetUtcNow(),
        }), cancellationToken);
        return await ChecksAsync(encounter, cancellationToken);
    }

    public async Task<RxAuthorisationState?> AuthorisationAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var latest = await LatestAsync(encounterId, cancellationToken);
        await audit.WriteAsync(Event(AuthorisationAction, encounter, latest?.Id ?? encounterId, $"version:{latest?.VersionNo ?? 0}"), cancellationToken);
        return await StateAsync(latest, cancellationToken);
    }

    public async Task<RxAuthorisationState?> AuthoriseAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        // The endpoint already needs Sign on Prescriptions and orders; this adds the care relationship, then the step-up, as signing the note does.
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var now = RequireFreshReauth();
        // A paper prescription carries the doctor's licence (ADR-016): a member with no licence on record cannot authorise one.
        if (await facilities.PrescriberAsync(Me, encounter.FacilityId, cancellationToken) is null) throw new PrescriptionConflictException("no_licence");
        var lines = await DraftAsync(encounter.Id, cancellationToken);
        if (lines.Count == 0) throw new PrescriptionConflictException("nothing_to_authorise");
        if (lines.Any(l => !l.Discontinue && l.Sig["en"].Length == 0)) throw new PrescriptionValidationException([("lines", "no_instructions")]);
        // The checks run again here, never trusted from the screen: a severe alert without a reason stops the authorisation.
        var checks = await ChecksAsync(encounter, cancellationToken);
        if (checks.Unresolved > 0) throw new PrescriptionConflictException("unresolved_alerts");

        var advice = await AdviceTextAsync(encounter.Id, cancellationToken);
        var hash = AuthorisedPrescription.Hash(lines, advice);
        var latest = await LatestAsync(encounter.Id, cancellationToken);
        // A double click or retry: nothing new is written. A cancelled version authorised again is a new version.
        if (latest?.ContentHash == hash && await CancellationAsync(latest.Id, cancellationToken) is null) return await StateAsync(latest, cancellationToken);
        var row = new AuthorisedPrescription
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = encounter.FacilityId, EncounterId = encounter.Id, VersionNo = (latest?.VersionNo ?? 0) + 1,
            Lines = lines, Advice = advice, ContentHash = hash, RuleSetVersion = checks.RuleSetVersion, VerifyCode = RxVerifyToken.NewCode(), AuthorisedBy = Me, AuthorisedAt = now,
        };
        try
        {
            await SaveAsync(encounter, AuthoriseAction, row.Id, $"version:{row.VersionNo};lines:{lines.Count};rules:{checks.RuleSetVersion};reauth:ok",
                () => db.AuthorisedPrescriptions.Add(row), cancellationToken);
        }
        // Another authorisation took this version number at the same moment: nothing of this one was written; the answer says what stands.
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await StateAsync(await LatestAsync(encounter.Id, cancellationToken), cancellationToken);
        }
        return new(View(row), false);
    }

    public async Task<RxAuthorisationState?> CancelAsync(Guid encounterId, CancelRx command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<(string, string)>();
        var reason = Text(command.Reason, "reason", PrescriptionCancellation.MaxReason, true, errors);
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);
        // The same gate as authorising: Sign at the endpoint, then the care relationship and a fresh step-up.
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var now = RequireFreshReauth();
        var latest = await LatestAsync(encounter.Id, cancellationToken) ?? throw new PrescriptionConflictException("not_authorised");
        if (await CancellationAsync(latest.Id, cancellationToken) is not null) return await StateAsync(latest, cancellationToken);
        var row = new PrescriptionCancellation { Id = Guid.CreateVersion7(), TenantId = TenantId, PrescriptionId = latest.Id, Reason = reason!, CancelledBy = Me, CancelledAt = now };
        try
        {
            await SaveAsync(encounter, CancelAction, latest.Id, $"version:{latest.VersionNo};reauth:ok", () => db.PrescriptionCancellations.Add(row), cancellationToken);
        }
        // Cancelled in another tab at the same moment: that cancellation stands.
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
        return await StateAsync(latest, cancellationToken);
    }

    public async Task<RxPrintView?> PrintAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var rx = await PrintableAsync(encounter, cancellationToken);
        var view = await PrintViewAsync(encounter, rx, cancellationToken);
        await audit.WriteAsync(Event(PrintAction, encounter, rx.Id, $"version:{rx.VersionNo}"), cancellationToken);
        return view;
    }

    public async Task<RxPdf?> PdfAsync(Guid encounterId, string? paper, CancellationToken cancellationToken = default)
    {
        var size = paper switch { null or "A4" => PlainPdf.A4, "A5" => PlainPdf.A5, _ => throw new PrescriptionValidationException([("paper", "unknown")]) };
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var rx = await PrintableAsync(encounter, cancellationToken);
        var (pdf, content) = await RenderAsync(encounter, rx, size, cancellationToken);
        await SaveAsync(encounter, PdfAction, rx.Id, $"version:{rx.VersionNo};paper:{size.Name}", () => db.PrescriptionPdfs.Add(pdf), cancellationToken);
        return new RxPdf(content, pdf.Sha256);
    }

    /// <summary>
    /// Renders the paper with the clinic logo and the authorising doctor's signature, and writes it once to Blob under a random key, before its row:
    /// a failed save leaves only an unreachable blob, never a row without its copy.
    /// </summary>
    async Task<(PrescriptionPdf Row, byte[] Content)> RenderAsync(EncounterView encounter, AuthorisedPrescription rx, PlainPdf.Paper size, CancellationToken cancellationToken)
    {
        var images = await facilities.PrintImagesAsync(rx.AuthorisedBy, rx.FacilityId, cancellationToken);
        var content = RxPdfDocument.Render(await PrintViewAsync(encounter, rx, cancellationToken), size, images);
        var id = Guid.CreateVersion7();
        var key = $"{TenantId}/rx/{id}"; // ids only, no names in blob paths
        await using (var stream = new MemoryStream(content, writable: false)) await files.PutAsync(key, stream, "application/pdf", cancellationToken);
        return (new PrescriptionPdf
        {
            Id = id, TenantId = TenantId, PrescriptionId = rx.Id, Paper = size.Name, BlobKey = key,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), RenderedBy = Me, RenderedAt = clock.GetUtcNow(),
        }, content);
    }

    public async Task<IReadOnlyList<RxShareView>?> SharesAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ShareReadAction, encounter, encounterId), cancellationToken);
        return await ShareViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxShareView>?> ShareAsync(Guid encounterId, ShareRx command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<(string, string)>();
        var pharmacy = command.Recipient == RxShareRecipient.Pharmacy;
        string? name = null, email = null;
        if (command.Recipient is null) errors.Add(("recipient", "required"));
        if (pharmacy)
        {
            name = Text(command.PharmacyName, "pharmacyName", RxShareRules.MaxPharmacyName, true, errors);
            email = Text(command.PharmacyEmail, "pharmacyEmail", 254, true, errors);
            if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _)) errors.Add(("pharmacyEmail", "invalid"));
        }
        else if (command.Recipient is not null && command.Channel is null) errors.Add(("channel", "required"));
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);
        var channel = pharmacy ? RxShareChannel.Email : command.Channel!.Value;

        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        // Only the latest authorisation, still what the draft says: never an unauthorised, changed or replaced prescription.
        var rx = await PrintableAsync(encounter, cancellationToken);
        // The code that opens the link always goes to the patient's registered mobile, whoever the link is for.
        if (string.IsNullOrWhiteSpace((await contacts.FindAsync(encounter.PatientId, cancellationToken))?.Mobile)) throw new PrescriptionConflictException("no_mobile");
        var (pdf, _) = await RenderAsync(encounter, rx, PlainPdf.A4, cancellationToken);
        var (token, hash) = InvitationToken.New(TenantId);
        var now = clock.GetUtcNow();
        var share = new RxShare
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = encounter.FacilityId, EncounterId = encounter.Id, PrescriptionId = rx.Id, PdfId = pdf.Id,
            Recipient = command.Recipient!.Value, Channel = channel, PharmacyName = name, PharmacyEmail = email, LinkHash = hash,
            ExpiresAt = now + RxShareRules.LinkLifetime, SharedBy = Me, SharedAt = now,
        };
        var url = $"{BaseUrl}/rx/{token}";

        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.PrescriptionPdfs.Add(pdf);
            db.RxShares.Add(share);
            if (pharmacy) db.PharmacyDispatchEvents.Add(new() { Id = Guid.CreateVersion7(), TenantId = TenantId, ShareId = share.Id, State = DispatchState.Sent, ChangedBy = Me, ChangedAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(Event(ShareAction, encounter, share.Id, $"recipient:{share.Recipient};channel:{channel};version:{rx.VersionNo}"), tx.GetDbTransaction(), cancellationToken);
            // Sent inside the transaction: a message that cannot go leaves no share behind.
            try
            {
                if (pharmacy) await links.SendLinkToPharmacyAsync(email!, encounter.FacilityId, url, share.ExpiresAt, cancellationToken);
                else await links.SendLinkToPatientAsync(encounter.PatientId, encounter.FacilityId, channel == RxShareChannel.Sms ? NotificationChannel.Sms : NotificationChannel.Email,
                    url, share.ExpiresAt, share.Id, cancellationToken);
            }
            catch (SecureLinkNotSentException e)
            {
                throw new PrescriptionConflictException(e.Code);
            }
            await tx.CommitAsync(cancellationToken);
        }
        return await ShareViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxShareView>?> DispatchAsync(Guid shareId, UpdateDispatch command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.RxShares.AsNoTracking().SingleOrDefaultAsync(s => s.Id == shareId && s.Recipient == RxShareRecipient.Pharmacy, cancellationToken) is not { } share) return null;
        if (await RequireAsync(share.EncounterId, cancellationToken) is not { } encounter) return null;
        var now = clock.GetUtcNow();
        var errors = new List<(string, string)>();
        var reference = Text(command.PharmacyRef, "pharmacyRef", RxShareRules.MaxPharmacyRef, false, errors);
        var state = command.State ?? DispatchState.Sent;
        if (state == DispatchState.Sent) errors.Add(("state", "unknown")); // a dispatch starts as Sent; only the clinic's later states are recorded here
        if (state == DispatchState.Dispensed && (command.DispensedAt is not { } at || at > now.AddMinutes(5) || at < share.SharedAt.AddMinutes(-5))) errors.Add(("dispensedAt", "invalid"));
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);
        var current = await db.PharmacyDispatchEvents.AsNoTracking().Where(d => d.ShareId == shareId).OrderByDescending(d => d.ChangedAt).ThenByDescending(d => d.Id).FirstAsync(cancellationToken);
        if (!RxShareRules.CanMove(current.State, state)) throw new PrescriptionConflictException("dispatch_final");

        var row = new PharmacyDispatchEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ShareId = shareId, State = state, PharmacyRef = reference,
            DispensedAt = state == DispatchState.Dispensed ? command.DispensedAt : null, ChangedBy = Me, ChangedAt = now,
        };
        await SaveAsync(encounter, DispatchAction, shareId, $"state:{state}", () => db.PharmacyDispatchEvents.Add(row), cancellationToken);
        return await ShareViewsAsync(share.EncounterId, cancellationToken);
    }

    async Task<IReadOnlyList<RxShareView>> ShareViewsAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        var versions = await db.AuthorisedPrescriptions.AsNoTracking().Where(p => p.EncounterId == encounterId).ToDictionaryAsync(p => p.Id, p => p.VersionNo, cancellationToken);
        var latest = versions.Count == 0 ? 0 : versions.Values.Max();
        var shares = await db.RxShares.AsNoTracking().Where(s => s.EncounterId == encounterId).OrderByDescending(s => s.SharedAt).ThenByDescending(s => s.Id).ToListAsync(cancellationToken);
        var ids = shares.Select(s => s.Id).ToList();
        var dispatch = (await db.PharmacyDispatchEvents.AsNoTracking().Where(d => ids.Contains(d.ShareId)).ToListAsync(cancellationToken))
            .GroupBy(d => d.ShareId).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.ChangedAt).ThenByDescending(d => d.Id).First());
        return [.. shares.Select(s => new RxShareView(s.Id, s.Recipient, s.Channel, s.PharmacyName, versions[s.PrescriptionId], versions[s.PrescriptionId] < latest, s.SharedBy, s.SharedAt, s.ExpiresAt,
            dispatch.TryGetValue(s.Id, out var d) ? new DispatchView(d.State, d.PharmacyRef, d.DispensedAt, d.ChangedBy, d.ChangedAt) : null))];
    }

    public async Task<RxLayoutView> LayoutAsync(CancellationToken cancellationToken = default) =>
        new(RxPdfDocument.LayoutVersion, await db.RxLayoutApprovals.AsNoTracking().Where(a => a.UserId == Me && a.LayoutVersion == RxPdfDocument.LayoutVersion)
            .Select(a => (DateTimeOffset?)a.ApprovedAt).SingleOrDefaultAsync(cancellationToken));

    public async Task<RxLayoutView> ApproveLayoutAsync(CancellationToken cancellationToken = default)
    {
        if (await LayoutAsync(cancellationToken) is { ApprovedAt: not null } approved) return approved;
        var row = new RxLayoutApproval { Id = Guid.CreateVersion7(), TenantId = TenantId, UserId = Me, LayoutVersion = RxPdfDocument.LayoutVersion, ApprovedAt = clock.GetUtcNow() };
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.RxLayoutApprovals.Add(row);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            // Approved in another tab at the same moment: that approval stands.
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                await tx.RollbackAsync(cancellationToken);
                return await LayoutAsync(cancellationToken);
            }
            await audit.WriteAsync(new AuditEvent
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = Me, Action = LayoutApproveAction, ResourceType = "prescription_layout", ResourceId = row.Id,
                Purpose = AuditPurpose.Operations, Outcome = AuditOutcome.Allowed, OccurredAt = row.ApprovedAt, Detail = $"layout:{row.LayoutVersion}",
            }, tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return new(row.LayoutVersion, row.ApprovedAt);
    }

    Task<AuthorisedPrescription?> LatestAsync(Guid encounterId, CancellationToken cancellationToken) =>
        db.AuthorisedPrescriptions.AsNoTracking().Where(p => p.EncounterId == encounterId).OrderByDescending(p => p.VersionNo).FirstOrDefaultAsync(cancellationToken);

    // shortcut: "changed" compares the rendered instructions, so a wording fix (the translator's sign-off of Sig) marks older prescriptions changed
    // until re-authorised (it fails safe: no print, never wrong text); hash the structured sig instead once the wording is signed off.
    async Task<RxAuthorisationState> StateAsync(AuthorisedPrescription? latest, CancellationToken cancellationToken) =>
        latest is null ? new(null, false) : new(View(latest, await CancellationAsync(latest.Id, cancellationToken)),
            latest.ContentHash != AuthorisedPrescription.Hash(await DraftAsync(latest.EncounterId, cancellationToken), await AdviceTextAsync(latest.EncounterId, cancellationToken)));

    Task<string?> AdviceTextAsync(Guid encounterId, CancellationToken cancellationToken) =>
        db.RxAdvice.AsNoTracking().Where(a => a.EncounterId == encounterId).Select(a => a.Text).SingleOrDefaultAsync(cancellationToken);

    public async Task<RxAdviceView?> AdviceAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(AdviceReadAction, encounter, encounter.Id), cancellationToken);
        return await AdviceViewAsync(encounter, await AdviceTextAsync(encounter.Id, cancellationToken), cancellationToken);
    }

    public async Task<RxAdviceView?> SetAdviceAsync(Guid encounterId, SetRxAdvice command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<(string, string)>();
        var text = Text(command.Text, "text", RxAdvice.MaxText, false, errors);
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var row = await db.RxAdvice.SingleOrDefaultAsync(a => a.EncounterId == encounter.Id, cancellationToken);
        await SaveAsync(encounter, AdviceSetAction, encounter.Id, text is null ? "advice:none" : "advice:set", () =>
        {
            if (text is null) { if (row is not null) db.RxAdvice.Remove(row); return; }
            row ??= db.RxAdvice.Add(new RxAdvice { Id = Guid.CreateVersion7(), TenantId = TenantId, EncounterId = encounter.Id }).Entity;
            (row.Text, row.UpdatedBy, row.UpdatedAt) = (text, Me, clock.GetUtcNow());
        }, cancellationToken);
        return await AdviceViewAsync(encounter, text, cancellationToken);
    }

    // shortcut: there are no reviewed translations of advice yet, so a patient whose language is not English gets English with a note; look the
    // reviewed translation up here when a translation review flow exists. Never a machine translation.
    async Task<RxAdviceView> AdviceViewAsync(EncounterView encounter, string? text, CancellationToken cancellationToken)
    {
        var language = (await patients.SummariesAsync([encounter.PatientId], cancellationToken)).GetValueOrDefault(encounter.PatientId)?.PreferredLanguage;
        return new(text, language, language is null or "en");
    }

    Task<PrescriptionCancellation?> CancellationAsync(Guid prescriptionId, CancellationToken cancellationToken) =>
        db.PrescriptionCancellations.AsNoTracking().SingleOrDefaultAsync(c => c.PrescriptionId == prescriptionId, cancellationToken);

    /// <summary>The latest authorisation, once it is what the draft still says and not cancelled: an unauthorised, changed or cancelled prescription never prints.</summary>
    async Task<AuthorisedPrescription> PrintableAsync(EncounterView encounter, CancellationToken cancellationToken)
    {
        var latest = await LatestAsync(encounter.Id, cancellationToken) ?? throw new PrescriptionConflictException("not_authorised");
        var state = await StateAsync(latest, cancellationToken);
        if (state.Authorised!.Cancellation is not null) throw new PrescriptionConflictException("cancelled");
        if (state.Changed) throw new PrescriptionConflictException("changed");
        return latest;
    }

    /// <summary>The time now, once the caller re-authenticated within the window (authorising and cancelling, as for signing the note).</summary>
    DateTimeOffset RequireFreshReauth()
    {
        var now = clock.GetUtcNow();
        // shortcut: the stamp is the session cookie's claim set by /bff/reauth, as for signing (ConsultNoteService); upgrade both together if it must be revocable.
        if (user.ReauthenticatedAt is not { } at || now - at > AuthorisedPrescription.ReauthWindow || at > now.AddMinutes(1)) throw new ReauthRequiredException();
        return now;
    }

    /// <summary>The included prescribe lines (each instruction in every print language, its class's print requirements) and the reconciliation's stops, as authorising freezes them.</summary>
    async Task<List<AuthorisedLine>> DraftAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        var items = await db.PrescriptionItems.AsNoTracking().Where(i => i.EncounterId == encounterId && i.Included).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var ids = items.Where(i => i.FormularyItemId is not null).Select(i => i.FormularyItemId!.Value).ToList();
        var classes = await db.FormularyItems.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.RegClass, cancellationToken);
        var rules = classes.Count == 0 ? null : FormularyRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country."));
        var stopped = await stops.StopsAsync(encounterId, cancellationToken);
        var changed = await stops.ChangesAsync(encounterId, cancellationToken);
        return
        [
            .. items.Select(i => new AuthorisedLine(false, i.Medicine, i.Strength, i.Form, i.FormularyItemId is null,
                Sig.Languages.ToDictionary(l => l, l => Sig.Render(new SigParts(i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions), l)),
                i.FormularyItemId is { } f && classes.TryGetValue(f, out var c) ? [.. rules!.PrintRequirements(c)] : [], null)),
            .. stopped.Select(s => new AuthorisedLine(true, s.Name, null, null, false, Sig.Languages.ToDictionary(l => l, Sig.Stop), [], s.Reason)),
            // A change or taper prints as a numbered line whose instruction says so in words, with the doctor's reason.
            .. changed.Select(c => new AuthorisedLine(false, c.Name, null, null, false, ChangeSig(c), [], c.Reason)),
        ];
    }

    async Task<RxPrintView> PrintViewAsync(EncounterView encounter, AuthorisedPrescription rx, CancellationToken cancellationToken)
    {
        var patient = (await patients.SummariesAsync([encounter.PatientId], cancellationToken)).GetValueOrDefault(encounter.PatientId) ?? throw new InvalidOperationException("Patient not in this tenant.");
        var facility = await facilities.FindAsync(rx.FacilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.");
        var doctor = await facilities.PrescriberAsync(rx.AuthorisedBy, rx.FacilityId, cancellationToken) ?? throw new InvalidOperationException("The authorising doctor has no licence on record.");
        var facts = await chart.FactsAsync(encounter.PatientId, cancellationToken);
        var allergies = facts.AllergyState switch { AllergyState.Listed => ChartAllergies.Listed, AllergyState.NoKnownAllergies => ChartAllergies.NoKnownAllergies, _ => ChartAllergies.NotRecorded };
        var url = $"{BaseUrl}/verify/rx/{RxVerifyToken.Token(rx.TenantId, rx.VerifyCode)}";
        return new RxPrintView(View(rx), facility.Name, facility.TimeZone, doctor.FullName, doctor.Qualifications, doctor.LicenceNumber, doctor.LicenceAuthority,
            new SlipPatient(patient.FullName, patient.Mrn, patient.DateOfBirth, patient.SexAtBirth.ToString()), allergies,
            [.. facts.Allergies.Where(a => a.Status != AllergyStatus.NotAnAllergy && a.Substance is not null).Select(a => a.Substance!)],
            [.. rx.Lines.Select(l => new RxPrintLine(l.Discontinue, l.Medicine, l.Strength, l.Form, l.Sig, l.PrintRequirements, l.Reason))], url, Qr(url), RxPdfDocument.LayoutVersion,
            rx.Advice is null ? null : new RxAdviceView(rx.Advice, patient.PreferredLanguage, patient.PreferredLanguage is null or "en"));
    }

    /// <summary>The QR code's modules, row by row ('1' dark), with its quiet zone; error correction M survives a crease or a stamp.</summary>
    internal static string[] Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return [.. data.ModuleMatrix.Select(row => string.Concat(row.Cast<bool>().Select(b => b ? '1' : '0')))];
    }

    static RxAuthorisationView View(AuthorisedPrescription p, PrescriptionCancellation? c = null) => new(p.VersionNo, p.Number(), p.AuthorisedBy, p.AuthorisedAt, p.ContentHash,
        p.Lines.Count(l => !l.Discontinue), c is null ? null : new RxCancellationView(c.Reason, c.CancelledBy, c.CancelledAt));

    /// <summary>The checks over the included prescribe lines and the chart's active medicines (less those stopped in this visit), with the current overrides
    /// (made under this rule set version: a new version asks again).</summary>
    async Task<RxCheckView> ChecksAsync(EncounterView encounter, CancellationToken cancellationToken)
    {
        var items = await db.PrescriptionItems.AsNoTracking().Where(i => i.EncounterId == encounter.Id && i.Included).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var formularyIds = items.Where(i => i.FormularyItemId is not null).Select(i => i.FormularyItemId!.Value).ToList();
        var atcOf = await db.FormularyItems.AsNoTracking().Where(f => formularyIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Atc, cancellationToken);
        var facts = await chart.FactsAsync(encounter.PatientId, cancellationToken);
        // The generic names are read only when there are chart medicines to match (a clinic's formulary can hold thousands of rows).
        Dictionary<string, string?> generics = new(StringComparer.OrdinalIgnoreCase);
        if (facts.ActiveMedicines.Count > 0)
        {
            var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
            generics = (await db.FormularyItems.AsNoTracking().Where(f => f.Country == country && f.Active && f.Atc != null).Select(f => new { f.Generic, f.Atc }).ToListAsync(cancellationToken))
                .GroupBy(f => f.Generic, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(f => f.Atc!).Distinct().Count() == 1 ? g.First().Atc : null, StringComparer.OrdinalIgnoreCase);
        }
        var stopped = (await stops.StopsAsync(encounter.Id, cancellationToken)).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lines = items.Select(i => new CheckedMedicine(i.Id, i.Medicine, i.FormularyItemId is { } f ? atcOf.GetValueOrDefault(f) : null, i.Strength,
            new SigParts(i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions), false, i.FormularyItemId is null)).ToList();
        // A chart medicine is checked only when its name names one formulary generic with one ATC code; otherwise it is reported as not checked.
        var onChart = facts.ActiveMedicines.Where(m => !stopped.Contains(m.Name))
            .Select(m => new CheckedMedicine(m.Id, m.Name, SafetyRules.MatchGeneric(m.Name, generics.Keys) is { } g ? generics[g] : null, null, null, true, false)).ToList();
        var state = facts.AllergyState switch
        {
            AllergyState.Listed => ChartAllergies.Listed,
            AllergyState.NoKnownAllergies => ChartAllergies.NoKnownAllergies,
            _ => ChartAllergies.NotRecorded,
        };
        var patient = (await patients.SummariesAsync([encounter.PatientId], cancellationToken)).GetValueOrDefault(encounter.PatientId) ?? throw new InvalidOperationException("Patient not in this tenant.");
        var now = clock.GetUtcNow();
        // eGFR in whole units, rounded down so a reading just under a threshold counts as under it.
        // shortcut: the latest eGFR at any age, and no pregnancy until MDV-P2-034 feeds it (reported as not checked until then); an eGFR age limit
        // when the clinical reviewer sets one (MDV-P2-028).
        var context = new PatientContext(CountryRules.AgeOn(CountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.")).Today(now), patient.DateOfBirth),
            patient.SexAtBirth == Medvya.Patients.Domain.SexAtBirth.Female, facts.Weight is { } w && w.TakenAt >= now.AddMonths(-1) ? w.ValueHundredths : null, facts.Egfr?.ValueHundredths / 100, null);
        var result = SafetyRules.Evaluate(lines, onChart, state, [.. facts.Allergies.Select(a => new CheckedAllergy(a.Id, a.Substance, a.Status.ToString()))], context);

        var keys = result.Alerts.Select(a => a.Key).ToList();
        var overrides = (await db.RxAlertOverrides.AsNoTracking().Where(o => o.EncounterId == encounter.Id && o.RuleSetVersion == SafetyRules.Version && keys.Contains(o.AlertKey)).ToListAsync(cancellationToken))
            .GroupBy(o => o.AlertKey).Select(g => g.OrderByDescending(o => o.OverriddenAt).ThenByDescending(o => o.Id).First())
            .Select(o => new RxOverrideView(o.AlertKey, o.Reason, o.RuleSetVersion, o.OverriddenBy, o.OverriddenAt)).ToList();
        var unresolved = result.Alerts.Count(a => a.Severity == RxSeverity.Severe && overrides.All(o => o.Key != a.Key));
        return new(SafetyRules.Source, SafetyRules.Version, state, result.Alerts, result.NotChecked, overrides, unresolved, now, result.WeightDoses);
    }

    /// <summary>The encounter, once the caller may prescribe for its patient at its facility. Null: no such encounter in this tenant.</summary>
    async Task<EncounterView?> RequireAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        if (await encounters.FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        return encounter;
    }

    static void Open(EncounterView encounter)
    {
        if (encounter.Status != EncounterStatus.Active) throw new PrescriptionConflictException("encounter_ended");
    }

    async Task SaveAsync(EncounterView encounter, string action, Guid resourceId, string? detail, Action change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, encounter, resourceId, detail), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    async Task<IReadOnlyList<RxLineView>> ViewsAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        var items = await db.PrescriptionItems.AsNoTracking().Where(i => i.EncounterId == encounterId).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var stopped = await stops.StopsAsync(encounterId, cancellationToken);
        return
        [
            .. items.Select(i =>
            {
                var sig = new SigParts(i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions);
                return new RxLineView(i.Id, RxLineKind.Prescribe, i.CreatedBy == Me ? RxSource.AddedByYou : RxSource.AddedByColleague, i.FormularyItemId, i.Medicine, i.Strength, i.Form,
                    i.FormularyItemId is null, i.Included, i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions, null,
                    Sig.Languages.ToDictionary(l => l, l => Sig.Render(sig, l)));
            }),
            .. stopped.Select(s => new RxLineView(null, RxLineKind.Discontinue, RxSource.ChartReconciliation, null, s.Name, null, null, false, true,
                null, null, null, null, null, null, null, null, s.Reason, Sig.Languages.ToDictionary(l => l, Sig.Stop))),
            .. (await stops.ChangesAsync(encounterId, cancellationToken)).Select(c => new RxLineView(null, c.Kind == MedicationDecisionKind.Taper ? RxLineKind.Taper : RxLineKind.Change,
                RxSource.ChartReconciliation, null, c.Name, null, null, false, true, null, null, null, null, null, null, null, null, c.Reason, ChangeSig(c))),
        ];
    }

    static Dictionary<string, string> ChangeSig(ChangedMedicine c) => Sig.Languages.ToDictionary(l => l, l => c.Kind == MedicationDecisionKind.Taper
        ? Sig.Taper(l, c.Steps!.Select(s => (s.Dose, s.Days)), c.EndsOn!.Value)
        : Sig.Change(l, c.FromDose, c.NewDose!));

    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static string? Text(string? value, string field, int max, bool required, List<(string, string)> errors)
    {
        var v = Blank(value);
        if (v is null && required) errors.Add((field, "required"));
        else if (v?.Length > max) errors.Add((field, "too_long"));
        return v;
    }

    AuditEvent Event(string action, EncounterView encounter, Guid resourceId, string? detail = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = resourceId,
        PatientId = encounter.PatientId, FacilityId = encounter.FacilityId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), Detail = detail,
    };

    string BaseUrl => VerifyBase(options.Value);

    internal static string VerifyBase(PrescribingOptions o) =>
        (o.VerifyBaseUrl ?? throw new InvalidOperationException("Prescribing:VerifyBaseUrl (or Invitations:LinkBaseUrl) is not configured.")).TrimEnd('/');
    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");
}
