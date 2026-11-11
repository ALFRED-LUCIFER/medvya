using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

/// <summary>A server-made paper as handed out: the PDF bytes and their SHA-256 (the stored copy's).</summary>
public sealed record PaperPdf(byte[] Content, string Sha256);

/// <summary>What the public QR check of a slip or letter answers: the kind, when, from which clinic and doctor, and how many items. Never the patient or what it lists.</summary>
public sealed record PaperVerifyView(PaperKind Kind, DateTimeOffset IssuedAt, string TimeZone, string Clinic, string Doctor, int Items);

/// <summary>The public QR check of an investigation slip or referral letter (MDV-P2-018/019): runs inside the token's tenant; unknown and malformed alike are null. Audited with the patient.</summary>
public interface IPaperVerification
{
    Task<PaperVerifyView?> VerifyAsync(string verifyCode, CancellationToken cancellationToken = default);
}
