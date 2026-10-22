using Medvya.Leads.Domain;

namespace Medvya.Leads.Application;

/// <summary>Stores interest in a country whose deployment is not live yet (MDV-P1-002).</summary>
public interface IWaitlist
{
    Task JoinAsync(WaitlistEntry entry, CancellationToken cancellationToken = default);
}
