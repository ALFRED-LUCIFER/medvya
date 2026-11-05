namespace Medvya.Patients.Application;

/// <summary>Where and in which language to reach a patient. Personal data: only the notification sender reads it, at send time, and never logs it.</summary>
public sealed record PatientContact(string? Mobile, string? Email, string? PreferredLanguage, string CountryCode);

/// <summary>Contact details for appointment messages (MDV-P1-032/033). Tenant filters and row-level security apply.</summary>
public interface IPatientContacts
{
    Task<PatientContact?> FindAsync(Guid patientId, CancellationToken cancellationToken = default);
}
