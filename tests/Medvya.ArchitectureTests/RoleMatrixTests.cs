using System.Text.Json;
using Medvya.Tenancy.Domain;

namespace Medvya.ArchitectureTests;

public class RoleMatrixTests
{
    static readonly Dictionary<string, string> Codes = new()
    {
        ["Full"] = "F", ["Read"] = "R", ["Own"] = "O", ["Limited"] = "L", ["Released"] = "P",
        ["Sign"] = "S", ["Config"] = "C", ["Break-glass"] = "B", ["—"] = "X",
    };

    [Fact]
    public void MatrixMatchesFoundationsCellByCell()
    {
        // Design data extracted from the Foundations design file; the server must enforce exactly this.
        var path = Path.Combine(AppContext.BaseDirectory, "foundations.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var design = doc.RootElement.GetProperty("matrix").EnumerateArray()
            .Select(r => (Label: r.GetProperty("cap").GetString(),
                          Codes: string.Join(' ', r.GetProperty("cells").EnumerateArray().Select(c => Codes[c.GetProperty("v").GetString()!]))))
            .ToList();

        Assert.Equal(design, RoleMatrix.Rows.Select(r => (Label: (string?)r.Label, r.Codes)).ToList());
        Assert.Equal(Enum.GetValues<Capability>().Length, RoleMatrix.Rows.Count);
        Assert.Equal(["Solo Dr", "Hosp. Dr", "Reception", "Nurse", "Care coord.", "Patient", "Pract. admin", "Hosp. IT"],
            doc.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
        Assert.Equal(8, Enum.GetValues<Role>().Length);
    }

    [Fact]
    public void ReceptionistsNeverSeeClinicalContent()
    {
        foreach (var c in new[] { Capability.Patient360Chart, Capability.ClinicalNotes, Capability.PrescriptionsOrders, Capability.AiDraftsEvidence, Capability.ResultReviewAcknowledge })
            Assert.Equal(Access.None, RoleMatrix.Of(Role.Receptionist, c));
        Assert.Equal(Access.None, RoleMatrix.Of(Role.Patient, Capability.AiDraftsEvidence)); // patients never see unreviewed AI output
        Assert.Equal(Access.BreakGlass, RoleMatrix.Of(Role.HospitalIt, Capability.Patient360Chart));
    }
}
