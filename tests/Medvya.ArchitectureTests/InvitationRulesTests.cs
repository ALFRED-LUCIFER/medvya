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

    [Fact]
    public void PatientsCannotBeInvitedAsStaff()
    {
        Assert.False(InvitationRules.CanInvite(Role.Patient));
        Assert.False(InvitationRules.CanInvite((Role)99));
        Assert.All(Enum.GetValues<Role>().Where(r => r != Role.Patient), r => Assert.True(InvitationRules.CanInvite(r)));
    }

    [Fact]
    public void DoctorsAreFlaggedForLicenceVerification() =>
        Assert.Equal([Role.SoloDoctor, Role.HospitalDoctor], Enum.GetValues<Role>().Where(InvitationRules.RequiresLicenceVerification));
}
