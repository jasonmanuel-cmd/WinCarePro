using System.IO;
using System.Security.Cryptography;
using WinCareDesktop.Services;

namespace LicenseGen;

/// <summary>
/// Owner-only offline ECDSA P-256 keypair &amp; WCP2 license-key generator.
/// Never commit generated private keys to source control.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        string? GetOpt(string flag)
        {
            for (var i = 1; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        switch (cmd)
        {
            case "keypair":
            {
                var outPriv = GetOpt("--out-private") ?? "wincare-owner-private.pem";
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var privPem = ecdsa.ExportECPrivateKeyPem();
                var pubSpkiBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
                File.WriteAllText(outPriv, privPem);
                Console.WriteLine($"Wrote private key (KEEP SECRET, DO NOT COMMIT): {Path.GetFullPath(outPriv)}");
                Console.WriteLine("Embed this DefaultPublicKeySpkiBase64 in Services/LicenseService.cs:");
                Console.WriteLine(pubSpkiBase64);
                return 0;
            }

            case "issue":
            {
                var privPath = GetOpt("--private-key");
                var licensee = GetOpt("--licensee");
                if (string.IsNullOrWhiteSpace(privPath) || string.IsNullOrWhiteSpace(licensee))
                {
                    Console.Error.WriteLine("Missing --private-key or --licensee.");
                    return 2;
                }
                var edition = GetOpt("--edition") ?? "Founding Pro";
                var orderId = GetOpt("--order") ?? ("WCP-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant());
                var seats = int.TryParse(GetOpt("--seats"), out var s) && s > 0 ? s : 1;
                var major = int.TryParse(GetOpt("--major"), out var m) && m > 0 ? m : 2;

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(File.ReadAllText(privPath));
                var pubSpkiBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

                var payload = new LicensePayload(
                    LicenseId: orderId,
                    Edition: edition,
                    Licensee: licensee.Trim(),
                    MajorVersion: major,
                    MaxSeats: seats,
                    IssuedUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

                var token = LicenseService.SignPayload(payload, ecdsa);
                var check = new LicenseService(publicKeySpkiBase64: pubSpkiBase64).VerifyKey(token);
                if (!check.IsValid)
                {
                    Console.Error.WriteLine("Self-verification failed: " + check.Message);
                    return 3;
                }

                Console.WriteLine(token);
                return 0;
            }

            case "batch":
            {
                var privPath = GetOpt("--private-key");
                var outFile = GetOpt("--out") ?? "license-pool.txt";
                if (string.IsNullOrWhiteSpace(privPath))
                {
                    Console.Error.WriteLine("Missing --private-key.");
                    return 2;
                }
                var count = int.TryParse(GetOpt("--count"), out var c) && c is > 0 and <= 10000 ? c : 100;
                var edition = GetOpt("--edition") ?? "Founding Pro";
                var prefix = GetOpt("--prefix") ?? "WCP";
                var seats = int.TryParse(GetOpt("--seats"), out var s) && s > 0 ? s : 1;
                var major = int.TryParse(GetOpt("--major"), out var m) && m > 0 ? m : 2;

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(File.ReadAllText(privPath));
                var pubSpkiBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
                var verifier = new LicenseService(publicKeySpkiBase64: pubSpkiBase64);

                var keys = new List<string>(count);
                var issued = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                for (var i = 1; i <= count; i++)
                {
                    var id = $"{prefix}-{i:D4}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
                    var payload = new LicensePayload(id, edition, $"License #{i}", major, seats, issued);
                    var token = LicenseService.SignPayload(payload, ecdsa);
                    if (!verifier.VerifyKey(token).IsValid)
                    {
                        Console.Error.WriteLine($"Self-verification failed at index {i}.");
                        return 3;
                    }
                    keys.Add(token);
                }

                File.WriteAllLines(outFile, keys);
                Console.WriteLine($"Generated and verified {keys.Count} unique '{edition}' keys -> {Path.GetFullPath(outFile)}");
                return 0;
            }

            case "verify":
            {
                var key = GetOpt("--key");
                var pub = GetOpt("--public-key");
                var svc = new LicenseService(publicKeySpkiBase64: pub);
                var res = svc.VerifyKey(key);
                Console.WriteLine(res.Message);
                return res.IsValid ? 0 : 1;
            }

            default:
                PrintUsage();
                return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("WinCare Pro Offline License Generator (ECDSA P-256)");
        Console.WriteLine("  keypair --out-private <path.pem>");
        Console.WriteLine("  issue   --private-key <path.pem> --licensee <email-or-name> [--edition \"Founding Pro\"] [--order ORD-1] [--seats 1]");
        Console.WriteLine("  batch   --private-key <path.pem> [--count 500] [--edition \"Founding Pro\"] [--seats 1] [--out keys.txt]");
        Console.WriteLine("  verify  --key <WCP2...> [--public-key <spki-base64>]");
    }
}
