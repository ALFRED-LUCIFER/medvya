using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure;

public sealed class PatientContacts(PatientsDbContext db) : IPatientContacts
{
    public Task<PatientContact?> FindAsync(Guid patientId, CancellationToken cancellationToken = default) =>
        db.Patients.AsNoTracking().Where(p => p.Id == patientId)
            .Select(p => new PatientContact(p.Mobile, p.Email, p.PreferredLanguage, p.CountryCode)).SingleOrDefaultAsync(cancellationToken);
}
