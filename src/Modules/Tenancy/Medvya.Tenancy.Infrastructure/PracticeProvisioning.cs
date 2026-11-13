using System.Globalization;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Creates a practice (MDV-P1-004). The new tenant's rows are written in a DI scope entered for the new tenant, so its connection carries
/// that tenant's session setting and row-level security (layer 2) accepts them exactly as it does for any tenant's own writes; it is never switched off.
/// The scope's context shares the app's EF model: a context built by hand here cost the first finish on each instance seconds (#205).
/// </summary>
public sealed class PracticeProvisioning(IServiceScopeFactory scopes, ICurrentUser user, IAuditLog audit)
    : IPracticeProvisioning
{
    public async Task<ProvisionedPractice> CreateAsync(string country, OnboardingData data, CancellationToken cancellationToken = default)
    {
        var rules = OnboardingRules.For(country) ?? throw new ArgumentException("Unsupported country.", nameof(country));
        if (rules.ValidateAll(data).Count > 0) throw new ArgumentException("The onboarding data is not valid.", nameof(data));
        var owner = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var clinic = data.Clinic!;
        var role = data.Profile!.Mode == OnboardingRules.Solo ? Role.SoloDoctor : Role.PracticeAdmin;
        var now = DateTimeOffset.UtcNow;

        var tenantId = Guid.CreateVersion7();
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ILinkTenant>().UseLinkTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();

        var facilityId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        var hours = data.Hours!;
        var specialty = OnboardingRules.SpecialtyText(data.Profile);
        db.Tenants.Add(new Tenant { Id = tenantId, Name = clinic.Name!.Trim(), CountryCode = country, ResidencyRegion = ResidencyRegion(country), CreatedAt = now,
            // ADR-016: a new organisation sets itself up as Draft; the owner submits the request and Medvya approves it.
            State = OrganisationState.Draft,
        });
        db.OrganisationRequests.Add(new OrganisationRequest { TenantId = tenantId, Type = Enum.Parse<OrganisationType>(clinic.Type!) });
        db.Facilities.Add(new Facility { Id = facilityId, TenantId = tenantId, Name = clinic.Name!.Trim(), TimeZone = rules.TimeZone });
        db.Memberships.Add(new Membership { Id = membershipId, TenantId = tenantId, UserId = owner, FacilityId = facilityId, Role = role, Status = MembershipStatus.Active, IsOwner = true });
        db.Profiles.Add(new PracticeProfile
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Address = clinic.Address!.Trim(), RegistrationNumber = Clean(clinic.RegistrationNumber),
            TaxNumber = Clean(clinic.TaxId), Email = clinic.ContactEmail!.Trim(), ChangedBy = owner, ChangedAt = now,
        });
        db.PracticeDefaultsSet.Add(new PracticeDefaults
        {
            TenantId = tenantId, Specialty = specialty, Qualifications = Clean(data.Profile.Qualifications), Languages = Clean(data.Profile.Languages),
            SlotMinutes = hours.SlotMinutes!.Value, NewVisitFeeMinor = hours.NewVisitFee!.Value, FollowUpFeeMinor = hours.FollowUpFee!.Value, Currency = rules.Currency,
            WeekdayHours = Windows(hours.Weekdays), SaturdayHours = Windows(hours.Saturday), NoteTemplates = JsonSerializer.Serialize(data.Templates ?? []),
            PatientStart = data.Patients,
        });
        // Opening hours of settings are one span a day: from the first window's start to the last window's end (the split stays in the defaults).
        foreach (var (day, windows) in OnboardingRules.OpenDays(hours))
        {
            var opens = windows.Min(w => TimeOnly.ParseExact(w.Start!, "HH:mm", CultureInfo.InvariantCulture));
            var closes = windows.Max(w => TimeOnly.ParseExact(w.End!, "HH:mm", CultureInfo.InvariantCulture));
            db.OperatingHours.Add(new OperatingHours { Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = facilityId, Weekday = day, Opens = opens, Closes = closes });
        }
        // Only a doctor enters a licence; the owner-doctor attests their own in the wizard (ADR-016).
        if (role == Role.SoloDoctor)
        {
            db.DoctorLicences.Add(new DoctorLicence
            {
                MembershipId = membershipId, TenantId = tenantId, Authority = data.Licence!.Authority!, Number = data.Licence.Number!.Trim(),
                ExpiresOn = data.Licence.ExpiresOn, CertificateName = Clean(data.Licence.CertificateName), SubmittedAt = now, AttestedBy = owner, AttestedAt = now,
            });
            // The values the owner attested, append-only, like Settings, Doctors, Add doctor (ADR-016).
            db.SettingsChanges.Add(new SettingsChange
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = owner, Section = "doctor", ResourceId = membershipId, OccurredAt = now,
                After = JsonSerializer.Serialize(new { authority = data.Licence.Authority, number = data.Licence.Number.Trim(), expiresOn = data.Licence.ExpiresOn, licence = "Attested" }, JsonSerializerOptions.Web),
            });
            // The owner doctor is listed on Settings, Doctors beside the doctors added later (MDV-P1-007).
            db.DoctorProfiles.Add(new DoctorProfile
            {
                MembershipId = membershipId, TenantId = tenantId, FullName = data.Profile.FullName!.Trim(), Specialty = specialty,
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
