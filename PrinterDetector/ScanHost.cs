namespace PrinterDetector;

/// <summary>
/// Sets this PC up to receive scans from a network scanner: SMB, private network, a "Scans" account, a folder and a share.
/// The work is done by Assets\Scripts\scan-host.ps1; this class checks the password and runs it.
/// </summary>
internal static class ScanHost
{
    public const string UserName = "Scans";
    public const string ShareName = "Scans";
    public const string DefaultFolder = @"C:\Scans";
    public const int MinLength = 8;

    // Common choices, compared after lowercasing, undoing look-alike characters and dropping trailing digits and symbols,
    // so "Password1!" and "P@ssw0rd" are caught as well as "password".
    private static readonly HashSet<string> Weak = new(StringComparer.Ordinal)
    {
        "password", "passw0rd", "passwd", "pass", "admin", "administrator", "root", "user", "guest", "login", "letmein",
        "welcome", "qwerty", "qwertyuiop", "asdfgh", "asdfghjkl", "zxcvbn", "zxcvbnm", "azerty", "abc", "abcdef", "abcdefgh",
        "iloveyou", "monkey", "dragon", "master", "sunshine", "princess", "football", "baseball", "superman", "trustno",
        "changeme", "default", "secret", "test", "testing", "temp", "temporary", "scans", "scan", "scanner", "scanning",
        "brother", "printer", "print", "office", "company", "network", "windows", "computer", "internet", "summer", "winter",
        "spring", "autumn", "hello", "freedom", "whatever", "shadow", "michael", "jennifer", "charlie"
    };

    private static readonly HashSet<string> WeakExact = new(StringComparer.Ordinal)
    {
        "12345678", "123456789", "1234567890", "87654321", "98765432", "11111111", "00000000", "12341234", "11223344",
        "1q2w3e4r", "1q2w3e4r5t", "q1w2e3r4", "123123123", "12121212", "69696969", "666666666"
    };

    /// <summary>Returns why the password is not acceptable, or null if it is.</summary>
    public static string? CheckPassword(string password)
    {
        if (password.Length < MinLength) return $"Use at least {MinLength} characters.";
        var lower = password.ToLowerInvariant();

        if (WeakExact.Contains(lower)) return "That is one of the most common passwords. Pick something else.";
        if (password.Distinct().Count() < 4) return "Use more than a few different characters.";
        if (IsRun(lower)) return "Letters or digits in a row (like 12345678 or abcdefgh) are too easy to guess.";

        bool hasLetter = password.Any(char.IsLetter);
        bool hasOther = password.Any(c => !char.IsLetter(c));
        if (!hasLetter || !hasOther) return "Mix letters with numbers or symbols.";

        if (lower.Contains(UserName.ToLowerInvariant())) return $"Do not include the account name '{UserName}'.";
        if (lower.Contains(Environment.MachineName.ToLowerInvariant()) && Environment.MachineName.Length >= 4)
            return "Do not include this PC's name.";

        var core = Core(lower);
        if (Weak.Contains(core) || WeakExact.Contains(core) || IsRun(core))
            return "That is a common password with a number or symbol added. Pick something else.";

        return null;
    }

    /// <summary>Drops trailing digits and symbols, then undoes look-alike characters (@ to a, 0 to o ...).</summary>
    private static string Core(string lower)
    {
        int end = lower.Length;
        while (end > 0 && !char.IsLetter(lower[end - 1])) end--;
        return new string(lower[..end].Select(c => c switch
        {
            '@' => 'a', '4' => 'a', '0' => 'o', '3' => 'e', '$' => 's', '5' => 's', '1' => 'l', '!' => 'i', _ => c
        }).ToArray());
    }

    /// <summary>True for text where every character is the previous one plus or minus one (12345678, abcdefgh, 987654321).</summary>
    private static bool IsRun(string s)
    {
        if (s.Length < 4) return false;
        int step = s[1] - s[0];
        if (step is not (1 or -1)) return false;
        for (int i = 1; i < s.Length; i++)
            if (s[i] - s[i - 1] != step) return false;
        return true;
    }

    /// <summary>The PC's LAN address: the IPv4 on an adapter that has a default gateway, else the first usable one, else the PC name.</summary>
    public static string LanAddress()
    {
        string? fallback = null;
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            var ip = props.UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                     !a.ToString().StartsWith("169.254."));
            if (ip == null) continue;
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                                              !g.Address.Equals(System.Net.IPAddress.Any));
            if (hasGateway) return ip.ToString();
            fallback ??= ip.ToString();
        }
        return fallback ?? Environment.MachineName;
    }

    public static Task<string> ConfigureAsync(string folder, string password, IProgress<string>? progress) =>
        QueueBundles.RunScriptAsync("scan-host.ps1",
            new[] { "-Folder", folder, "-UserName", UserName, "-ShareName", ShareName },
            progress,
            new Dictionary<string, string> { ["SCANHOST_PASSWORD"] = password });
}
