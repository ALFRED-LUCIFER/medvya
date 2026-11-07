using System.Buffers.Text;
using System.Security.Cryptography;
using Medvya.Tenancy.Domain;

namespace Medvya.ArchitectureTests;

/// <summary>MDV-P0-013: invitation tokens are random (≥128 bits), carry their tenant, and only their hash is stored.</summary>
public class InvitationRulesTests
{
    static readonly Guid Tenant = Guid.Parse("5e1d0001-0000-4000-8000-000000000001");

    [Fact]
    public void TokensCarry256RandomBitsAndTheirTenant()
    {
        var (a, hashA) = InvitationToken.New(Tenant);
        var (b, _) = InvitationToken.New(Tenant);
        var bytes = Base64Url.DecodeFromChars(a);

        Assert.Equal(48, bytes.Length); // 16 tenant + 32 random bytes
        Assert.NotEqual(a, b);
        Assert.NotEqual(bytes[16..], Base64Url.DecodeFromChars(b)[16..]);
        var read = InvitationToken.Read(a)!.Value;
        Assert.Equal(Tenant, read.TenantId);
        Assert.Equal(hashA, read.Hash);
        Assert.Equal(SHA256.HashData(bytes), hashA); // the stored value is the hash, never the token
    }

    [Fact]
    public void ChangingAnyPartOfATokenChangesItsHash()
    {
        var (token, hash) = InvitationToken.New(Tenant);
        var bytes = Base64Url.DecodeFromChars(token);
        bytes[0] ^= 1; // another tenant, same random part
        Assert.NotEqual(hash, InvitationToken.Read(Base64Url.EncodeToString(bytes))!.Value.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("AAAA")]
    public void MalformedTokensAreRejected(string? token) => Assert.Null(InvitationToken.Read(token));

    [Fact]
    public void OverlongTokensAreRejected() =>
        Assert.Null(InvitationToken.Read(Base64Url.EncodeToString(new byte[60])));

    [Theory]
    // #875: an inviter cannot create an account that reaches patient data they cannot reach themselves.
    [InlineData(Role.HospitalIt, Role.PracticeAdmin, false)]
    [InlineData(Role.HospitalIt, Role.Receptionist, false)]
    [InlineData(Role.HospitalIt, Role.HospitalIt, true)]
    [InlineData(Role.PracticeAdmin, Role.Receptionist, true)]
    [InlineData(Role.PracticeAdmin, Role.Nurse, true)]
    [InlineData(Role.PracticeAdmin, Role.HospitalDoctor, true)]
    [InlineData(Role.SoloDoctor, Role.PracticeAdmin, true)]
    public void NobodyGrantsPatientAccessTheyLack(Role granter, Role grantee, bool allowed) =>
        Assert.Equal(allowed, InvitationRules.CanGrant(c => RoleMatrix.Of(granter, c), c => RoleMatrix.Of(grantee, c)));

    [Theory]
    [InlineData("Iyer Family Clinic (synthetic)", false)]
    [InlineData("Dr. Mehta's Clinic", false)]
    [InlineData("Sharma & Co. Clinic", false)]
    [InlineData("Visit www.example-clinic.test now", true)]
    [InlineData("Clinic https://x.test", true)]
    [InlineData("Reset at secure-login.com", true)]
    public void NamesSentInEmailsCarryNoLinks(string name, bool hasLink) => Assert.Equal(hasLink, OutboundText.HasLink(name));

    [Fact]
    public void DoctorsAreFlaggedForLicenceVerification() =>
        Assert.Equal([Role.SoloDoctor, Role.HospitalDoctor], Enum.GetValues<Role>().Where(InvitationRules.RequiresLicenceVerification));
}
