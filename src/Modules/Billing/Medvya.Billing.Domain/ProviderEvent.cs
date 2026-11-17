using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Domain;

/// <summary>
/// A payment provider's webhook event that has been handled (MDV-P4-007, ADR-023 §6): delivery is at least once, so a replay of the same event id is
/// answered without acting again. Written only after the event was acted on, so a failed attempt is redone by the provider's retry.
/// </summary>
public sealed class ProviderEvent
{
    public const int MaxEventId = 64, MaxType = 64;

    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>"razorpay".</summary>
    [Classification(DataClass.Internal)] public required string Provider { get; init; }
    /// <summary>The provider's event id (Razorpay's x-razorpay-event-id header).</summary>
    [Classification(DataClass.Internal)] public required string EventId { get; init; }
    /// <summary>The event type, e.g. payment_link.paid.</summary>
    [Classification(DataClass.Internal)] public required string Type { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ReceivedAt { get; init; }
}
