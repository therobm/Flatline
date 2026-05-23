using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flatline.Logging;

namespace Flatline.Http
{
    public static class CertificateProvider
    {
        private const string CertFilePath = "flatline-cert.pfx";
        private const string CertPassword = "flatline";

        public static X509Certificate2 EnsureServerCertificate()
        {
            if (!File.Exists(CertFilePath))
            {
                Log.Info("Generating new self-signed TLS certificate at " + CertFilePath);
                byte[] generatedPfx = GenerateSelfSignedPfx();
                File.WriteAllBytes(CertFilePath, generatedPfx);
            }
            else
            {
                Log.Info("Loading existing TLS certificate from " + CertFilePath);
            }

            byte[] pfxBytes = File.ReadAllBytes(CertFilePath);
            X509Certificate2 fileCert = new X509Certificate2(
                pfxBytes,
                CertPassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            try
            {
                InstallInCurrentUserStore(fileCert);
                X509Certificate2 storeBackedCert = FetchFromCurrentUserStore(fileCert.Thumbprint);
                if (storeBackedCert == null)
                {
                    throw new InvalidOperationException("Certificate not found in CurrentUser\\My after install: " + fileCert.Thumbprint);
                }
                return storeBackedCert;
            }
            finally
            {
                fileCert.Dispose();
            }
        }

        public static void RemoveFromCurrentUserStore(X509Certificate2 cert)
        {
            if (cert == null)
            {
                return;
            }
            string thumbprint = cert.Thumbprint;
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                store.Open(OpenFlags.ReadWrite);
                X509Certificate2Collection matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                int matchCount = matches.Count;
                for (int matchIndex = 0; matchIndex < matchCount; matchIndex++)
                {
                    store.Remove(matches[matchIndex]);
                    matches[matchIndex].Dispose();
                }
                if (matchCount > 0)
                {
                    Log.Info("Removed TLS certificate from CurrentUser\\My (thumbprint " + thumbprint + ")");
                }
            }
            finally
            {
                store.Close();
            }
        }

        private static byte[] GenerateSelfSignedPfx()
        {
            RSA rsaKey = RSA.Create(2048);
            try
            {
                CertificateRequest request = new CertificateRequest(
                    "CN=Flatline Self-Signed",
                    rsaKey,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                SubjectAlternativeNameBuilder sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

                /* Include every up, non-loopback IPv4 address on the machine so that
                 * other devices on the LAN can hit this server over HTTPS without
                 * tripping a "subject alt name doesn't match" cert error. */
                IPAddress[] lanAddresses = GetLanIPv4Addresses();
                int lanCount = lanAddresses.Length;
                for (int lanIndex = 0; lanIndex < lanCount; lanIndex++)
                {
                    IPAddress lanAddress = lanAddresses[lanIndex];
                    sanBuilder.AddIpAddress(lanAddress);
                    Log.Info("  SAN includes LAN IP " + lanAddress);
                }

                request.CertificateExtensions.Add(sanBuilder.Build());

                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));

                OidCollection serverAuthOids = new OidCollection();
                serverAuthOids.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(serverAuthOids, false));

                X509KeyUsageFlags keyUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;
                request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsages, true));

                X509SubjectKeyIdentifierExtension subjectKeyId = new X509SubjectKeyIdentifierExtension(request.PublicKey, false);
                request.CertificateExtensions.Add(subjectKeyId);

                DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
                DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddYears(5);

                X509Certificate2 generatedCert = request.CreateSelfSigned(notBefore, notAfter);
                try
                {
                    return generatedCert.Export(X509ContentType.Pkcs12, CertPassword);
                }
                finally
                {
                    generatedCert.Dispose();
                }
            }
            finally
            {
                rsaKey.Dispose();
            }
        }

        private static void InstallInCurrentUserStore(X509Certificate2 cert)
        {
            string thumbprint = cert.Thumbprint;
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                store.Open(OpenFlags.ReadWrite);
                X509Certificate2Collection existing = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                int existingCount = existing.Count;
                for (int existingIndex = 0; existingIndex < existingCount; existingIndex++)
                {
                    store.Remove(existing[existingIndex]);
                    existing[existingIndex].Dispose();
                }
                store.Add(cert);
                Log.Info("Installed TLS certificate into CurrentUser\\My (thumbprint " + thumbprint + ")");
            }
            finally
            {
                store.Close();
            }
        }

        private static IPAddress[] GetLanIPv4Addresses()
        {
            System.Collections.Generic.List<IPAddress> collected = new System.Collections.Generic.List<IPAddress>();
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            int interfaceCount = interfaces.Length;
            for (int interfaceIndex = 0; interfaceIndex < interfaceCount; interfaceIndex++)
            {
                NetworkInterface nic = interfaces[interfaceIndex];
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                IPInterfaceProperties properties = nic.GetIPProperties();
                UnicastIPAddressInformationCollection unicastAddresses = properties.UnicastAddresses;
                int unicastCount = unicastAddresses.Count;
                for (int unicastIndex = 0; unicastIndex < unicastCount; unicastIndex++)
                {
                    UnicastIPAddressInformation unicastInfo = unicastAddresses[unicastIndex];
                    if (unicastInfo.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    if (IPAddress.IsLoopback(unicastInfo.Address))
                    {
                        continue;
                    }
                    collected.Add(unicastInfo.Address);
                }
            }
            return collected.ToArray();
        }

        private static X509Certificate2 FetchFromCurrentUserStore(string thumbprint)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                store.Open(OpenFlags.ReadOnly);
                X509Certificate2Collection matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                if (matches.Count == 0)
                {
                    return null;
                }
                return matches[0];
            }
            finally
            {
                store.Close();
            }
        }
    }
}
