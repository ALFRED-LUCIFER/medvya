using System.Data.Common;

namespace Medvya.Tenancy.Application;

/// <summary>A doctor's (or the clinic's) new-visit and follow-up fees in minor units; null where no price exists.</summary>
public sealed record ConsultationFees(long? NewVisitMinor, long? FollowUpMinor);

/// <summary>
/// Doctor fees for the doctor screens and onboarding (MDV-P4-002): the prices of the two consultation services in the Billing catalogue, their only store.
/// Tenancy owns this port and Billing implements it, so Tenancy (and the platform console that loads it) references no Billing project.
/// Writes run on the caller's connection and transaction, so a doctor is never saved with a fee that failed to save. Not behind the billing flag:
/// fees exist before a clinic switches billing on.
/// </summary>
public interface IDoctorFees
{
    /// <summary>The fees in effect now for each doctor (membership id): the doctor's own price, else the clinic default.</summary>
    Task<IReadOnlyDictionary<Guid, ConsultationFees>> ForDoctorsAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken = default);

    /// <summary>The clinic default fees in effect now.</summary>
    Task<ConsultationFees> DefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the fees from now, for <paramref name="doctorId"/> or (null) as the clinic default, creating a consultation service that does not exist yet.
    /// Amounts are minor units of <paramref name="countryCode"/>'s currency, already validated by the caller.
    /// </summary>
    Task SetAsync(Guid? doctorId, string countryCode, long newVisitMinor, long followUpMinor, DbTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Gives <paramref name="toDoctorId"/> (the same doctor at another location) the fees <paramref name="fromDoctorId"/> has now, unless it has its own.</summary>
    Task CopyAsync(Guid fromDoctorId, Guid toDoctorId, DbTransaction transaction, CancellationToken cancellationToken = default);
}
