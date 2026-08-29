using System.Security.Cryptography;
using System.Text;

namespace RufusMapEditor.Licensing.Client;

/// <summary>Stable installation/device identity for license binding. Not WPF-coupled.</summary>
public interface IDeviceIdProvider
{
    /// <summary>Derived opaque id (hex). Never raw serials/MAC/username.</summary>
    string GetDeviceId();
}

/// <summary>
/// Windows MachineGuid (HKLM Cryptography) hashed with domain salt.
/// Independent of portable folder — copying RufusMapEditor.exe+Library does not copy this id.
/// Sends only SHA-256 hex to backend.
/// </summary>
public sealed class WindowsMachineGuidDeviceIdProvider : IDeviceIdProvider
{
    public const string Salt = "rufus-device-v1";

    public string GetDeviceId()
    {
        var machineGuid = TryReadMachineGuid();
        if (string.IsNullOrWhiteSpace(machineGuid))
            machineGuid = "unknown-machine";

        var material = Salt + "|" + machineGuid.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? TryReadMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Deterministic fake for tests.</summary>
public sealed class FakeDeviceIdProvider : IDeviceIdProvider
{
    private readonly string _id;
    public FakeDeviceIdProvider(string id) => _id = id;
    public string GetDeviceId() => _id;
}
