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

    // ---------- Checks run before the script, so problems can be asked about instead of failing halfway ----------

    /// <summary>An existing SMB share. IsFolder is false for printer queues and other non-disk shares.</summary>
    public sealed record ExistingShare(string Name, string Path, bool IsFolder);

    private static string Wql(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    private static IEnumerable<System.Management.ManagementBaseObject> Query(string wql)
    {
        using var searcher = new System.Management.ManagementObjectSearcher(wql);
        foreach (var o in searcher.Get()) yield return o;
    }

    /// <summary>The share with this name (Windows compares share names case-insensitively), or null.</summary>
    public static ExistingShare? FindShare(string name)
    {
        foreach (var o in Query($"SELECT Name, Path, Type FROM Win32_Share WHERE Name = '{Wql(name)}'"))
        {
            // Type: 0 = disk folder, 1 = print queue, 2 = device, 3 = IPC; the high bit marks admin shares.
            uint type = Convert.ToUInt32(o["Type"] ?? 0u) & 0x7FFFFFFF;
            return new ExistingShare((string)o["Name"], (o["Path"] as string ?? "").TrimEnd('\\'), type == 0);
        }
        return null;
    }

    /// <summary>The first free share name of the form Scans2, Scans3 ...</summary>
    public static string SuggestShareName(string baseName)
    {
        for (int i = 2; i < 100; i++)
            if (FindShare(baseName + i) == null) return baseName + i;
        return baseName + "_" + Environment.TickCount % 10000;
    }

    /// <summary>Returns why the share name cannot be used, or null if it can.</summary>
    public static string? CheckShareName(string name)
    {
        if (name.Length == 0) return "Enter a share name.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[\w\.\- ]{1,80}$"))
            return "Use letters, numbers, spaces, dots, dashes or underscores (80 at most).";
        if (name != name.Trim()) return "The name cannot start or end with a space.";
        var taken = FindShare(name);
        if (taken != null) return $"'{taken.Name}' is also taken ({(taken.IsFolder ? taken.Path : "not a folder share")}).";
        return null;
    }

    public static string NormalizeFolder(string folder) => Path.GetFullPath(folder).TrimEnd('\\');

    private static bool SameOrUnder(string path, string? root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        root = root.TrimEnd('\\');
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns why the folder is not suitable for a scan share, or null if it is. Pass a full path.</summary>
    public static string? CheckFolder(string folder)
    {
        if (folder.StartsWith(@"\\")) return "Pick a folder on this PC, not a network path.";
        if (!Path.IsPathFullyQualified(folder)) return "Enter a full folder path such as C:\\Scans.";
        string full;
        try { full = NormalizeFolder(folder); }
        catch (Exception ex) { return "That folder path is not valid: " + ex.Message; }

        var root = Path.GetPathRoot(full) ?? "";
        if (full.Length <= 3) return $"Pick a folder, not the whole drive ({root}).";
        if (File.Exists(full)) return $"{full} is a file, not a folder.";

        var drive = new DriveInfo(root);
        if (drive.DriveType is DriveType.Network or DriveType.CDRom or DriveType.NoRootDirectory or DriveType.Unknown)
            return $"Drive {root} is not a local disk on this PC. Pick a folder on a local drive.";
        if (!drive.IsReady) return $"Drive {root} is not ready.";

        // The scan account gets full control, so keep it away from system folders and whole profiles.
        foreach (var sys in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                 })
            if (SameOrUnder(full, sys)) return $"{full} is a Windows or program folder. Pick a folder of its own, such as C:\\Scans.";

        var profiles = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (profiles != null &&
            (full.Equals(profiles.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetDirectoryName(full), profiles.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            return "That is a whole user profile. Pick or make a folder inside it instead, such as a Scans folder.";

        foreach (var whole in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     Environment.GetEnvironmentVariable("OneDrive"),
                     Environment.GetEnvironmentVariable("OneDriveCommercial"),
                     Environment.GetEnvironmentVariable("OneDriveConsumer")
                 })
            if (!string.IsNullOrEmpty(whole) && full.Equals(whole.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return $"Sharing all of {full} would give the scanner access to everything in it. Make a Scans folder inside it instead.";

        return null;
    }

    public enum AccountState { Missing, Exists, Blocked }

    /// <summary>Whether the scan account can be created, already exists (its password will be reset), or cannot be used, with the reason.</summary>
    public static (AccountState State, string? Reason) CheckAccount()
    {
        var machine = Environment.MachineName;
        if (UserName.Equals(machine, StringComparison.OrdinalIgnoreCase))
            return (AccountState.Blocked, $"This PC is called {machine}, the same as the account name '{UserName}'. Windows does not allow that. Rename the PC first.");
        if (UserName.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase) &&
            Environment.UserDomainName.Equals(machine, StringComparison.OrdinalIgnoreCase))
            return (AccountState.Blocked, $"You are signed in as '{UserName}'. Resetting its password would change your own. Sign in with another administrator account.");

        // LocalAccount = TRUE stops WMI asking the domain controller on domain-joined PCs.
        string q = $"LocalAccount = TRUE AND Name = '{Wql(UserName)}'";
        if (Query($"SELECT Name FROM Win32_Group WHERE {q}").Any())
            return (AccountState.Blocked, $"There is a local group called '{UserName}', so an account with that name cannot be made. Rename or remove the group first.");
        if (Query($"SELECT Name FROM Win32_UserAccount WHERE {q}").Any())
            return (AccountState.Exists, null);
        return (AccountState.Missing, null);
    }

    public static Task<string> ConfigureAsync(string folder, string shareName, string password, bool desktopShortcut, bool quickAccess,
        IProgress<string>? progress)
    {
        var args = new List<string> { "-Folder", folder, "-UserName", UserName, "-ShareName", shareName };
        if (desktopShortcut) args.Add("-DesktopShortcut");
        if (quickAccess) args.Add("-QuickAccess");
        return QueueBundles.RunScriptAsync("scan-host.ps1", args, progress,
            new Dictionary<string, string> { ["SCANHOST_PASSWORD"] = password });
    }
}
