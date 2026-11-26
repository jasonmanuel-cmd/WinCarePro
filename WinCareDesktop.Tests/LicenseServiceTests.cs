using System.IO;
using System.Security.Cryptography;
using WinCareDesktop.Services;
using WinCareDesktop.ViewModels;
using Xunit;

namespace WinCareDesktop.Tests;

public class LicenseServiceTests
{
    private static (ECDsa Key, string PublicSpkiBase64) CreateKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        return (ecdsa, pub);
    }

    private static LicensePayload SamplePayload(int major = 2, int seats = 1, string edition = "Founding Pro", string licensee = "alice@example.com", string id = "ORD-1001") =>
        new(id, edition, licensee, major, seats, "2026-11-25T00:00:00Z");

    [Fact]
    public void Default_embedded_public_key_is_valid_nist_p256_spki()
    {
        var bytes = Convert.FromBase64String(LicenseService.DefaultPublicKeySpkiBase64);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(bytes, out var read);
        Assert.Equal(bytes.Length, read);
        Assert.Equal(256, ecdsa.KeySize);
    }

    [Fact]
    public void Valid_signed_license_verifies_offline_and_extracts_payload()
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var payload = SamplePayload();
            var token = LicenseService.SignPayload(payload, priv);
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);

            var res = svc.VerifyKey(token);
            Assert.True(res.IsValid, res.Message);
            Assert.NotNull(res.Payload);
            Assert.Equal("ORD-1001", res.Payload!.LicenseId);
            Assert.Equal("Founding Pro", res.Payload.Edition);
            Assert.Equal("alice@example.com", res.Payload.Licensee);
            Assert.Equal(2, res.Payload.MajorVersion);
            Assert.Equal(1, res.Payload.MaxSeats);
        }
    }

    [Fact]
    public void Line_wrapped_and_whitespace_padded_keys_verify_cleanly()
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var token = LicenseService.SignPayload(SamplePayload(), priv);
            var wrapped = "  \r\n" + token[..25] + "\n  " + token[25..60] + "\r\n\t" + token[60..] + "  ";
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            Assert.True(svc.VerifyKey(wrapped).IsValid);
        }
    }

    [Fact]
    public void Signature_from_different_private_key_is_rejected()
    {
        var (attackerPriv, _) = CreateKeyPair();
        var (realPriv, realPub) = CreateKeyPair();
        using (attackerPriv)
        using (realPriv)
        {
            var forged = LicenseService.SignPayload(SamplePayload(), attackerPriv);
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), realPub);
            var res = svc.VerifyKey(forged);
            Assert.False(res.IsValid);
            Assert.Contains("signature", res.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static IEnumerable<object[]> SignatureByteOffsets() =>
        Enumerable.Range(0, 64).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(SignatureByteOffsets))]
    public void Flipping_any_single_byte_in_64_byte_ecdsa_signature_fails_verification(int byteOffset)
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var token = LicenseService.SignPayload(SamplePayload(), priv);
            var parts = token.Split('.');
            var sig = LicenseService.Base64UrlDecode(parts[2]);
            sig[byteOffset] ^= 0x5A;
            var tampered = $"{parts[0]}.{parts[1]}.{LicenseService.Base64UrlEncode(sig)}";

            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            Assert.False(svc.VerifyKey(tampered).IsValid);
        }
    }

    public static IEnumerable<object[]> PayloadByteOffsets() =>
        Enumerable.Range(0, 24).Select(i => new object[] { i * 3 });

    [Theory]
    [MemberData(nameof(PayloadByteOffsets))]
    public void Modifying_any_payload_byte_invalidates_signature(int offset)
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var token = LicenseService.SignPayload(SamplePayload(), priv);
            var parts = token.Split('.');
            var payloadBytes = LicenseService.Base64UrlDecode(parts[1]);
            payloadBytes[offset % payloadBytes.Length] ^= 0x01;
            var tampered = $"{parts[0]}.{LicenseService.Base64UrlEncode(payloadBytes)}.{parts[2]}";

            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            Assert.False(svc.VerifyKey(tampered).IsValid);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID-KEY")]
    [InlineData("WCP1.abc.def")]
    [InlineData("WCP2.only-two-parts")]
    [InlineData("WCP2.a.b.c")]
    [InlineData("WCP2..sig")]
    [InlineData("WCP2.payload.")]
    [InlineData("WCP2.!!!notbase64!!!.!!!notbase64!!!")]
    [InlineData("WCP2.e30.e30")]
    public void Malformed_tokens_are_rejected_without_throwing(string? badToken)
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            var res = svc.VerifyKey(badToken);
            Assert.False(res.IsValid);
            Assert.False(string.IsNullOrWhiteSpace(res.Message));
        }
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(99)]
    public void Signed_keys_for_other_major_versions_are_rejected(int majorVersion)
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var token = LicenseService.SignPayload(SamplePayload(major: majorVersion), priv);
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            var res = svc.VerifyKey(token);
            Assert.False(res.IsValid);
            Assert.Contains($"v{majorVersion}.x", res.Message);
        }
    }

    [Theory]
    [InlineData("", "Founding Pro", "alice@example.com", 1)]
    [InlineData("   ", "Founding Pro", "alice@example.com", 1)]
    [InlineData("ORD-1", "", "alice@example.com", 1)]
    [InlineData("ORD-1", "Founding Pro", "", 1)]
    [InlineData("ORD-1", "Founding Pro", "alice@example.com", 0)]
    [InlineData("ORD-1", "Founding Pro", "alice@example.com", -3)]
    public void Signed_keys_with_missing_or_invalid_fields_are_rejected(string id, string edition, string licensee, int seats)
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var token = LicenseService.SignPayload(SamplePayload(seats: seats, edition: edition, licensee: licensee, id: id), priv);
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            Assert.False(svc.VerifyKey(token).IsValid);
        }
    }

    public static IEnumerable<object[]> Base64UrlLengths() =>
        Enumerable.Range(0, 36).Select(len => new object[] { len });

    [Theory]
    [MemberData(nameof(Base64UrlLengths))]
    public void Base64Url_roundtrips_every_byte_length_and_padding_case(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)((i * 37 + 19) & 0xFF);
        var encoded = LicenseService.Base64UrlEncode(data);
        Assert.DoesNotContain("=", encoded);
        Assert.DoesNotContain("+", encoded);
        Assert.DoesNotContain("/", encoded);
        Assert.Equal(data, LicenseService.Base64UrlDecode(encoded));
    }

    [Fact]
    public void Safe_mode_blocks_license_disk_writes_and_deletions()
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var path = Path.Combine(Path.GetTempPath(), "safe-lic-" + Guid.NewGuid() + ".json");
            var svc = new LicenseService(path, pub);
            var token = LicenseService.SignPayload(SamplePayload(), priv);

            var res = svc.ActivateAndSave(token);
            Assert.False(res.IsValid);
            Assert.Contains("safe mode", res.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(path));
        }
    }

    [Fact]
    public void Normal_mode_persists_reloads_and_deactivates_license_cleanly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lic-store-" + Guid.NewGuid());
        try
        {
            var (priv, pub) = CreateKeyPair();
            using (priv)
            {
                var licPath = Path.Combine(dir, "license.json");
                var optPath = Path.Combine(dir, "options.json");
                var svc = new LicenseService(licPath, pub) { IsSafeMode = () => false };
                var token = LicenseService.SignPayload(SamplePayload(edition: "Standard Pro", seats: 3, licensee: "Bob"), priv);

                var vm = new OptionsViewModel(new RescueOptionsStore(optPath), svc);
                Assert.False(vm.IsLicensed);
                Assert.Equal("v2.0 BETA", vm.EditionBadge);

                vm.LicenseKeyInput = token;
                vm.ActivateLicense();

                Assert.True(vm.IsLicensed);
                Assert.Equal("v2.0 PRO", vm.EditionBadge);
                Assert.Equal("", vm.LicenseKeyInput);
                Assert.True(File.Exists(licPath));

                var reloaded = new OptionsViewModel(new RescueOptionsStore(optPath), svc);
                Assert.True(reloaded.IsLicensed);
                Assert.Equal("v2.0 PRO", reloaded.EditionBadge);
                Assert.Contains("Standard Pro", reloaded.LicenseStatus);

                reloaded.DeactivateLicense();
                Assert.False(reloaded.IsLicensed);
                Assert.Equal("v2.0 BETA", reloaded.EditionBadge);
                Assert.False(File.Exists(licPath));
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Schema\":99,\"Key\":\"WCP2.a.b\"}")]
    [InlineData("{\"Schema\":1,\"Key\":\"\"}")]
    [InlineData("{\"Schema\":1,\"Key\":\"WCP2.tampered.key\"}")]
    public void Corrupt_or_tampered_stored_license_falls_back_safely_to_unlicensed(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "corrupt-lic-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        try
        {
            var (_, pub) = CreateKeyPair();
            var svc = new LicenseService(path, pub);
            var loaded = svc.LoadActivatedLicense();
            Assert.False(loaded.IsValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Batch_generated_keys_are_all_unique_and_cryptographically_valid()
    {
        var (priv, pub) = CreateKeyPair();
        using (priv)
        {
            var svc = new LicenseService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), pub);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 1; i <= 50; i++)
            {
                var payload = SamplePayload(seats: 1, edition: "Founding Pro", licensee: $"Buyer #{i}", id: $"WCP-{i:D4}-{Guid.NewGuid():N}");
                var token = LicenseService.SignPayload(payload, priv);
                Assert.True(keys.Add(token), "Duplicate token generated");
                var verified = svc.VerifyKey(token);
                Assert.True(verified.IsValid);
                Assert.Equal($"Buyer #{i}", verified.Payload!.Licensee);
            }
            Assert.Equal(50, keys.Count);
        }
    }
}
