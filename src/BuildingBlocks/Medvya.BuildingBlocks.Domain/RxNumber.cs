namespace Medvya.BuildingBlocks.Domain;

/// <summary>
/// The number printed on a prescription ("RX-" and eight letters of the authorised version's id): a reference to read out, not a credential.
/// Shared so the signed note's print (Encounters, MDV-P2-049) shows the same number as the prescription's paper (Prescribing).
/// </summary>
public static class RxNumber
{
    public static string Of(Guid authorisedPrescriptionId) => "RX-" + authorisedPrescriptionId.ToString("N")[^8..].ToUpperInvariant();
}
