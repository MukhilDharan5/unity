using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PhoneCompanion.Windows.Connection;

public sealed record TrustedDevice(string Fingerprint, string Name, string? WifiEndpoint);

public sealed class IdentityAndTrustStore
{
    private const string KeyName = "UnityConnect.Windows.Identity.P256.v1";
    private readonly string _trustPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity Connect", "trusted-phone-v1.json");

    public ECDsa OpenIdentity()
    {
        if (CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
            return new ECDsaCng(CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider));
        var creation = new CngKeyCreationParameters { Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing };
        return new ECDsaCng(CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, creation));
    }

    public TrustedDevice? Load()
    {
        try { return File.Exists(_trustPath) ? JsonSerializer.Deserialize<TrustedDevice>(File.ReadAllBytes(_trustPath)) : null; }
        catch { return null; }
    }

    public void Save(TrustedDevice phone)
    {
        var directory = Path.GetDirectoryName(_trustPath)!;
        Directory.CreateDirectory(directory);
        var temp = _trustPath + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(phone));
        File.Move(temp, _trustPath, true);
    }

    public void Forget() { if (File.Exists(_trustPath)) File.Delete(_trustPath); }
}
