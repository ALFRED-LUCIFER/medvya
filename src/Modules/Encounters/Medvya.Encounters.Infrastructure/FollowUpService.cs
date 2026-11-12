using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The plan's follow-up (MDV-P2-043). Clinical access first (a doctor, care relationship, the encounter's facility); the choice is kept by the
/// Scheduling module as a waitlist entry, written on this transaction with the audit event (the period code only), so the two commit together.
/// </summary>
public sealed class FollowUpService(
    EncountersDbContext db, IFollowUpBookings bookings, IClinicalAccess clinical, IFacilityDirectory facilities, ICurrentTenant tenant, ICurrentUser user,
    IAuditLog audit, TimeProvider clock) : IFollowUps
{
    public const string ReadAction = "encounter.followup.read", SetAction = "encounter.followup.set", Resource = "encounter";
    static readonly Access[] Doctors = [Access.Full];

    public async Task<FollowUpView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ReadAction, encounter, null), cancellationToken);
        return View(await bookings.FindAsync(encounter.Id, cancellationToken));
    }

    public async Task<FollowUpView?> SetAsync(Guid encounterId, SetFollowUp command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        if (encounter.Status == EncounterStatus.Ended) throw new VisitTransitionException("ended");
        DateOnly? dueOn = null;
        if (command.Period is { } period)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(encounter.FacilityId, cancellationToken) ?? throw new InvalidOperationException("Visit facility not in this tenant.")).TimeZone);
            dueOn = FollowUpRules.DueOn(ScheduleRules.LocalDate(clock.GetUtcNow(), zone), period) ?? throw new FollowUpValidationException();
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var booking = await bookings.SetAsync(encounter.AppointmentId, encounter.Id, command.Period, dueOn, tx.GetDbTransaction(), cancellationToken);
        await audit.WriteAsync(Event(SetAction, encounter, $"period:{command.Period ?? "none"}"), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(booking);
    }

    static FollowUpView View(FollowUpBooking? b) => new(b?.Period, b?.DueOn, b?.State.ToString());

    async Task<Encounter?> FindAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Doctors, cancellationToken);
        return encounter;
    }

    AuditEvent Event(string action, Encounter e, string? detail) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), ActorUserId = user.UserId, Action = action,
        ResourceType = Resource, ResourceId = e.Id, PatientId = e.PatientId, FacilityId = e.FacilityId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed,
        OccurredAt = clock.GetUtcNow(), Detail = detail,
    };
}
