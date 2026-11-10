using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.SyntheticData;

/// <summary>What one <see cref="SyntheticPractice.SeedAsync"/> run added (patients, visits) and what the practice has now (staff, rooms).</summary>
public sealed record PracticeSeeded(Guid Tenant, Guid Facility, int Patients, int Doctors, int Nurses, int Receptionists, int Admins, int Rooms, int Visits);

/// <summary>
/// A synthetic multi-doctor practice for testing on Azure dev and locally (docs/05-delivery/load-testing.md): doctors across specialties (two general
/// practitioners, so a walk-in can be sent to the other one), nurses, receptionists, a practice admin, rooms, weekly hours for every calendar, and a few
/// visits (future bookings, today's checked-in visits, a walk-in queue per doctor). Patients come from <see cref="SyntheticPatients"/>.
/// Staff get identity.Users rows so lists show them, but NO Entra account: nobody can sign in as them. Written as the application role inside the tenant.
/// shortcut: visits are written directly (no audit event, no outbox event, so nothing is ever messaged) and are dated from "now", so a re-run on a later day
/// adds that day's visits too; use the booking API for anything that must be audited.
/// </summary>
public static class SyntheticPractice
{
    public const string DefaultPractice = "Menon Multi-Doctor Clinic";

    // The last two doctors are left "Verification pending" to test the gated state; the rest are verified.
    sealed record Doc(string Name, string Specialty, string Qualifications, int SlotMinutes, DayOfWeek[] Days, (int From, int To)[] Sessions, bool Verified);

    static readonly DayOfWeek[] MonSat = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    static readonly Doc[] Doctors =
    [
        new("Dr. Anil Menon", "General Practice", "MBBS, MD", 15, MonSat, [(9, 13), (17, 20)], true),
        new("Dr. Suma Pillai", "General Practice", "MBBS, DNB", 15, MonSat, [(9, 13), (16, 19)], true),
        new("Dr. Rahul Nair", "Paediatrics", "MBBS, MD", 20, MonSat[..5], [(10, 14)], true),
        new("Dr. Lakshmi Iyer", "Gynaecology", "MBBS, MS", 30, MonSat, [(10, 13)], true),
        new("Dr. George Thomas", "Cardiology", "MBBS, DM", 30, [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], [(9, 13)], true),
        new("Dr. Joseph Varghese", "Orthopaedics", "MBBS, MS", 30, MonSat, [(17, 20)], true),
        new("Dr. Fatima Khan", "Dermatology", "MBBS, MD", 20, [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday], [(10, 14)], false),
        new("Dr. Vijay Kurup", "ENT", "MBBS, MS", 20, MonSat[..5], [(14, 18)], false),
    ];

    static readonly string[] Nurses = ["Sheeba Joseph", "Anju Thomas", "Remya Nair", "Bindu Pillai", "Mary Varghese", "Deepa Menon", "Latha Kurup", "Sreeja Iyer"];
    static readonly string[] Receptionists = ["Arun Kumar", "Divya Raj", "Salma Beegum"];
    static readonly string[] Rooms = ["ECG room", "Minor procedure room", "Nebulisation room"];

    static Guid Det(Guid tenant, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"medvya-synthetic-practice-row:{tenant}:{key}")).AsSpan(0, 16));

    /// <summary>Unique per doctor and valid for the India licence format (3 to 20 letters, digits, "/" and "-"), so it passes the same check as the doctor form.</summary>
    public static string Licence(int i) => $"SYN-TCMC-{10001 + i}";

    public static async Task<PracticeSeeded> SeedAsync(string connectionString, string? environment, string practice, int patientCount, int mrnStart,
        Guid? memberUserId = null, string region = "centralindia", CancellationToken ct = default)
    {
        // Patients first: this runs every refusal rule (environment, name, "(synthetic)" marker) and creates the tenant and facility.
        var (tenant, facility, _, patientsAdded) = await SyntheticPatients.SeedAsync(connectionString, environment, practice, patientCount, mrnStart, null, memberUserId, region, ct);
        var at = DateTimeOffset.UtcNow;
        var by = Det(tenant, "operations"); // the owner who attests the licences: a fixed synthetic id, not an account

        var people = Doctors.Select((d, i) => (Key: $"doctor{i}", d.Name, Role: Role.HospitalDoctor))
            .Concat(Nurses.Select((n, i) => (Key: $"nurse{i}", Name: n + " RN", Role: Role.Nurse)))
            .Concat(Receptionists.Select((n, i) => (Key: $"reception{i}", Name: n, Role: Role.Receptionist)))
            .Append((Key: "admin0", Name: "K. Radhakrishnan", Role: Role.PracticeAdmin))
            .Select(p => (p.Key, Name: p.Name + SyntheticSeed.Marker, p.Role, UserId: Det(tenant, "user:" + p.Key), MembershipId: Det(tenant, "member:" + p.Key))).ToList();

        int doctors, nurses, receptionists, admins;
        await using (var db = SyntheticSeed.Tenancy(connectionString, tenant))
        {
            var members = await db.Memberships.Select(m => m.Id).ToListAsync(ct);
            db.Memberships.AddRange(people.Where(p => !members.Contains(p.MembershipId)).Select(p => new Membership
            {
                Id = p.MembershipId, TenantId = tenant, UserId = p.UserId, FacilityId = facility, Role = p.Role, Status = MembershipStatus.Active,
            }));
            var listed = await db.DoctorLicences.Select(l => l.MembershipId).ToListAsync(ct);
            for (var i = 0; i < Doctors.Length; i++)
            {
                var (d, m) = (Doctors[i], people[i].MembershipId);
                if (listed.Contains(m)) continue;
                db.DoctorProfiles.Add(new DoctorProfile
                {
                    MembershipId = m, TenantId = tenant, FullName = people[i].Name, Specialty = d.Specialty, Qualifications = d.Qualifications, Languages = "English, Malayalam",
                    NewVisitFeeMinor = 50_000, FollowUpFeeMinor = 30_000, ChangedAt = at,
                });
                db.DoctorLicences.Add(new DoctorLicence
                {
                    MembershipId = m, TenantId = tenant, Authority = "TCMC", Number = Licence(i), Category = "Permanent", SubmittedAt = at,
                    ExpiresOn = d.Verified ? DateOnly.FromDateTime(at.UtcDateTime).AddYears(5) : null,
                    AttestedAt = at, AttestedBy = by,
                });
            }
            await db.SaveChangesAsync(ct);
            doctors = await db.Memberships.CountAsync(m => m.Role == Role.HospitalDoctor, ct);
            nurses = await db.Memberships.CountAsync(m => m.Role == Role.Nurse, ct);
            receptionists = await db.Memberships.CountAsync(m => m.Role == Role.Receptionist, ct);
            admins = await db.Memberships.CountAsync(m => m.Role == Role.PracticeAdmin, ct);
        }

        // Users exist so lists show names; issuer "development" has no sign-in outside Development, and no Entra account stands behind them.
        await using (var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            var have = await identity.Users.Select(u => u.Id).ToListAsync(ct);
            identity.Users.AddRange(people.Where(p => !have.Contains(p.UserId)).Select(p => new StaffUser
            {
                Id = p.UserId, Issuer = IdentityProviders.Development, Subject = p.UserId.ToString(), DisplayName = p.Name,
            }));
            await identity.SaveChangesAsync(ct);
        }

        await using var sdb = SyntheticSeed.Scheduling(connectionString, tenant);
        var resources = await sdb.Resources.Select(r => r.Id).ToListAsync(ct);
        var templates = await sdb.Templates.Select(t => t.Id).ToListAsync(ct);
        ScheduleTemplate Template(string key, Guid calendar, CalendarKind kind, int slot, DayOfWeek[] days, (int From, int To)[] sessions, string room) => new()
        {
            Id = Det(tenant, "template:" + key), TenantId = tenant, FacilityId = facility, CalendarId = calendar, CalendarKind = kind, SlotMinutes = slot,
            Hours = [.. days.SelectMany(d => sessions.Select(s => new WorkingHours { Weekday = d, Start = new(s.From, 0), End = new(s.To, 0), Room = room }))],
            VisitLengths = [new VisitLength { VisitType = VisitType.NewConsultation, Minutes = 2 * slot }], ChangedAt = at,
        };
        var doctorTemplates = Doctors.Select((d, i) => Template($"doctor{i}", people[i].UserId, CalendarKind.Doctor, d.SlotMinutes, d.Days, d.Sessions, $"Room {i + 1}")).ToList();
        var wanted = new List<ScheduleTemplate>(doctorTemplates);
        for (var r = 0; r < Rooms.Length; r++)
        {
            var id = Det(tenant, "room:" + r);
            if (!resources.Contains(id)) sdb.Resources.Add(new ScheduleResource { Id = id, TenantId = tenant, FacilityId = facility, Name = Rooms[r] + SyntheticSeed.Marker, CreatedAt = at });
            wanted.Add(Template($"room{r}", id, CalendarKind.Resource, 30, MonSat, [(10, 13)], Rooms[r]));
        }
        sdb.Templates.AddRange(wanted.Where(t => !templates.Contains(t.Id)));
        await sdb.SaveChangesAsync(ct);
        var rooms = await sdb.Resources.CountAsync(ct);

        var visits = await AddVisitsAsync(sdb, tenant, facility, patientCount, mrnStart, doctorTemplates, at, ct);
        return new(tenant, facility, patientsAdded, doctors, nurses, receptionists, admins, rooms, visits);
    }

    /// <summary>Per doctor: three bookings in the next ten days; today (when they work): two checked in, one with the nurse, one booked, and two walk-ins queued with no slot.</summary>
    static async Task<int> AddVisitsAsync(SchedulingDbContext db, Guid tenant, Guid facility, int patientCount, int mrnStart,
        List<ScheduleTemplate> doctors, DateTimeOffset now, CancellationToken ct)
    {
        var ist = TimeSpan.FromHours(5.5);
        var today = DateOnly.FromDateTime((now + ist).DateTime);
        var have = (await db.Appointments.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        var tokens = await db.QueueTokens.Where(t => t.Date == today).Select(t => new { t.CalendarId, t.Number }).ToListAsync(ct);
        var patient = 0;
        var added = 0;
        Guid NextPatient() => SyntheticPatients.PatientId(tenant, (mrnStart + patient++ % patientCount).ToString("D6", CultureInfo.InvariantCulture));

        for (var d = 0; d < doctors.Count; d++)
        {
            var t = doctors[d];
            DateTimeOffset SlotStart(DateOnly day, int index) =>
                new DateTimeOffset(day.ToDateTime(t.Hours.First(h => h.Weekday == day.DayOfWeek).Start), ist).AddMinutes(index * t.SlotMinutes).ToUniversalTime();
            bool Works(DateOnly day) => t.Hours.Any(h => h.Weekday == day.DayOfWeek);
            var prefix = QueueToken.PrefixAt(d);
            var number = tokens.Where(k => k.CalendarId == t.CalendarId).Select(k => k.Number).DefaultIfEmpty(0).Max();

            void Add(string key, DateTimeOffset start, int minutes, AppointmentState state, bool walkIn)
            {
                var id = Det(tenant, $"appointment:{t.CalendarId}:{key}");
                var patientId = NextPatient(); // advance even when skipped, so a re-run gives each visit the same patient
                if (!have.Add(id)) return;
                var a = new Appointment
                {
                    Id = id, TenantId = tenant, FacilityId = facility, CalendarId = t.CalendarId, CalendarKind = CalendarKind.Doctor, PatientId = patientId,
                    VisitType = VisitType.FollowUp, Start = start, End = start.AddMinutes(minutes), State = state, ReasonCategory = ReasonCategory.General,
                    Channel = walkIn ? BookingChannel.InPerson : BookingChannel.Phone, WalkIn = walkIn, Unscheduled = walkIn, BookedAt = now, ChangedAt = now,
                };
                db.Appointments.Add(a);
                if (!walkIn) db.ScheduleSlots.AddRange(ScheduleSlot.For(a));
                db.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = tenant, AppointmentId = id, State = AppointmentState.Booked, OccurredAt = now });
                added++;
                if (state is not (AppointmentState.CheckedIn or AppointmentState.WithNurse)) return;
                db.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = tenant, AppointmentId = id, State = AppointmentState.CheckedIn, OccurredAt = now });
                db.QueueTokens.Add(new QueueToken
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant, FacilityId = facility, Date = today, CalendarId = t.CalendarId, Prefix = prefix, Number = ++number,
                    AppointmentId = id, IssuedAt = now.AddMinutes(-5 * (7 - number)),
                });
            }

            var future = Enumerable.Range(1, 10).Select(today.AddDays).Where(Works).Take(3).ToList();
            for (var i = 0; i < future.Count; i++) Add($"future{future[i]:yyyyMMdd}", SlotStart(future[i], 2 + i), t.SlotMinutes, AppointmentState.Booked, false);
            if (!Works(today)) continue;
            Add($"today0-{today:yyyyMMdd}", SlotStart(today, 0), t.SlotMinutes, AppointmentState.CheckedIn, false);
            Add($"today1-{today:yyyyMMdd}", SlotStart(today, 1), t.SlotMinutes, AppointmentState.CheckedIn, false);
            Add($"today2-{today:yyyyMMdd}", SlotStart(today, 2), t.SlotMinutes, AppointmentState.WithNurse, false);
            Add($"today3-{today:yyyyMMdd}", SlotStart(today, 3), t.SlotMinutes, AppointmentState.Booked, false);
            Add($"walkin0-{today:yyyyMMdd}", now.AddMinutes(-20), 0, AppointmentState.CheckedIn, true);
            Add($"walkin1-{today:yyyyMMdd}", now.AddMinutes(-10), 0, AppointmentState.CheckedIn, true);
        }
        await db.SaveChangesAsync(ct);
        return added;
    }
}
