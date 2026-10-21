namespace Medvya.Tenancy.Domain;

public enum Role { SoloDoctor, HospitalDoctor, Receptionist, Nurse, CareCoordinator, Patient, PracticeAdmin, HospitalIt }

public enum Capability
{
    RegisterBookPatients, CheckInQueue, InvoicesPayments, Patient360Chart, VitalsAllergiesEntry, ClinicalNotes,
    PrescriptionsOrders, RecordingConsentCapture, AiDraftsEvidence, ResultReviewAcknowledge, CareTasksPatientContact,
    ReleaseRecordsToPortal, DevicePairingFleet, StaffRolesIntegrations, AuditLog,
}

/// <summary>
/// None: no access. Read: view. Own: own records only. Limited: subset defined per capability. Released: only items released to the patient.
/// Full: create/update. Sign: full plus clinical sign-off. Config: administer. BreakGlass: emergency read with justification and review.
/// </summary>
public enum Access { None, Read, Own, Limited, Released, Full, Sign, Config, BreakGlass }

/// <summary>
/// Role-permission matrix from Foundations §06 (docs/02-design/sitemap-and-permissions.md), checked server-side on every endpoint (PRD v2 P0-05).
/// Rows keep the Foundations wording and codes so the test can compare them cell by cell with the design data.
/// Role alone never grants another facility, patient relationship or purpose; those checks come on top (MDV-P2-024).
/// </summary>
public static class RoleMatrix
{
    // Column order: SoloDoctor HospitalDoctor Receptionist Nurse CareCoordinator Patient PracticeAdmin HospitalIt.
    // F Full · R Read · O Own · L Limited · P Released · S Sign · C Config · B Break-glass · X None
    public static readonly IReadOnlyList<(Capability Capability, string Label, string Codes)> Rows =
    [
        (Capability.RegisterBookPatients, "Register / book patients", "F F F R R O F X"),
        (Capability.CheckInQueue, "Check-in & queue", "F R F F R X R X"),
        (Capability.InvoicesPayments, "Invoices & payments", "F X F X X O F X"),
        (Capability.Patient360Chart, "Patient 360 chart", "F F X L L P X B"),
        (Capability.VitalsAllergiesEntry, "Vitals & allergies entry", "F F X F X X X X"),
        (Capability.ClinicalNotes, "Clinical notes", "S S X R R P X X"),
        (Capability.PrescriptionsOrders, "Prescriptions & orders", "S S X X X P X X"),
        (Capability.RecordingConsentCapture, "Recording consent & capture", "F F X L X O X X"),
        (Capability.AiDraftsEvidence, "AI drafts & evidence", "F F X X X X X X"),
        (Capability.ResultReviewAcknowledge, "Result review / acknowledge", "S S X X R X X X"),
        (Capability.CareTasksPatientContact, "Care tasks & patient contact", "F F X F F X X X"),
        (Capability.ReleaseRecordsToPortal, "Release records to portal", "F F X X L O X X"),
        (Capability.DevicePairingFleet, "Device pairing & fleet", "O O X O X X C C"),
        (Capability.StaffRolesIntegrations, "Staff, roles, integrations", "C X X X X X C C"),
        (Capability.AuditLog, "Audit log", "R X X X X X R F"),
    ];

    static readonly Dictionary<(Role, Capability), Access> Table = Rows
        .SelectMany(r => r.Codes.Split(' ').Select((code, i) => (Key: ((Role)i, r.Capability), Value: Parse(code))))
        .ToDictionary(x => x.Key, x => x.Value);

    public static Access Of(Role role, Capability capability) => Table[(role, capability)];

    static Access Parse(string code) => code switch
    {
        "F" => Access.Full, "R" => Access.Read, "O" => Access.Own, "L" => Access.Limited, "P" => Access.Released,
        "S" => Access.Sign, "C" => Access.Config, "B" => Access.BreakGlass, "X" => Access.None,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown access code"),
    };
}
