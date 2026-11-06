using System.Globalization;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Creates a practice (MDV-P1-004). The new tenant's rows are written on a connection that already carries the new tenant's
/// session tenant setting, so row-level security (layer 2) accepts them exactly as it does for any tenant's own writes; it is never switched off.
/// </summary>
public sealed class PracticeProvisioning(string connectionString, ICurrentUser user, IAuditLog audit)
    : IPracticeProvisioning
{
    sealed class NewTenant(Guid id) : ICurrentTenant { public Guid? TenantId => id; }

    public async Task<ProvisionedPractice> CreateAsync(string country, OnboardingData data, CancellationToken cancellationToken = default)
    {
        var rules = OnboardingRules.For(country) ?? throw new ArgumentException("Unsupported country.", nameof(country));
        if (rules.ValidateAll(data).Count > 0) throw new ArgumentException("The onboarding data is not valid.", nameof(data));
        var owner = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var clinic = data.Clinic!;
        var role = data.Profile!.Mode == OnboardingRules.Solo ? Role.SoloDoctor : Role.PracticeAdmin;
        var now = DateTimeOffset.UtcNow;

        var tenantId = Guid.CreateVersion7();
        var tenant = new NewTenant(tenantId);
        await using var db = new TenancyDbContext(
            new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(new TenantSessionInterceptor(tenant)).Options, tenant);

        var facilityId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        var hours = data.Hours!;
        db.Tenants.Add(new Tenant { Id = tenantId, Name = clinic.Name!.Trim(), CountryCode = country, ResidencyRegion = ResidencyRegion(country) });
        db.Facilities.Add(new Facility { Id = facilityId, TenantId = tenantId, Name = clinic.Name!.Trim(), TimeZone = rules.TimeZone });
        db.Memberships.Add(new Membership { Id = membershipId, TenantId = tenantId, UserId = owner, FacilityId = facilityId, Role = role, Status = MembershipStatus.Active });
        db.Profiles.Add(new PracticeProfile
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Address = clinic.Address!.Trim(), RegistrationNumber = Clean(clinic.RegistrationNumber),
            TaxNumber = Clean(clinic.TaxId), ChangedBy = owner, ChangedAt = now,
        });
        db.PracticeDefaultsSet.Add(new PracticeDefaults
        {
            TenantId = tenantId, Specialty = Clean(data.Profile.Specialty), Qualifications = Clean(data.Profile.Qualifications), Languages = Clean(data.Profile.Languages),
            SlotMinutes = hours.SlotMinutes!.Value, NewVisitFeeMinor = hours.NewVisitFee!.Value, FollowUpFeeMinor = hours.FollowUpFee!.Value, Currency = rules.Currency,
            WeekdayHours = Windows(hours.Weekdays), SaturdayHours = Windows(hours.Saturday), NoteTemplates = JsonSerializer.Serialize(data.Templates ?? []),
        });
        // Opening hours of settings are one span a day: from the first window's start to the last window's end (the split stays in the defaults).
        foreach (var (days, windows) in new[] { (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, hours.Weekdays), ([DayOfWeek.Saturday], hours.Saturday) })
        {
            if (windows is not { Count: > 0 }) continue;
            var opens = windows.Min(w => TimeOnly.ParseExact(w.Start!, "HH:mm", CultureInfo.InvariantCulture));
            var closes = windows.Max(w => TimeOnly.ParseExact(w.End!, "HH:mm", CultureInfo.InvariantCulture));
            foreach (var day in days)
                db.OperatingHours.Add(new OperatingHours { Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = facilityId, Weekday = day, Opens = opens, Closes = closes });
        }
        // Only a doctor submits a licence; their recording and signing stay off until it is verified (LicenceRules).
        if (role == Role.SoloDoctor)
        {
            db.DoctorLicences.Add(new DoctorLicence
            {
                MembershipId = membershipId, TenantId = tenantId, Authority = data.Licence!.Authority!, Number = data.Licence.Number!.Trim(),
                CertificateName = Clean(data.Licence.CertificateName), SubmittedAt = now,
            });
            // The owner doctor is listed on Settings, Doctors beside the doctors added later (MDV-P1-007).
            db.DoctorProfiles.Add(new DoctorProfile
            {
                MembershipId = membershipId, TenantId = tenantId, FullName = data.Profile.FullName!.Trim(), Specialty = Clean(data.Profile.Specialty),
                Qualifications = Clean(data.Profile.Qualifications), Languages = Clean(data.Profile.Languages),
                NewVisitFeeMinor = hours.NewVisitFee!.Value, FollowUpFeeMinor = hours.FollowUpFee!.Value, ChangedBy = owner, ChangedAt = now,
            });
        }

        // The audit row commits with the practice (fail closed). The country choice is part of the action code: opaque, no free text.
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEvent
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = owner, Action = $"onboarding.complete.{country}", ResourceType = "tenant",
                ResourceId = tenantId, Purpose = "support", Outcome = AuditOutcome.Allowed, OccurredAt = now,
            }, tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        return new ProvisionedPractice(tenantId, facilityId, membershipId, role);
    }

    // The data plane the tenant is bound to (ADR-003); the region names follow the Azure regions the selector shows.
    static string ResidencyRegion(string country) => country == "IN" ? "centralindia" : "uaenorth";

    static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    static string Windows(IReadOnlyList<HoursWindow>? w) => JsonSerializer.Serialize((w ?? []).Select(x => new { start = x.Start, end = x.End }));
}
