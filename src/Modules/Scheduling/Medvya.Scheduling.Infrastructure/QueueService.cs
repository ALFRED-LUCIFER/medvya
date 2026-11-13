using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Waiting-room TVs (MDV-P1-031). A display is created by staff who may configure the facility, with a token that carries the
/// tenant and 32 random bytes (the invitation-link format); only its SHA-256 hash is stored, and revoking ends it at its next
/// request. The TV reads its facility's queue through its own session: tokens, rooms, doctor display names and waits only.
/// shortcut: a wait is the token's place in its doctor's queue times the doctor's slot length; use the running-delay estimate
/// of the clinic overview (MDV-P1-030) when it exists.
/// </summary>
public sealed class QueueService(SchedulingDbContext db, SchedulingContext context, ICurrentFacility facility, QueueOrdering ordering) : IQueueService
{
    const int MaxName = 60, MaxNext = 8, DefaultSlotMinutes = 15;

    static readonly AppointmentState[] Waiting = [AppointmentState.CheckedIn, AppointmentState.WithNurse, AppointmentState.ReadyForDoctor];

    public async Task<QueueTv> TvAsync(CancellationToken cancellationToken = default)
    {
        var facilityId = facility.FacilityId ?? throw new SchedulingAccessDeniedException();
        var (info, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var date = ScheduleRules.LocalDate(context.Now, zone);
        var queue = (await (
                from t in db.QueueTokens.AsNoTracking()
                join a in db.Appointments.AsNoTracking() on t.AppointmentId equals a.Id
                where t.FacilityId == facilityId && t.Date == date && (Waiting.Contains(a.State) || a.State == AppointmentState.InConsult)
                select new { Visit = a, t.Prefix, t.Number })
            .ToListAsync(cancellationToken)).ConvertAll(q => (q.Visit, Token: QueueToken.Format(q.Prefix, q.Number)));
        var names = (await context.CalendarsAsync(facilityId, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var slots = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId)
            .ToDictionaryAsync(t => t.CalendarId, t => t.SlotMinutes, cancellationToken);
        var places = await ordering.PlacesAsync(queue.Select(q => q.Visit), info.Queue, cancellationToken);

        // Now: the latest call-in of each doctor, newest first.
        var now = queue.Where(q => q.Visit.State == AppointmentState.InConsult).GroupBy(q => q.Visit.CalendarId)
            .Select(g => g.MaxBy(q => q.Visit.ChangedAt)).OrderByDescending(q => q.Visit.ChangedAt)
            .Select(q => new QueueTvNow(q.Token, q.Visit.Room, names.GetValueOrDefault(q.Visit.CalendarId, ""))).ToList();
        // Next: each doctor's waiting tokens in the clinic's queue order (MDV-P1-103), after whoever is with the doctor now; each names its
        // doctor, since a reassigned token keeps its letter.
        var next = queue.Where(q => places.ContainsKey(q.Visit.Id)).GroupBy(q => q.Visit.CalendarId).SelectMany(g =>
            {
                var busy = queue.Any(q => q.Visit.CalendarId == g.Key && q.Visit.State == AppointmentState.InConsult) ? 1 : 0;
                var minutes = slots.GetValueOrDefault(g.Key, DefaultSlotMinutes);
                return g.OrderBy(q => places[q.Visit.Id]).Select((q, place) =>
                    (Place: places[q.Visit.Id], Next: new QueueTvNext(q.Token, RoundUp((place + busy) * minutes), names.GetValueOrDefault(g.Key, ""))));
            })
            .OrderBy(n => n.Next.WaitMinutes).ThenBy(n => n.Place).Take(MaxNext).Select(n => n.Next).ToList();
        return new(now, next);
    }

    // "≈ 0 min" reads as wrong on a TV: the shortest estimate is 5 minutes, and estimates step by 5.
    static int RoundUp(int minutes) => Math.Max(5, (minutes + 4) / 5 * 5);

    public async Task<IReadOnlyList<DisplayView>> ListDisplaysAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanConfigure, cancellationToken);
        return await db.QueueDisplays.AsNoTracking().Where(d => d.FacilityId == facilityId).OrderBy(d => d.RevokedAt != null).ThenBy(d => d.CreatedAt)
            .Select(d => new DisplayView(d.Id, d.FacilityId, d.Name, d.CreatedAt, d.RevokedAt)).ToListAsync(cancellationToken);
    }

    public async Task<CreatedDisplay> CreateDisplayAsync(CreateDisplay command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = ScheduleService.Clean(command.Name);
        var errors = new List<ValidationError>();
        if (name is null) errors.Add(new("name", "required"));
        else if (name.Length > MaxName) errors.Add(new("name", "too_long"));
        SchedulingValidationException.ThrowIfAny(errors);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);

        var (token, hash) = InvitationToken.New(context.TenantId);
        var display = new QueueDisplay
        {
            Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, Name = name!, TokenHash = hash,
            CreatedBy = context.UserId, CreatedAt = context.Now,
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.QueueDisplays.Add(display);
        await db.SaveChangesAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.DisplayCreate, SchedulingAudit.DisplayResource, SchedulingAudit.Operations, display.Id, null, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(new(display.Id, display.FacilityId, display.Name, display.CreatedAt, null), token);
    }

    public async Task RevokeDisplayAsync(Guid displayId, CancellationToken cancellationToken = default)
    {
        var display = await db.QueueDisplays.AsNoTracking().SingleOrDefaultAsync(d => d.Id == displayId, cancellationToken) ?? throw new DisplayNotFoundException();
        await context.RequireAsync(display.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var (now, me) = (context.Now, context.UserId);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.QueueDisplays.Where(d => d.Id == displayId && d.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevokedAt, now).SetProperty(d => d.RevokedBy, me), cancellationToken);
        await context.AuditAsync(SchedulingAudit.DisplayRevoke, SchedulingAudit.DisplayResource, SchedulingAudit.Operations, displayId, null, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public Task<DisplaySession?> FindDisplayAsync(byte[] tokenHash, CancellationToken cancellationToken = default) =>
        db.QueueDisplays.AsNoTracking().Where(d => d.TokenHash == tokenHash && d.RevokedAt == null)
            .Select(d => new DisplaySession(d.Id, d.FacilityId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<DisplaySession?> PairDisplayAsync(byte[] linkHash, byte[] screenHash, CancellationToken cancellationToken = default)
    {
        if (await FindDisplayAsync(linkHash, cancellationToken) is not { } display) return null;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // Conditional on the link's hash: of two TVs pairing with one link at once, only one gets a screen secret.
        if (await db.QueueDisplays.Where(d => d.Id == display.DisplayId && d.TokenHash == linkHash && d.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.TokenHash, screenHash), cancellationToken) != 1) return null;
        await context.AuditAsync(SchedulingAudit.DisplayPair, SchedulingAudit.DisplayResource, SchedulingAudit.Operations, display.DisplayId, null, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return display;
    }
}
