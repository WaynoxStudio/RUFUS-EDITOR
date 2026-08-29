using System.Net.Http.Json;
using System.Text.Json;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts;

// Dev-only license probe. NOT for dist.
// Usage:
//   LicenseProbe activate --base http://127.0.0.1:5088 --code RUF-... [--device fake-id]
//   LicenseProbe heartbeat --base ... --token ... --device ...
//   LicenseProbe logout --base ... --token ... --device ...
//   LicenseProbe device-id

static int Fail(string msg)
{
    Console.Error.WriteLine(msg);
    return 1;
}

if (args.Length == 0)
    return Fail("Commands: activate | heartbeat | logout | device-id");

var cmd = args[0].ToLowerInvariant();
string? GetOpt(string name)
{
    for (var i = 1; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

if (cmd == "device-id")
{
    IDeviceIdProvider provider = OperatingSystem.IsWindows()
        ? new WindowsMachineGuidDeviceIdProvider()
        : new FakeDeviceIdProvider("non-windows");
    var id = provider.GetDeviceId();
    Console.WriteLine(id);
    Console.WriteLine("(hash only — MachineGuid never printed)");
    return 0;
}

var baseUrl = GetOpt("--base") ?? Environment.GetEnvironmentVariable("RUFUS_LICENSE_API_BASE") ?? "http://127.0.0.1:5088";
var device = GetOpt("--device");
if (string.IsNullOrWhiteSpace(device))
{
    IDeviceIdProvider provider = OperatingSystem.IsWindows()
        ? new WindowsMachineGuidDeviceIdProvider()
        : new FakeDeviceIdProvider("probe-default-device");
    device = provider.GetDeviceId();
}

using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

switch (cmd)
{
    case "activate":
    {
        var code = GetOpt("--code");
        if (string.IsNullOrWhiteSpace(code))
            return Fail("--code required");
        var body = new ActivateLicenseRequest
        {
            LicenseCode = code,
            DeviceId = device!,
            ClientVersion = "LicenseProbe/1.0",
        };
        using var res = await http.PostAsJsonAsync("v1/license/activate", body);
        var text = await res.Content.ReadAsStringAsync();
        Console.WriteLine($"HTTP {(int)res.StatusCode}");
        Console.WriteLine(text);
        return res.IsSuccessStatusCode ? 0 : 2;
    }
    case "heartbeat":
    case "session":
    {
        var token = GetOpt("--token");
        if (string.IsNullOrWhiteSpace(token))
            return Fail("--token required");
        var path = cmd == "session" ? "v1/license/session" : "v1/license/heartbeat";
        var body = new HeartbeatRequest { SessionToken = token, DeviceId = device! };
        using var res = await http.PostAsJsonAsync(path, body);
        Console.WriteLine($"HTTP {(int)res.StatusCode}");
        Console.WriteLine(await res.Content.ReadAsStringAsync());
        return res.IsSuccessStatusCode ? 0 : 2;
    }
    case "logout":
    {
        var token = GetOpt("--token");
        if (string.IsNullOrWhiteSpace(token))
            return Fail("--token required");
        var body = new LogoutRequest { SessionToken = token, DeviceId = device! };
        using var res = await http.PostAsJsonAsync("v1/license/logout", body);
        Console.WriteLine($"HTTP {(int)res.StatusCode}");
        Console.WriteLine(await res.Content.ReadAsStringAsync());
        return res.IsSuccessStatusCode ? 0 : 2;
    }
    default:
        return Fail("Unknown command");
}
