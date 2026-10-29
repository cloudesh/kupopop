using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kupopop.Server;

public static class LocalCertificate
{
    public static X509Certificate2 Load(string directory, bool requestTrust)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "localhost.pfx");
        X509Certificate2 cert;
        if (File.Exists(path)) cert = X509CertificateLoader.LoadPkcs12FromFile(path, null);
        else
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            File.WriteAllBytes(path, generated.Export(X509ContentType.Pfx));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            cert = X509CertificateLoader.LoadPkcs12FromFile(path, null);
        }
        File.WriteAllText(Path.Combine(directory, "localhost.cer"), cert.ExportCertificatePem());
        if (DateTime.UtcNow >= cert.NotAfter.ToUniversalTime()) throw new InvalidOperationException("Local certificate expired. Stop the server and remove data/localhost.pfx to generate a new one.");
        if (requestTrust && OperatingSystem.IsWindows())
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count == 0)
            {
                Console.WriteLine("LOCAL TESTING ONLY: Trust this localhost certificate for your Windows user?");
                Console.WriteLine($"SHA-256: {Convert.ToHexString(SHA256.HashData(cert.RawData))}");
                Console.Write("Type TRUST to install it, or press Enter to cancel: ");
                if (!string.Equals(Console.ReadLine(), "TRUST", StringComparison.Ordinal))
                    throw new InvalidOperationException("Local trust setup cancelled. No certificate was installed.");
                store.Add(X509CertificateLoader.LoadCertificate(cert.RawData));
            }
        }
        return cert;
    }
}
