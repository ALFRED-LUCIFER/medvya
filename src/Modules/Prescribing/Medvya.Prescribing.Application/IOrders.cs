using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

public sealed record OrderView(Guid Id, OrderKind Kind, string Name, string? Note, OrderState State, string? Destination, DateTimeOffset? OrderedAt);

/// <summary>A test the doctor adds to the consultation's draft orders.</summary>
public sealed record AddOrder(OrderKind? Kind, string? Name, string? Note);

/// <summary>Every test of one of the clinic's order sets, added as drafts.</summary>
public sealed record AddOrderSetOrders(Guid? OrderSetId);

public sealed record IncludeOrder(bool? Included);

/// <summary>"Place orders": every included draft becomes Ordered, for the lab named here (free text, optional).</summary>
public sealed record PlaceOrders(string? Destination);

/// <summary>What is printed on the investigation slip: who it is for, who ordered, where, and the tests with their instructions.</summary>
public sealed record SlipPatient(string FullName, string Mrn, DateOnly DateOfBirth, string SexAtBirth);

public sealed record OrderSlip(SlipPatient Patient, string FacilityName, string? DoctorName, DateTimeOffset OrderedAt, string? Destination, IReadOnlyList<OrderView> Orders);

/// <summary>Input refused: fields and codes only, never what was typed.</summary>
public sealed class OrdersValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Order input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The call is not possible now: "encounter_ended", "nothing_to_place", "not_draft" or "already_listed".</summary>
public sealed class OrdersConflictException(string code) : Exception("Orders conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Investigation orders of a consultation (MDV-P2-018). Every call checks clinical access to the encounter's patient (Prescriptions and orders
/// at Sign, care relationship, facility) and writes its audit event with the patient; writes do it in the transaction of the change.
/// Placing is its own call: nothing here is reached from signing a note. A null answer means no such encounter or order in this tenant.
/// </summary>
public interface IOrders
{
    Task<IReadOnlyList<OrderView>?> ListAsync(Guid encounterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>?> AddAsync(Guid encounterId, AddOrder command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>?> AddSetAsync(Guid encounterId, AddOrderSetOrders command, CancellationToken cancellationToken = default);

    /// <summary>Switch a draft off ("not included") or back on; an ordered test cannot be switched.</summary>
    Task<IReadOnlyList<OrderView>?> IncludeAsync(Guid orderId, IncludeOrder command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>?> PlaceAsync(Guid encounterId, PlaceOrders command, CancellationToken cancellationToken = default);

    /// <summary>The placed orders for printing or sharing. Null also when nothing has been placed yet.</summary>
    Task<OrderSlip?> SlipAsync(Guid encounterId, CancellationToken cancellationToken = default);
}
