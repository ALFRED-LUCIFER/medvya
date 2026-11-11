using System.Net;
using System.Text;
using Medvya.Encounters.Application;
using Medvya.Notifications.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-038: the authorisation and IDOR matrix over HTTP on real PostgreSQL (row-level security on). Every endpoint that takes an
/// id is listed in <see cref="CrossTenant"/> or, with the reason, in <see cref="NotInTheMatrix"/> (a guard test enforces it). For each:
/// (a) another tenant's real id answers exactly as a random id does (no existence oracle), never 2xx and never data, and changes
/// nothing; (b) a role without the capability gets 403; (c) a session at one facility cannot reach another facility. Synthetic data only.
/// </summary>
public sealed class AuthorisationMatrixTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid ownVisit, ownEncounter;
    Dictionary<string, Guid> theirs = null!;

    public async Task InitializeAsync()
    {
        foreach (var clinic in new[] { h, other })
        {
            await clinic.SeedAsync();
            await clinic.DoctorTemplateAsync();
        }
        await MarkTheirsAsync();
        (ownVisit, ownEncounter) = await InConsultationAsync(h);
        theirs = await TheirIdsAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:PermitsPerMinute", "100000")
            .UseSetting("RateLimiting:PatientRecordsPerMinute", "100000")
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant); // every caller is signed in to tenant A (h)
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
        await other.CloseAsync();
    }

    /// <summary>Tenant B's names carry <see cref="Theirs"/>, so any answer that holds one of B's rows is caught by text.</summary>
    const string Theirs = "(synthetic B)";

    async Task MarkTheirsAsync()
    {
        await using (var patients = sql.Patients(other.Tenant))
        {
            foreach (var p in await patients.Patients.ToListAsync()) p.FullName = $"Zara {p.Mrn} {Theirs}";
            await patients.SaveChangesAsync();
        }
        await using (var tenancy = sql.Context(other.Tenant))
        {
            (await tenancy.Tenants.SingleAsync()).Name = $"Other clinic {Theirs}";
            foreach (var f in await tenancy.Facilities.ToListAsync()) f.Name = $"Other location {Theirs}";
            await tenancy.SaveChangesAsync();
        }
        await using var identity = sql.Identity();
        foreach (var u in await identity.Users.Where(u => u.Id == other.Doctor || u.Id == other.SecondDoctor).ToListAsync()) u.DisplayName = $"Dr. Other {Theirs}";
        await identity.SaveChangesAsync();
    }

    static async Task<(Guid Visit, Guid Encounter)> InConsultationAsync(SchedulingHarness clinic)
    {
        var visit = await clinic.BookAsync(10);
        await clinic.FrontDesk(clinic.Reception).CheckInAsync(visit.Id, new CheckIn());
        var encounter = await clinic.Get<IEncounters>(clinic.Doctor).StartAsync(new StartEncounter(visit.Id));
        return (visit.Id, encounter!.Id);
    }

    /// <summary>Real rows of tenant B (other), one per kind of id an endpoint takes.</summary>
    async Task<Dictionary<string, Guid>> TheirIdsAsync()
    {
        var (visit, encounter) = await InConsultationAsync(other);
        var hold = await other.Booking(other.Reception).HoldAsync(new HoldSlot
        {
            FacilityId = other.Facility, CalendarId = other.Doctor, Start = SchedulingHarness.At(11), VisitType = Scheduling.Domain.VisitType.FollowUp,
        });
        var display = await other.Get<IQueueService>(other.Admin).CreateDisplayAsync(new CreateDisplay { FacilityId = other.Facility, Name = "Waiting room TV" });
        var closure = await other.Settings(other.Admin).AddClosureAsync(new AddClosure { FacilityId = other.Facility, Date = new(2026, 12, 25), Name = "Synthetic holiday" });
        var leave = await other.Adjustments(other.Admin).SaveAsync(new SaveAdjustment
        {
            FacilityId = other.Facility, CalendarId = other.Doctor, Kind = Scheduling.Domain.ExceptionKind.Leave, FromDate = new(2026, 12, 28), ToDate = new(2026, 12, 28),
        });
        await other.Settings(other.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        var request = await other.Get<IAccessRequests>(other.SecondDoctor, s => s.AddScoped<IAccessRequestParticipant, AccessRequestNotifier>())
            .RequestAsync(other.Patient, other.Facility, AccessRequestReason.CoveringColleague);

        var document = new Encounters.Domain.ClinicalDocument
        {
            Id = Guid.CreateVersion7(), TenantId = other.Tenant, FacilityId = other.Facility, PatientId = other.Patient, Type = Encounters.Domain.DocumentType.LabReport, DocumentDate = new(2026, 10, 1),
            Source = Encounters.Domain.ChartSource.Imported, ContentType = "application/pdf", SizeBytes = 1, Sha256 = new('0', 64), BlobKey = Guid.NewGuid().ToString("N"),
            ScanStatus = Encounters.Domain.ScanStatus.Clean, UploadedBy = other.Doctor, UploadedAt = SchedulingHarness.MondayMorning,
        };
        await using (var documents = sql.Encounters(other.Tenant))
        {
            documents.Documents.Add(document);
            await documents.SaveChangesAsync();
        }
        var order = new Medvya.Prescribing.Domain.ClinicalOrder
        {
            Id = Guid.CreateVersion7(), TenantId = other.Tenant, FacilityId = other.Facility, PatientId = other.Patient, EncounterId = encounter, Kind = Medvya.Prescribing.Domain.OrderKind.Lab,
            Name = "Synthetic test", State = Medvya.Prescribing.Domain.OrderState.Draft, CreatedBy = other.Doctor, CreatedAt = SchedulingHarness.MondayMorning,
        };
        var referral = new Medvya.Prescribing.Domain.Referral
        {
            Id = Guid.CreateVersion7(), TenantId = other.Tenant, FacilityId = other.Facility, PatientId = other.Patient, EncounterId = encounter, Direction = Medvya.Prescribing.Domain.ReferralDirection.Out,
            Provider = "Dr. Other (synthetic B)", Urgency = Medvya.Prescribing.Domain.ReferralUrgency.Routine, State = Medvya.Prescribing.Domain.ReferralState.Draft, CreatedBy = other.Doctor, CreatedAt = SchedulingHarness.MondayMorning,
        };
        var rxItem = new Medvya.Prescribing.Domain.PrescriptionItem
        {
            Id = Guid.CreateVersion7(), TenantId = other.Tenant, FacilityId = other.Facility, PatientId = other.Patient, EncounterId = encounter, Medicine = $"Synthetic medicine {Theirs}",
            CreatedBy = other.Doctor, CreatedAt = SchedulingHarness.MondayMorning, UpdatedAt = SchedulingHarness.MondayMorning,
        };
        var rxShare = new Medvya.Prescribing.Domain.RxShare
        {
            Id = Guid.CreateVersion7(), TenantId = other.Tenant, FacilityId = other.Facility, EncounterId = encounter, PrescriptionId = Guid.NewGuid(), PdfId = Guid.NewGuid(),
            Recipient = Medvya.Prescribing.Domain.RxShareRecipient.Pharmacy, Channel = Medvya.Prescribing.Domain.RxShareChannel.Email, PharmacyName = $"Pharmacy {Theirs}",
            LinkHash = InvitationToken.New(other.Tenant).Hash, ExpiresAt = SchedulingHarness.MondayMorning.AddDays(3), SharedBy = other.Doctor, SharedAt = SchedulingHarness.MondayMorning,
        };
        await using (var orders = sql.Prescribing(other.Tenant))
        {
            orders.RxShares.Add(rxShare);
            orders.Orders.Add(order);
            orders.PrescriptionItems.Add(rxItem);
            orders.Referrals.Add(referral);
            await orders.SaveChangesAsync();
        }
        await using var db = sql.Context(other.Tenant);
        var invited = new Membership { Id = Guid.NewGuid(), TenantId = other.Tenant, FacilityId = other.Facility, Role = Role.Receptionist, Status = MembershipStatus.Invited };
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(), TenantId = other.Tenant, MembershipId = invited.Id, Email = "new.desk@example.test", TokenHash = InvitationToken.New(other.Tenant).Hash,
            InvitedBy = other.Admin, CreatedAt = SchedulingHarness.MondayMorning, ExpiresAt = SchedulingHarness.MondayMorning.AddDays(7),
        };
        var role = new CustomRole { Id = Guid.NewGuid(), TenantId = other.Tenant, Name = "Synthetic desk lead", BaseRole = Role.Receptionist, Capabilities = "", CreatedAt = SchedulingHarness.MondayMorning };
        db.AddRange(invited, invitation, role);
        await db.SaveChangesAsync();
        var membership = await db.Memberships.Where(m => m.UserId == other.Reception).Select(m => m.Id).SingleAsync();
        var doctor = await db.Memberships.Where(m => m.UserId == other.Doctor).Select(m => m.Id).SingleAsync();

        return new()
        {
            ["patient"] = other.Patient, ["appointment"] = visit, ["encounter"] = encounter, ["document"] = document.Id, ["order"] = order.Id, ["rxItem"] = rxItem.Id, ["rxShare"] = rxShare.Id, ["referral"] = referral.Id, ["hold"] = hold.Id, ["display"] = display.Display.Id,
            ["closure"] = closure!.Id, ["exception"] = leave.Exception.Id, ["accessRequest"] = request.Id, ["invitation"] = invitation.Id,
            ["membership"] = membership, ["doctor"] = doctor, ["location"] = other.Facility, ["role"] = role.Id, ["user"] = other.Reception,
        };
    }

    public sealed record Hit(string Method, string Path, string? Body = null);

    /// <summary>
    /// Every endpoint with an id in its route, the id named by kind, and the tenant-A role that may call it on its own clinic's rows (so a
    /// refusal is the tenant boundary, not a missing capability).
    /// </summary>
    static readonly (Role Caller, Hit Call)[] CrossTenant =
    [
        (Role.Receptionist, new("GET", "/v1/patients/{patient}")),
        (Role.Receptionist, new("POST", "/v1/patients/{patient}/identifiers/reveal", "{}")),
        (Role.Receptionist, new("POST", "/v1/patients/{patient}/completion", """{"fullName":"Changed (synthetic)"}""")),
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/consents")),
        (Role.Receptionist, new("POST", "/v1/patients/{patient}/consents", """{"decisions":[{"type":"Marketing","granted":true}]}""")),
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/merges")),
        (Role.HospitalDoctor, new("POST", "/v1/patients/{patient}/access-requests", """{"reason":"CoveringColleague"}""")),
        (Role.Receptionist, new("GET", "/v1/schedule/patients/{patient}?facilityId={facility}")),
        (Role.PracticeAdmin, new("GET", "/v1/audit/access-log/patients/{patient}")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/cancel", """{"reason":"PatientRequest"}""")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/cancel-following", """{"reason":"PatientRequest"}""")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/reschedule", $$"""{"calendarId":"{doctorCalendar}","start":"{{SchedulingHarness.At(12, 20):O}}"}""")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/no-show")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/check-in", "{}")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/reassign", """{"calendarId":"{doctorCalendar}","reason":"PatientRequest"}""")),
        (Role.Receptionist, new("POST", "/v1/appointments/{appointment}/status", """{"state":"Done"}""")),
        (Role.Receptionist, new("GET", "/v1/appointments/{appointment}/notifications?facilityId={facility}")),
        (Role.Receptionist, new("GET", "/v1/appointments/{appointment}/history")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/chart")),
        (Role.SoloDoctor, new("POST", "/v1/documents/{document}/view-link")),
        (Role.SoloDoctor, new("PATCH", "/v1/documents/{document}", """{"type":"Other","documentDate":"2026-09-01"}""")),
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/documents")),
        (Role.Receptionist, new("POST", "/v1/patients/{patient}/documents?type=LabReport&date=2026-10-01&source=Imported")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/timeline")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/trends")),
        (Role.SoloDoctor, new("PUT", "/v1/patients/{patient}/history/Social", """{"text":"Synthetic"}""")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/child-health")),
        (Role.Nurse, new("POST", "/v1/patients/{patient}/immunisations", """{"vaccine":"BCG","dose":1,"outcome":"Deferred","reason":"Synthetic"}""")),
        (Role.Nurse, new("POST", "/v1/patients/{patient}/milestones", """{"band":"y4","milestone":"tellsAStory","answer":"Yes","answeredBy":"Parent"}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/end")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/medicines/reconciliation")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/medicines/reconciliation/decisions", """{"key":"synthetic","kind":"Keep"}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/medicines/reconciliation/confirm")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/problems")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/problems", """{"code":"I10","verification":"Verified"}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/note")),
        (Role.SoloDoctor, new("PUT", "/v1/encounters/{encounter}/note", """{"baseVersion":0,"sections":{}}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/note/sign", """{"baseVersion":0}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/note/addenda", """{"text":"Synthetic","reason":"Synthetic"}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/note/pdf")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/note/prefill")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/note/previous")),
        (Role.SoloDoctor, new("POST", "/v1/encounters", """{"appointmentId":"{appointment}","leaveOpen":true}""")),
        (Role.Nurse, new("GET", "/v1/intake/{appointment}")),
        (Role.Nurse, new("PUT", "/v1/intake/{appointment}", """{"chiefComplaint":"Synthetic","vitals":[]}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/orders")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/orders", """{"kind":"Lab","name":"Synthetic test"}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/orders/place", """{"destination":"Synthetic lab"}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/orders/slip")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/orders/slip/pdf")),
        (Role.SoloDoctor, new("PUT", "/v1/orders/{order}/included", """{"included":false}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/prescription/items", """{"medicine":"Synthetic medicine"}""")),
        (Role.SoloDoctor, new("PUT", "/v1/prescription-items/{rxItem}", """{"dose":"1","doseUnit":"tablet"}""")),
        (Role.SoloDoctor, new("PUT", "/v1/prescription-items/{rxItem}/included", """{"included":false}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription/checks")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/prescription/overrides", """{"key":"synthetic","reason":"Synthetic"}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription/authorisation")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/prescription/authorisation")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/prescription/cancellation", """{"reason":"Synthetic"}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription/print")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/prescription/pdf")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription/shares")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/prescription/shares", """{"recipient":"Patient","channel":"Sms"}""")),
        (Role.SoloDoctor, new("POST", "/v1/prescription-shares/{rxShare}/dispatch", """{"state":"Cancelled"}""")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/referrals", """{"provider":"Dr. Synthetic","urgency":"Routine","reason":"Synthetic","sections":[]}""")),
        (Role.SoloDoctor, new("POST", "/v1/referrals/{referral}/send", """{"channel":"Print"}""")),
        (Role.SoloDoctor, new("POST", "/v1/referrals/{referral}/status", """{"state":"Closed"}""")),
        (Role.SoloDoctor, new("GET", "/v1/referrals/{referral}/letter")),
        (Role.SoloDoctor, new("POST", "/v1/referrals/{referral}/letter/pdf")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/referrals")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/geriatric-assessments")),
        (Role.Nurse, new("POST", "/v1/patients/{patient}/geriatric-assessments", """{"clinicalFrailtyScale":4}""")),
        (Role.SoloDoctor, new("GET", "/v1/patients/{patient}/medicine-review")),
        (Role.SoloDoctor, new("POST", "/v1/encounters/{encounter}/orders/from-set", """{"orderSetId":"{order}"}""")),
        (Role.Nurse, new("POST", "/v1/intake/{appointment}/escalate", """{"reason":"Synthetic"}""")),
        (Role.Nurse, new("POST", "/v1/intake/{appointment}/handoff")),
        (Role.Nurse, new("POST", "/v1/intake/{appointment}/allergies", """{"substance":"Synthetic","status":"Confirmed"}""")),
        (Role.Nurse, new("POST", "/v1/intake/{appointment}/no-known-allergies")),
        (Role.Nurse, new("POST", "/v1/intake/{appointment}/medicines", """{"name":"Synthetic"}""")),
        (Role.Receptionist, new("DELETE", "/v1/slot-holds/{hold}")),
        (Role.PracticeAdmin, new("DELETE", "/v1/queue/displays/{display}")),
        (Role.PracticeAdmin, new("PUT", "/v1/schedule/exceptions/{exception}", """{"facilityId":"{facility}","calendarId":"{doctorCalendar}","kind":"Leave","fromDate":"2026-12-29","toDate":"2026-12-29"}""")),
        (Role.PracticeAdmin, new("DELETE", "/v1/schedule/exceptions/{exception}?facilityId={facility}")),
        (Role.PracticeAdmin, new("POST", "/v1/access-requests/{accessRequest}/grant", """{"days":30}""")),
        (Role.PracticeAdmin, new("POST", "/v1/access-requests/{accessRequest}/decline")),
        (Role.PracticeAdmin, new("DELETE", "/v1/invitations/{invitation}")),
        (Role.PracticeAdmin, new("POST", "/v1/staff/invitations/{invitation}/resend")),
        (Role.PracticeAdmin, new("POST", "/v1/staff/access-review/{membership}/confirm")),
        (Role.PracticeAdmin, new("POST", "/v1/staff/{membership}/offboard", """{"reason":"Synthetic test"}""")),
        (Role.PracticeAdmin, new("POST", "/v1/staff/{membership}/reactivate", """{"reason":"Synthetic test"}""")),
        (Role.PracticeAdmin, new("PUT", "/v1/staff/{membership}/locations", """{"facilityIds":["{facility}"],"reason":"Synthetic test"}""")),
        (Role.PracticeAdmin, new("PUT", "/v1/staff/roles/{role}", """{"name":"Renamed (synthetic)","baseRole":"Receptionist","capabilities":[]}""")),
        (Role.PracticeAdmin, new("DELETE", "/v1/staff/{user}/sessions")),
        (Role.PracticeAdmin, new("PUT", "/v1/settings/locations/{location}", """{"name":"Renamed (synthetic)"}""")),
        (Role.PracticeAdmin, new("GET", "/v1/settings/locations/{location}/hours")),
        (Role.PracticeAdmin, new("PUT", "/v1/settings/locations/{location}/hours", """[{"weekday":"Monday","opens":"09:00:00","closes":"13:00:00"}]""")),
        (Role.PracticeAdmin, new("GET", "/v1/settings/locations/{location}/closures")),
        (Role.PracticeAdmin, new("POST", "/v1/settings/locations/{location}/closures", """{"date":"2026-12-31","name":"Synthetic"}""")),
        (Role.PracticeAdmin, new("DELETE", "/v1/settings/closures/{closure}")),
        (Role.PracticeAdmin, new("PUT", "/v1/settings/doctors/{doctor}/licence", """{"authority":"Synthetic council","number":"SYN-0000","category":"MBBS","expiresOn":"2030-01-01","certificateName":"licence.pdf"}""")),
        (Role.PracticeAdmin, new("DELETE", "/v1/settings/doctors/{doctor}/signature")),
    ];

    /// <summary>Routes with an id that are not in <see cref="CrossTenant"/>, and why. The guard below fails on any route missing from both.</summary>
    static readonly Dictionary<string, string> NotInTheMatrix = new()
    {
        ["/v1/patients/merge-requests/{requestId:guid}"] = "merge requests: cross-tenant and facility refusals in PatientMergeEndpointTests and PatientMergeTests (RLS)",
        ["/v1/patients/merge-requests/{requestId:guid}/not-same"] = "as above",
        ["/v1/patients/merge-requests/{requestId:guid}/request-approval"] = "as above",
        ["/v1/patients/merge-requests/{requestId:guid}/approve"] = "as above",
        ["/v1/patients/merge-requests/{requestId:guid}/undo"] = "as above",
        ["/v1/patients/{patientId:guid}/history/{section}"] = "covered above as /history/Social: the section is a name, not an id",
        ["/v1/notifications/inbox/{id:guid}/read"] = "the inbox row is keyed by tenant and user; another user's id is a no-op (NotificationsTests)",
        ["/v1/me/sessions/{id:guid}"] = "own sessions only: the session row is matched on the caller's user id (SessionControlTests)",
        ["/v1/operations/organisations/{tenantId:guid}/documents/{documentId:guid}"] = "Medvya operations act on any organisation by design, behind the approval capability: a practice user gets 403 (OrganisationRequestTests)",
        ["/v1/operations/organisations/{tenantId:guid}"] = "as above",
        ["/v1/operations/organisations/{tenantId:guid}/reinstate"] = "as above",
        ["/v1/operations/organisations/{tenantId:guid}/approve"] = "as above",
        ["/v1/operations/organisations/{tenantId:guid}/request-changes"] = "as above",
        ["/v1/operations/organisations/{tenantId:guid}/reject"] = "as above",
        ["/v1/operations/organisations/{tenantId:guid}/suspend"] = "as above",
        ["/v1/settings/doctors/{id:guid}/signature"] = "PUT takes an image body; the DELETE on the same route is in the matrix",
        ["/v1/intake/{appointmentId:guid}/vitals/{kind}/keep"] = "same visit lookup and clinical access as the other intake routes, which are in the matrix; the reading is a route value (IntakeTests, IntakeEndpointTests)",
        ["/v1/encounters/{encounterId:guid}/problems/{problemId:guid}"] = "two ids: only a problem of the encounter's own patient is found, another patient's is not (ProblemListTests, with row-level security); the encounter route is in the matrix",
        ["/v1/intake/{appointmentId:guid}/allergies/{allergyId:guid}/status"] = "two ids: only an allergy of the visit's own patient is found, another patient's or clinic's is not (AllergyIntakeTests, with row-level security)",
        ["/v1/waitlist/{id:guid}"] = "waitlist: another practice's id is 404 and another location's is 403, in WaitlistEndpointTests and WaitlistTests (RLS)",
        ["/v1/waitlist/{id:guid}/offer"] = "as above",
        ["/v1/waitlist/{id:guid}/accept"] = "as above",
        ["/v1/waitlist/{id:guid}/decline"] = "as above",
    };

    /// <summary>
    /// Routes that answer another tenant's id exactly as any unknown id, with a success: what they return or change is the caller's own
    /// tenant's (row-level security), checked by the marker and the "nothing of theirs moved" assertions below.
    /// </summary>
    static readonly Dictionary<string, string> SucceedsAsANoOp = new()
    {
        ["/v1/audit/access-log/patients/{patient}"] = "the caller's own access log filtered by a patient id: another clinic's id matches only the caller's own refused attempts",
        ["/v1/appointments/{appointment}/notifications"] = "the caller's own reminder attempts for that id: an empty list",
        ["/v1/slot-holds/{hold}"] = "releasing a hold that is not the caller's does nothing (idempotent, IBookingService.ReleaseHoldAsync)",
        ["/v1/staff/{user}/sessions"] = "ends the user's sessions in the caller's tenant only (IStaffSessions.RevokeInTenantAsync)",
    };

    static string Route(Hit c) => c.Path.Split('?')[0];

    static string Fill(string text, Dictionary<string, Guid> ids, Guid facility, Guid calendar)
    {
        var s = new StringBuilder(text).Replace("{facility}", facility.ToString()).Replace("{doctorCalendar}", calendar.ToString());
        foreach (var (kind, id) in ids) s.Replace("{" + kind + "}", id.ToString());
        return s.ToString();
    }

    HttpClient Client(Guid user, Guid? facility = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", (facility ?? h.Facility).ToString());
        return client;
    }

    static async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpClient client, Hit call, Dictionary<string, Guid> ids, Guid facility, Guid calendar)
    {
        using var request = new HttpRequestMessage(new HttpMethod(call.Method), new Uri(Fill(call.Path, ids, facility, calendar), UriKind.Relative));
        if (call.Body is not null) request.Content = new StringContent(Fill(call.Body, ids, facility, calendar), Encoding.UTF8, "application/json");
        else if (call.Method is "POST" or "PUT") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnotherTenantsIdsAnswerLikeUnknownIdsAndChangeNothing()
    {
        var random = theirs.ToDictionary(p => p.Key, _ => Guid.NewGuid());
        var failures = new List<string>();
        foreach (var (caller, call) in CrossTenant)
        {
            using var client = Client(h.ByRole[caller]);
            // The facility and calendar in a query or body are the caller's own: only the id under test is the other clinic's.
            var (status, body) = await SendAsync(client, call, theirs, h.Facility, h.Doctor);
            var (unknown, _) = await SendAsync(client, call, random, h.Facility, h.Doctor);
            if (status != unknown) failures.Add($"{call.Method} {call.Path}: {(int)status} for their id but {(int)unknown} for an unknown one");
            if ((int)status < 400 && !SucceedsAsANoOp.ContainsKey(Route(call))) failures.Add($"{call.Method} {call.Path}: {(int)status}");
            if (body.Contains(Theirs, StringComparison.Ordinal)) failures.Add($"{call.Method} {call.Path}: the answer carries the other clinic's data");
        }
        None(failures);

        // Nothing of theirs moved: the visit is still in consultation, the encounter open, the invitation and the desk still live.
        await using var scheduling = sql.Scheduling(other.Tenant);
        Assert.Equal(Scheduling.Domain.AppointmentState.InConsult, await scheduling.Appointments.Where(a => a.Id == theirs["appointment"]).Select(a => a.State).SingleAsync());
        Assert.True(await scheduling.SlotHolds.AnyAsync(x => x.Id == theirs["hold"]));
        Assert.Null(await scheduling.QueueDisplays.Where(d => d.Id == theirs["display"]).Select(d => d.RevokedAt).SingleAsync());
        Assert.True(await scheduling.Exceptions.AnyAsync(x => x.Id == theirs["exception"] && x.FromDate == new DateOnly(2026, 12, 28)));
        Assert.Null((await other.Get<IEncounters>(other.Doctor).GetAsync(theirs["encounter"]))!.EndedAt);
        await using var tenancy = sql.Context(other.Tenant);
        Assert.Null(await tenancy.Invitations.Where(i => i.Id == theirs["invitation"]).Select(i => i.RevokedAt).SingleAsync());
        Assert.Equal(MembershipStatus.Active, await tenancy.Memberships.Where(m => m.Id == theirs["membership"]).Select(m => m.Status).SingleAsync());
        Assert.Equal("Synthetic desk lead", await tenancy.CustomRoles.Where(r => r.Id == theirs["role"]).Select(r => r.Name).SingleAsync());
        Assert.Equal($"Other location {Theirs}", await tenancy.Facilities.Where(f => f.Id == other.Facility).Select(f => f.Name).SingleAsync());
        Assert.True(await tenancy.Closures.AnyAsync(c => c.Id == theirs["closure"]));
        Assert.Equal(AccessRequestStatus.Pending, await tenancy.AccessRequests.Where(r => r.Id == theirs["accessRequest"]).Select(r => r.Status).SingleAsync());
        await using var patients = sql.Patients(other.Tenant);
        Assert.Equal($"Zara 000001 {Theirs}", await patients.Patients.Where(p => p.Id == other.Patient).Select(p => p.FullName).SingleAsync());
    }

    /// <summary>(b) Roles the matrix gives no access (docs/02-design/sitemap-and-permissions.md), each on its own clinic's real rows.</summary>
    static readonly (Role Caller, Hit Call)[] WithoutTheCapability =
    [
        (Role.Nurse, new("GET", "/v1/staff")), // Staff, roles, integrations: X
        (Role.Receptionist, new("GET", "/v1/staff")),
        (Role.HospitalDoctor, new("GET", "/v1/staff")),
        (Role.Nurse, new("POST", "/v1/invitations", """{"email":"x@example.test","role":"Receptionist","facilityId":"{facility}"}""")),
        (Role.Receptionist, new("POST", "/v1/staff/{membership}/offboard", """{"reason":"Synthetic"}""")),
        (Role.Receptionist, new("POST", "/v1/staff/{membership}/reactivate", """{"reason":"Synthetic"}""")),
        (Role.Receptionist, new("PUT", "/v1/staff/{membership}/locations", """{"facilityIds":["{facility}"],"reason":"Synthetic"}""")),
        (Role.Receptionist, new("GET", "/v1/settings/profile")),
        (Role.SoloDoctor, new("GET", "/v1/settings/profile")), // solo doctor holds Config: control row below
        (Role.Receptionist, new("GET", "/v1/schedule/metrics?facilityId={facility}")), // pilot metrics: Staff, roles, integrations at Config
        (Role.SoloDoctor, new("GET", "/v1/schedule/metrics?facilityId={facility}")),
        (Role.Nurse, new("PUT", "/v1/settings/clinical-access", """{"policy":"AllFacilityClinicians"}""")),
        (Role.Receptionist, new("GET", "/v1/settings/doctors")),
        (Role.Receptionist, new("GET", "/v1/access-requests")),
        (Role.Receptionist, new("GET", "/v1/encounters/{encounter}")), // Patient 360: X
        (Role.Receptionist, new("PUT", "/v1/encounters/{encounter}/note", """{"baseVersion":0,"sections":{}}""")), // Patient 360: X
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/note/sign", """{"baseVersion":0}""")), // Clinical notes: X
        (Role.Nurse, new("POST", "/v1/encounters/{encounter}/note/sign", """{"baseVersion":0}""")), // Clinical notes: Read, never Sign
        (Role.Nurse, new("POST", "/v1/encounters/{encounter}/note/addenda", """{"text":"Synthetic","reason":"Synthetic"}""")), // Clinical notes: Read, never Sign
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/note/pdf")), // Patient 360: X
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/note")), // notes are the doctor's: Patient 360 below Full
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/note/prefill")), // same gate as the note
        (Role.Receptionist, new("GET", "/v1/encounters/{encounter}/note/previous")), // same gate as the note
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/chart")), // Patient 360: X
        (Role.Receptionist, new("POST", "/v1/documents/{document}/view-link")), // the desk uploads and lists, never opens
        (Role.Receptionist, new("PATCH", "/v1/documents/{document}", """{"type":"Other","documentDate":"2026-09-01"}""")),
        (Role.Patient, new("GET", "/v1/patients/{patient}/documents")), // Register / book: Own
        (Role.HospitalIt, new("POST", "/v1/patients/{patient}/documents?type=LabReport&date=2026-10-01&source=Imported")),
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/timeline")),
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/trends")),
        (Role.Receptionist, new("PUT", "/v1/patients/{patient}/history/Social", """{"text":"Synthetic"}""")), // Vitals & allergies entry: X
        (Role.CareCoordinator, new("PUT", "/v1/patients/{patient}/history/Social", """{"text":"Synthetic"}""")),
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/child-health")), // Patient 360: X
        (Role.CareCoordinator, new("POST", "/v1/patients/{patient}/immunisations", """{"vaccine":"BCG","dose":1,"outcome":"Deferred","reason":"Synthetic"}""")), // Vitals & allergies entry: X
        (Role.Receptionist, new("POST", "/v1/patients/{patient}/milestones", """{"band":"y4","milestone":"tellsAStory","answer":"Yes","answeredBy":"Parent"}""")), // Vitals & allergies entry: X
        (Role.PracticeAdmin, new("GET", "/v1/patients/{patient}/chart")),
        (Role.PracticeAdmin, new("GET", "/v1/encounters/{encounter}")),
        (Role.Receptionist, new("POST", "/v1/encounters", """{"appointmentId":"{appointment}","leaveOpen":true}""")),
        (Role.Receptionist, new("GET", "/v1/intake/{appointment}")), // Vitals & allergies entry: X
        (Role.PracticeAdmin, new("POST", "/v1/intake/{appointment}/handoff")),
        (Role.Receptionist, new("POST", "/v1/intake/{appointment}/allergies", """{"substance":"Synthetic","status":"Confirmed"}""")),
        (Role.CareCoordinator, new("PUT", "/v1/intake/{appointment}", """{"chiefComplaint":"Synthetic","vitals":[]}""")),
        (Role.Nurse, new("PUT", "/v1/settings/nurse-intake", """{"enabled":true}""")), // the owner's switch: Staff, roles, integrations
        (Role.Receptionist, new("PUT", "/v1/settings/nurse-intake", """{"enabled":true}""")),
        (Role.Receptionist, new("POST", "/v1/chart/allergy-flags", """{"patientIds":["{patient}"]}""")), // Patient 360: X
        (Role.Receptionist, new("PUT", "/v1/note-templates/mine", """{"keys":["general-opd"]}""")), // Patient 360: X
        (Role.Nurse, new("GET", "/v1/note-templates/")),
        (Role.PracticeAdmin, new("POST", "/v1/chart/allergy-flags", """{"patientIds":["{patient}"]}""")),
        (Role.Nurse, new("POST", "/v1/encounters/{encounter}/referrals", """{"provider":"Dr. Synthetic","urgency":"Routine","reason":"Synthetic","sections":[]}""")), // Prescriptions & orders: X
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/referrals")), // Patient 360: X
        (Role.Receptionist, new("GET", "/v1/patients/{patient}/geriatric-assessments")), // Patient 360: X
        (Role.CareCoordinator, new("POST", "/v1/patients/{patient}/geriatric-assessments", """{"clinicalFrailtyScale":4}""")), // Vitals & allergies entry: X
        (Role.Nurse, new("GET", "/v1/patients/{patient}/medicine-review")), // Prescriptions & orders: X
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/orders")), // Prescriptions & orders: X
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/prescription")), // Prescriptions & orders: X
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/prescription/items", """{"medicine":"Synthetic medicine"}""")),
        (Role.PracticeAdmin, new("GET", "/v1/encounters/{encounter}/prescription")),
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/prescription/checks")), // Prescriptions & orders: X
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/prescription/overrides", """{"key":"synthetic","reason":"Synthetic"}""")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription/authorisation")),
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/prescription/authorisation")), // Prescriptions & orders: X
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/prescription/authorisation")),
        (Role.Nurse, new("POST", "/v1/encounters/{encounter}/prescription/cancellation", """{"reason":"Synthetic"}""")), // Prescriptions & orders: X
        (Role.PracticeAdmin, new("GET", "/v1/encounters/{encounter}/prescription/print")),
        (Role.Nurse, new("POST", "/v1/prescription-layout/approval")),
        (Role.SoloDoctor, new("GET", "/v1/encounters/{encounter}/prescription/shares")),
        (Role.Nurse, new("GET", "/v1/encounters/{encounter}/prescription/shares")), // Prescriptions & orders: X
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/prescription/shares", """{"recipient":"Patient","channel":"Sms"}""")), // the desk shares once the matrix says so
        (Role.Receptionist, new("POST", "/v1/encounters/{encounter}/orders/place", """{"destination":"Synthetic lab"}""")),
        (Role.PracticeAdmin, new("GET", "/v1/encounters/{encounter}/orders/slip")),
        (Role.Nurse, new("POST", "/v1/encounters/{encounter}/orders/slip/pdf")), // Prescriptions & orders: X
        (Role.Nurse, new("GET", "/v1/formulary")), // Prescriptions & orders: X
        (Role.PracticeAdmin, new("GET", "/v1/order-sets")), // Prescriptions & orders: X (the admin reads them under settings)
        (Role.HospitalDoctor, new("POST", "/v1/settings/formulary", """{"generic":"Synthetic","strength":"1 mg","form":"Tablet","regClass":"None"}""")), // Staff, roles, integrations: X
        (Role.Receptionist, new("POST", "/v1/settings/order-sets", """{"name":"Synthetic","items":["HbA1c"]}""")),
        (Role.Receptionist, new("GET", "/v1/audit/access-log")), // Audit log: X
        (Role.Nurse, new("GET", "/v1/audit/access-log/patients/{patient}")),
        (Role.HospitalIt, new("GET", "/v1/patients/{patient}")), // Register / book: X
        (Role.HospitalIt, new("POST", "/v1/patients/search", """{"query":"meera"}""")),
        (Role.Patient, new("GET", "/v1/patients/{patient}")),
        (Role.CareCoordinator, new("POST", "/v1/patients/{patient}/identifiers/reveal", "{}")), // Read only, reveal needs Full
        (Role.CareCoordinator, new("POST", "/v1/patients/{patient}/completion", """{"fullName":"Changed (synthetic)"}""")),
        (Role.Receptionist, new("POST", "/v1/patients/merge-requests/{patient}/approve")), // Clinical notes Sign
        (Role.Receptionist, new("POST", "/v1/patients/merge-requests/{patient}/undo")), // Config
        (Role.Patient, new("GET", "/v1/front-desk/board?facilityId={facility}")), // Check-in & queue: X
        (Role.HospitalIt, new("GET", "/v1/front-desk/board?facilityId={facility}")),
        (Role.Patient, new("POST", "/v1/appointments/{appointment}/check-in", "{}")),
        (Role.Patient, new("GET", "/v1/schedule?facilityId={facility}&date=2026-10-19")),
    ];

    [Fact]
    public async Task ARoleWithoutTheCapabilityIsRefused()
    {
        var ours = new Dictionary<string, Guid> { ["patient"] = h.Patient, ["appointment"] = ownVisit, ["encounter"] = ownEncounter, ["document"] = Guid.NewGuid() };
        await using (var db = sql.Context(h.Tenant))
            ours["membership"] = await db.Memberships.Where(m => m.UserId == h.Nurse).Select(m => m.Id).SingleAsync();
        var failures = new List<string>();
        foreach (var (caller, call) in WithoutTheCapability)
        {
            using var client = Client(h.ByRole[caller]);
            var (status, body) = await SendAsync(client, call, ours, h.Facility, h.Doctor);
            var expected = caller == Role.SoloDoctor ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            if (status != expected) failures.Add($"{caller} {call.Method} {call.Path}: {(int)status}, expected {(int)expected}");
            if (expected == HttpStatusCode.Forbidden && body.Contains("Meera", StringComparison.Ordinal)) failures.Add($"{caller} {call.Method} {call.Path}: refusal carries data");
        }
        None(failures);
    }

    [Fact]
    public async Task ASessionAtOneFacilityCannotReachAnother()
    {
        var failures = new List<string>();
        void Expect(string what, HttpStatusCode status, params HttpStatusCode[] allowed)
        {
            if (!allowed.Contains(status)) failures.Add($"{what}: {(int)status}");
        }

        // A practice admin of Velachery only, whose session claims Adyar: the membership is read from the database, never the claim.
        using (var elsewhere = Client(h.Elsewhere, h.Facility))
            foreach (var path in new[] { "/v1/staff", "/v1/settings/profile", $"/v1/patients/{h.Patient}", $"/v1/front-desk/board?facilityId={h.Facility}", "/v1/audit/access-log",
                $"/v1/schedule/metrics?facilityId={h.Facility}" })
                Expect($"Velachery admin at Adyar GET {path}", (await elsewhere.GetAsync(new Uri(path, UriKind.Relative))).StatusCode, HttpStatusCode.Forbidden);

        // Adyar reception asking for Velachery's rows by naming the facility.
        using var desk = Client(h.Reception);
        foreach (var path in new[]
        {
            $"/v1/front-desk/board?facilityId={h.OtherFacility}", $"/v1/front-desk/overview?facilityId={h.OtherFacility}",
            $"/v1/schedule?facilityId={h.OtherFacility}&date=2026-10-19", $"/v1/schedule/week?facilityId={h.OtherFacility}&calendarId={h.Doctor}&date=2026-10-19",
            $"/v1/queue/displays?facilityId={h.OtherFacility}", $"/v1/notifications/failed-visits?facilityId={h.OtherFacility}",
            $"/v1/schedule/patients/{h.OtherFacilityPatient}?facilityId={h.OtherFacility}",
        })
        {
            var response = await desk.GetAsync(new Uri(path, UriKind.Relative));
            Expect($"Adyar reception GET {path}", response.StatusCode, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
            if ((await response.Content.ReadAsStringAsync()).Contains("Lakshmi", StringComparison.Ordinal)) failures.Add($"{path}: Velachery's patient in the answer");
        }

        // MDV-P1-072: the patient record itself (/v1/patients/{id}, consents, completion) is shared by the organisation's locations, so it is not a
        // facility boundary any more; the cross-tenant matrix above and PatientRegistryTests cover its isolation (EF filter and row-level security).
        None(failures);
    }

    /// <summary>The matrix cannot silently fall behind the route table: a new route with an id must be added above or explained.</summary>
    [Fact]
    public void EveryRouteWithAnIdIsInTheMatrixOrExplained()
    {
        var routes = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.RoutePattern.Parameters.Any(p => p.ParameterPolicies.Any(pp => pp.Content == "guid")))
            .Select(e => e.RoutePattern.RawText!.TrimEnd('/')).Distinct();
        var covered = CrossTenant.Select(c => Generic(Route(c.Call))).ToHashSet();
        Assert.DoesNotContain(routes, r => !covered.Contains(Generic(r)) && !NotInTheMatrix.ContainsKey(r));
    }

    static void None(List<string> failures)
    {
        if (failures.Count > 0) Assert.Fail(string.Join(Environment.NewLine, failures));
    }

    // "/v1/patients/{patientId:guid}/consents" and "/v1/patients/{patient}/consents" compare equal.
    static string Generic(string route) => System.Text.RegularExpressions.Regex.Replace(route, @"\{[^}]+\}", "{}");
}
