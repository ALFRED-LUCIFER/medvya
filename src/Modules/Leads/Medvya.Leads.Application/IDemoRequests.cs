using Medvya.Leads.Domain;

namespace Medvya.Leads.Application;

/// <summary>Stores a validated demo request and tells the founder by email (MDV-P1-003).</summary>
public interface IDemoRequests
{
    /// <summary>The request is stored first; a failed email is logged and does not lose it.</summary>
    Task SubmitAsync(DemoRequest request, CancellationToken cancellationToken = default);
}
