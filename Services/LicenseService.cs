using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinCareDesktop.Services;

public sealed record LicensePayload(
    string LicenseId,
    string Edition,
    string Licensee,
    int MajorVersion,
    int MaxSeats,
    string IssuedUtc);

public sealed record LicenseValidationResult(
    bool IsValid,
    string Message,
    LicensePayload? Payload = null);

public sealed record StoredLicenseRecord(
    int Schema,
    string Key,
    string ActivatedUtc);

/// <summary>
/// 100% offline asymmetric ECDSA P-256 license verifier and local store.
/// Performs zero network calls and stores only the user-supplied license token
/// under %LOCALAPPDATA%\WinCarePro\license.json.
/// </summary>
public sealed class LicenseService
{
    public const string TokenPrefix = "WCP2.";
    public const int SupportedMajorVersion = 2;

    /// <summary>
    /// Default P-256 SubjectPublicKeyInfo (DER, Base64). Replace with your
    /// production public key generated via `dotnet run --project Tools/LicenseGen -- keypair`
    /// before selling keys; private keys never live in this repository.
    /// </summary>
    public const string DefaultPublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+iGWZY1yRFvNLp46/aSwn/SMjeWUYzn7p8AXFpQmJAGYFEk1lhOiMXuJFiZHGmtQEikq1UulKSwNsRUxKuLHRg==";

    private readonly byte[] _publicKeySpki;
    private readonly string _licenseFilePath;
    public Func<bool> IsSafeMode { get; set; } = () => SafetyMode.Enabled;

    public LicenseService(string? licenseFilePath = null, string? publicKeySpkiBase64 = null)
    {
        _licenseFilePath = licenseFilePath ?? Path.Combine(SafetyMode.DataRoot, "license.json");
        _publicKeySpki = Convert.FromBase64String(publicKeySpkiBase64 ?? DefaultPublicKeySpkiBase64);
    }

    public string LicenseFilePath => _licenseFilePath;

    public LicenseValidationResult VerifyKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return new(false, "Enter a WinCare Pro v2 license key starting with WCP2.");

        // Allow copy-pasting keys that wrapped across email lines.
        var cleaned = new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!cleaned.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return new(false, "Invalid key format: expected WCP2.<payload>.<signature>.");

        var parts = cleaned.Split('.');
        if (parts.Length != 3 || parts[1].Length == 0 || parts[2].Length == 0)
            return new(false, "Invalid key structure: expected three dot-separated segments.");

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[1]);
            signatureBytes = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return new(false, "License key contains invalid Base64Url characters.");
        }

        if (payloadBytes.Length is 0 or > 4096 || signatureBytes.Length != 64)
            return new(false, "License key payload or ECDSA signature length is invalid.");

        bool signatureOk;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(_publicKeySpki, out var bytesRead);
            if (bytesRead != _publicKeySpki.Length)
                return new(false, "Configured public key metadata is invalid.");

            var signedData = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            signatureOk = ecdsa.VerifyData(signedData, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return new(false, "Cryptographic verification could not validate this key.");
        }

        if (!signatureOk)
            return new(false, "License signature verification failed. Check for typos or contact support.");

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return new(false, "Signed license payload is malformed.");
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.LicenseId) ||
            string.IsNullOrWhiteSpace(payload.Edition) ||
            string.IsNullOrWhiteSpace(payload.Licensee) ||
            payload.MaxSeats <= 0)
        {
            return new(false, "License payload is missing required fields.");
        }

        if (payload.MajorVersion != SupportedMajorVersion)
        {
            return new(false, $"This key is for WinCare Pro v{payload.MajorVersion}.x (current app is v{SupportedMajorVersion}.x).");
        }

        return new(true, $"Activated: {payload.Edition} ({payload.Licensee}) • {payload.MaxSeats} PC, v{payload.MajorVersion}.x updates.", payload);
    }

    public LicenseValidationResult LoadActivatedLicense()
    {
        try
        {
            if (!File.Exists(_licenseFilePath))
                return new(false, "Community / Evaluation mode — enter an offline WCP2 license key to register Pro.");

            var json = File.ReadAllText(_licenseFilePath);
            var stored = JsonSerializer.Deserialize<StoredLicenseRecord>(json);
            if (stored is null || stored.Schema != 1 || string.IsNullOrWhiteSpace(stored.Key))
                return new(false, "Stored license file could not be read; re-enter your license key.");

            var verified = VerifyKey(stored.Key);
            return verified.IsValid
                ? verified
                : new(false, "Stored license key failed verification; re-enter a valid WCP2 key.");
        }
        catch
        {
            return new(false, "Stored license file could not be read; running in evaluation mode.");
        }
    }

    public LicenseValidationResult ActivateAndSave(string? rawKey)
    {
        if (IsSafeMode())
            return new(false, "License activation writes are disabled in safe mode.");

        var verified = VerifyKey(rawKey);
        if (!verified.IsValid)
            return verified;

        var cleaned = new string(rawKey!.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var dir = Path.GetDirectoryName(_licenseFilePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        var staging = _licenseFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var record = new StoredLicenseRecord(1, cleaned, DateTimeOffset.UtcNow.ToString("O"));
            File.WriteAllText(staging, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(staging, _licenseFilePath, overwrite: true);
            return verified;
        }
        catch
        {
            try { if (File.Exists(staging)) File.Delete(staging); } catch { }
            return new(false, "Could not write license.json locally; check folder permissions.");
        }
    }

    public (bool Success, string Message) Deactivate()
    {
        if (IsSafeMode())
            return (false, "License removal is disabled in safe mode.");

        try
        {
            if (File.Exists(_licenseFilePath)) File.Delete(_licenseFilePath);
            return (true, "Local license removed from this PC. Evaluation mode active.");
        }
        catch
        {
            return (false, "Could not remove local license file; check folder permissions.");
        }
    }

    /// <summary>
    /// Signs a payload with an ECDSA P-256 private key (used by Tools/LicenseGen and tests).
    /// </summary>
    public static string SignPayload(LicensePayload payload, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(privateKey);

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var encodedPayload = Base64UrlEncode(jsonBytes);
        var signingInput = Encoding.ASCII.GetBytes($"WCP2.{encodedPayload}");
        var sig = privateKey.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"WCP2.{encodedPayload}.{Base64UrlEncode(sig)}";
    }

    public static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 0: break;
            default: throw new FormatException("Illegal Base64Url string length.");
        }
        return Convert.FromBase64String(padded);
    }
}
