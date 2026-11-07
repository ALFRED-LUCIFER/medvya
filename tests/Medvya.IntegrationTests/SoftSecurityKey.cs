using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Medvya.IntegrationTests;

/// <summary>
/// A software WebAuthn authenticator for tests (MDV-P5-040): a P-256 key that answers creation and request options exactly as a browser
/// would serialise a hardware key's answer ("none" attestation). Its flags can be turned off one by one to prove the server checks them.
/// </summary>
public sealed class SoftSecurityKey(string origin, string rpId = "localhost") : IDisposable
{
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    public uint Counter { get; set; }
    /// <summary>User verification (PIN or biometrics on the key).</summary>
    public bool Verifies { get; set; } = true;
    /// <summary>A synced passkey reports itself backup eligible.</summary>
    public bool BackupEligible { get; set; }

    const byte UserPresent = 0x01, UserVerified = 0x04, BackupEligibleFlag = 0x08, AttestedData = 0x40;

    public void Dispose() => key.Dispose();

    /// <summary>The COSE public key, as the server stores it.</summary>
    public byte[] CosePublicKey()
    {
        var p = key.ExportParameters(false);
        var w = new CborWriter(CborConformanceMode.Ctap2Canonical);
        w.WriteStartMap(5);
        w.WriteInt32(1); w.WriteInt32(2);   // kty: EC2
        w.WriteInt32(3); w.WriteInt32(-7);  // alg: ES256
        w.WriteInt32(-1); w.WriteInt32(1);  // crv: P-256
        w.WriteInt32(-2); w.WriteByteString(p.Q.X!);
        w.WriteInt32(-3); w.WriteByteString(p.Q.Y!);
        w.WriteEndMap();
        return w.Encode();
    }

    /// <summary>The PublicKeyCredential JSON for navigator.credentials.create() with these creation options.</summary>
    public string Create(string optionsJson)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());
        var credentialData = new byte[16 + 2 + CredentialId.Length];
        credentialData[16] = (byte)(CredentialId.Length >> 8);
        credentialData[17] = (byte)CredentialId.Length;
        CredentialId.CopyTo(credentialData, 18);
        var authData = AuthData(AttestedData, [.. credentialData, .. CosePublicKey()]);
        var att = new CborWriter(CborConformanceMode.Ctap2Canonical);
        att.WriteStartMap(3);
        att.WriteTextString("fmt"); att.WriteTextString("none");
        att.WriteTextString("attStmt"); att.WriteStartMap(0); att.WriteEndMap();
        att.WriteTextString("authData"); att.WriteByteString(authData);
        att.WriteEndMap();
        return Credential(new JsonObject
        {
            ["attestationObject"] = B64(att.Encode()),
            ["clientDataJSON"] = B64(clientData),
            ["transports"] = new JsonArray("usb"),
        });
    }

    /// <summary>The PublicKeyCredential JSON for navigator.credentials.get() with these request options.</summary>
    public string Get(string optionsJson, Guid userHandle)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>());
        Counter++;
        var authData = AuthData(0, []);
        var signature = key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return Credential(new JsonObject
        {
            ["authenticatorData"] = B64(authData),
            ["clientDataJSON"] = B64(clientData),
            ["signature"] = B64(signature),
            ["userHandle"] = B64(userHandle.ToByteArray()),
        });
    }

    byte[] AuthData(byte flags, byte[] rest)
    {
        flags |= UserPresent;
        if (Verifies) flags |= UserVerified;
        if (BackupEligible) flags |= BackupEligibleFlag;
        byte[] counter = [(byte)(Counter >> 24), (byte)(Counter >> 16), (byte)(Counter >> 8), (byte)Counter];
        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), flags, .. counter, .. rest];
    }

    byte[] ClientData(string type, string challenge) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });

    string Credential(JsonObject response) => new JsonObject
    {
        ["id"] = B64(CredentialId),
        ["rawId"] = B64(CredentialId),
        ["type"] = "public-key",
        ["response"] = response,
        ["clientExtensionResults"] = new JsonObject(),
    }.ToJsonString();

    static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
