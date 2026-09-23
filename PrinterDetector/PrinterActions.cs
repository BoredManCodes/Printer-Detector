using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;

namespace PrinterDetector;

public static class PrinterActions
{
    public static void OpenWebGui(string address)
    {
        Process.Start(new ProcessStartInfo($"http://{address}") { UseShellExecute = true });
    }

    public static void OpenWebGuiHttps(string address)
    {
        Process.Start(new ProcessStartInfo($"https://{address}") { UseShellExecute = true });
    }

    public static void SearchDriverOnGoogle(string printerName)
    {
        var query = Uri.EscapeDataString($"{printerName} driver");
        Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={query}") { UseShellExecute = true });
    }

    public static void OpenPrintQueue(string printerName) => RunPrintUi($"/o /n \"{printerName}\"");

    public static void OpenProperties(string printerName) => RunPrintUi($"/p /n \"{printerName}\"");

    public static void OpenPrintingPreferences(string printerName) => RunPrintUi($"/e /n \"{printerName}\"");

    public static void OpenAddPrinterWizard() => RunPrintUi("/il");

    private static void RunPrintUi(string args)
    {
        Process.Start(new ProcessStartInfo("rundll32.exe", $"printui.dll,PrintUIEntry {args}")
        {
            UseShellExecute = true
        });
    }

    public static void CopyAddressToClipboard(string address) => Clipboard.SetText(address);

    public static void CopyText(string text) => Clipboard.SetText(text);

    public static void SetAsDefault(string printerName) => InvokePrinterMethod(printerName, "SetDefaultPrinter");
    public static void Pause(string printerName) => InvokePrinterMethod(printerName, "Pause");
    public static void Resume(string printerName) => InvokePrinterMethod(printerName, "Resume");
    public static void CancelAllJobs(string printerName) => InvokePrinterMethod(printerName, "CancelAllJobs");
    public static void PrintTestPage(string printerName) => InvokePrinterMethod(printerName, "PrintTestPage");

    private static void InvokePrinterMethod(string printerName, string method)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
        foreach (ManagementObject printer in searcher.Get())
        {
            if (!string.Equals(printer["Name"]?.ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                continue;

            var result = printer.InvokeMethod(method, null);
            var returnValue = result != null ? Convert.ToUInt32(result) : 0;
            if (returnValue != 0)
                throw new InvalidOperationException($"{method} failed with WMI return code {returnValue}.");
            return;
        }

        throw new InvalidOperationException($"Printer '{printerName}' not found.");
    }

    public static void Rename(string printerName, string newName) => InvokePrinterMethod(printerName, "RenamePrinter", newName);

    public static void Delete(string printerName)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
        foreach (ManagementObject printer in searcher.Get())
        {
            if (!string.Equals(printer["Name"]?.ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                continue;

            printer.Delete();
            return;
        }

        throw new InvalidOperationException($"Printer '{printerName}' not found.");
    }

    private static void InvokePrinterMethod(string printerName, string method, string arg)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
        foreach (ManagementObject printer in searcher.Get())
        {
            if (!string.Equals(printer["Name"]?.ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                continue;

            var inParams = printer.GetMethodParameters(method);
            inParams["NewPrinterName"] = arg;
            var result = printer.InvokeMethod(method, inParams, null);
            var returnValue = result?["ReturnValue"] != null ? Convert.ToUInt32(result["ReturnValue"]) : 0;
            if (returnValue != 0)
                throw new InvalidOperationException($"{method} failed with WMI return code {returnValue}.");
            return;
        }

        throw new InvalidOperationException($"Printer '{printerName}' not found.");
    }

    public static (string Comment, string Location, bool Shared, string ShareName) GetPrinterDetails(string printerName)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
        foreach (ManagementObject printer in searcher.Get())
        {
            if (!string.Equals(printer["Name"]?.ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                continue;

            return (
                printer["Comment"]?.ToString() ?? "",
                printer["Location"]?.ToString() ?? "",
                printer["Shared"] is bool b && b,
                printer["ShareName"]?.ToString() ?? "");
        }

        throw new InvalidOperationException($"Printer '{printerName}' not found.");
    }

    public static void SetCommentLocation(string printerName, string comment, string location)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
        foreach (ManagementObject printer in searcher.Get())
        {
            if (!string.Equals(printer["Name"]?.ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                continue;

            printer["Comment"] = comment;
            printer["Location"] = location;
            printer.Put();
            return;
        }

        throw new InvalidOperationException($"Printer '{printerName}' not found.");
    }

    public static void SetShared(string printerName, bool shared, string shareName)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
        foreach (ManagementObject printer in searcher.Get())
        {
            if (!string.Equals(printer["Name"]?.ToString(), printerName, StringComparison.OrdinalIgnoreCase))
                continue;

            printer["Shared"] = shared;
            if (shared && !string.IsNullOrWhiteSpace(shareName))
                printer["ShareName"] = shareName;
            printer.Put();
            return;
        }

        throw new InvalidOperationException($"Printer '{printerName}' not found.");
    }

    public static void RestartSpooler()
    {
        using var sc = new ServiceController("Spooler");
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
    }

    public static void ClearStuckQueue()
    {
        using (var sc = new ServiceController("Spooler"))
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));

            var spoolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");
            if (Directory.Exists(spoolDir))
            {
                foreach (var file in Directory.EnumerateFiles(spoolDir))
                {
                    try { File.Delete(file); } catch { /* file locked/in use - skip */ }
                }
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        }
    }

    public static void OpenSpoolFolder()
    {
        var spoolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "PRINTERS");
        Process.Start(new ProcessStartInfo("explorer.exe", spoolDir) { UseShellExecute = true });
    }

    public static void OpenDevicesAndPrinters() =>
        Process.Start(new ProcessStartInfo("control.exe", "printers") { UseShellExecute = true });

    public static void OpenPrintManagementConsole() =>
        Process.Start(new ProcessStartInfo("printmanagement.msc") { UseShellExecute = true });

    public static void OpenDeviceManager() =>
        Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });

    public static void OpenEventViewer() =>
        Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });

    public static void OpenServicesConsole() =>
        Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });

    public static void OpenTraceroute(string address) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k tracert -d {address}") { UseShellExecute = true });

    public static void OpenContinuousPing(string address) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k ping -t {address}") { UseShellExecute = true });

    public static async Task<string> PingAsync(string address)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 1500);
            return reply.Status == IPStatus.Success
                ? $"Reply from {address} in {reply.RoundtripTime} ms"
                : $"No reply ({reply.Status})";
        }
        catch (Exception ex)
        {
            return $"Ping failed: {ex.Message}";
        }
    }

    public static async Task<string> TestPort9100Async(string address)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(address, 9100);
            var completed = await Task.WhenAny(connectTask, Task.Delay(2000));
            if (completed != connectTask || !client.Connected)
                return "Port 9100 (raw print): closed / unreachable.";
            return "Port 9100 (raw print): open.";
        }
        catch (Exception ex)
        {
            return $"Port 9100 (raw print): closed / unreachable ({ex.Message}).";
        }
    }

    public static async Task<(bool Found, string Result)> GetArpMacAsync(string address)
    {
        // touch the ARP cache first so a printer that hasn't been talked to recently still resolves
        try
        {
            using var ping = new Ping();
            await ping.SendPingAsync(address, 1000);
        }
        catch { /* best effort */ }

        try
        {
            var psi = new ProcessStartInfo("arp.exe", $"-a {address}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains(address)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var mac = parts.FirstOrDefault(p => p.Count(c => c == '-') == 5 || p.Count(c => c == ':') == 5);
                if (mac != null) return (true, mac);
            }

            return (false, "MAC address not found (printer may be off-subnet or unreachable).");
        }
        catch (Exception ex)
        {
            return (false, $"ARP lookup failed: {ex.Message}");
        }
    }

    public static void SendWakeOnLan(string mac)
    {
        var bytes = mac.Split(new[] { '-', ':' }).Select(b => Convert.ToByte(b, 16)).ToArray();
        if (bytes.Length != 6)
            throw new InvalidOperationException("Not a valid MAC address.");

        var packet = new byte[102];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 1; i <= 16; i++)
            Array.Copy(bytes, 0, packet, i * 6, 6);

        using var client = new UdpClient { EnableBroadcast = true };
        client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
    }

    public record DiscoveredDevice(string Ip, string Hostname, List<int> OpenPorts);

    private static readonly int[] ScanPorts = { 9100, 631, 515 };

    private static List<(IPAddress Local, IPAddress Mask)> GetLocalIPv4Subnets()
    {
        var results = new List<(IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (ua.IPv4Mask == null || ua.IPv4Mask.Equals(IPAddress.Any)) continue;
                results.Add((ua.Address, ua.IPv4Mask));
            }
        }
        return results;
    }

    private static uint ToUint(IPAddress ip) => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0);

    private static IPAddress FromUint(uint value) => new(BitConverter.GetBytes(value).Reverse().ToArray());

    private static List<IPAddress> EnumerateHosts(IPAddress local, IPAddress mask)
    {
        uint ip = ToUint(local);
        uint m = ToUint(mask);
        uint network = ip & m;
        uint broadcast = network | ~m;
        uint hostCount = broadcast - network;

        // cap at a /22-sized range so a bad/huge mask (e.g. a VPN adapter with 255.0.0.0) can't hang the scan
        if (hostCount is 0 or > 1024) return new List<IPAddress>();

        var hosts = new List<IPAddress>();
        for (uint host = network + 1; host < broadcast; host++)
            hosts.Add(FromUint(host));
        return hosts;
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port, ct).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<DiscoveredDevice>> ScanNetworkForPrintersAsync(
        HashSet<string> excludeIps, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var hosts = GetLocalIPv4Subnets()
            .SelectMany(s => EnumerateHosts(s.Local, s.Mask))
            .DistinctBy(ip => ip.ToString())
            .Where(ip => !excludeIps.Contains(ip.ToString()))
            .ToList();

        var results = new System.Collections.Concurrent.ConcurrentBag<DiscoveredDevice>();
        var sem = new SemaphoreSlim(64);
        int done = 0;

        var tasks = hosts.Select(async ip =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var openPorts = new List<int>();
                foreach (var port in ScanPorts)
                {
                    if (ct.IsCancellationRequested) break;
                    if (await IsPortOpenAsync(ip, port, 350, ct))
                        openPorts.Add(port);
                }

                if (openPorts.Count == 0) return;

                string hostname = ip.ToString();
                try
                {
                    var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(TimeSpan.FromMilliseconds(600), ct);
                    if (!string.IsNullOrWhiteSpace(entry.HostName)) hostname = entry.HostName;
                }
                catch { /* no reverse DNS - fall back to IP */ }

                results.Add(new DiscoveredDevice(ip.ToString(), hostname, openPorts));
            }
            finally
            {
                Interlocked.Increment(ref done);
                progress?.Report((done, hosts.Count));
                sem.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // scan cancelled - return whatever was found before cancellation
        }

        return results.OrderBy(r => string.Join(".", r.Ip.Split('.').Select(o => o.PadLeft(3, '0')))).ToList();
    }

    // reseller/provider code for Border Tech Solutions - fixed per EKM account, does not change between installs
    private const string DcaProviderCode = "happen";
    private const string DcaDownloadUrl = "https://updates.ekmglobal.com/dist/DCA/Vanilla/";
    private const string DcaInstallDir = @"C:\Program Files (x86)\i2emfw";

    public static async Task<(bool Success, string Message)> DownloadAndInstallDcaAsync(string licenseKey, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            throw new InvalidOperationException("License key is required.");

        var tempDir = Path.Combine(Path.GetTempPath(), "dca_deploy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var installerPath = Path.Combine(tempDir, "DCA_Setup.exe");
        var varFilePath = Path.Combine(tempDir, "response.varfile");

        try
        {
            progress?.Report("Downloading latest DCA build...");
            using (var http = new HttpClient())
            using (var response = await http.GetAsync(DcaDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var fileStream = File.Create(installerPath);
                await response.Content.CopyToAsync(fileStream);
            }

            progress?.Report("Writing license response file...");
            File.WriteAllText(varFilePath, BuildDcaVarFile(licenseKey));

            progress?.Report("Running silent install (approve the UAC prompt if it appears)...");
            // a leftover install dir (db/log/backup from a prior uninstall) makes install4j's
            // silent mode exit 1 instead of prompting - clear it first, in the same elevated
            // process as the installer so there's only one UAC prompt
            var cmdArgs = $"/c (if exist \"{DcaInstallDir}\" rmdir /s /q \"{DcaInstallDir}\") & \"{installerPath}\" -q -varfile \"{varFilePath}\"";
            var psi = new ProcessStartInfo("cmd.exe", cmdArgs)
            {
                UseShellExecute = true, // installer self-elevates; ShellExecute lets Windows show the UAC prompt
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();

            return proc.ExitCode == 0
                ? (true, "DCA installed.")
                : (false, $"Installer exited with code {proc.ExitCode}.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
        }
    }

    private static string BuildDcaVarFile(string licenseKey)
    {
        string Escape(string s) => s.Replace("\\", "\\\\").Replace(":", "\\:");
        var sb = new StringBuilder();
        sb.AppendLine("# install4j response file for EKM Insight Data Collection Application");
        sb.AppendLine("createDesktopLinkAction$Boolean=true");
        sb.AppendLine("executeLauncherAction$Boolean=true");
        sb.AppendLine("sys.adminRights$Boolean=true");
        sb.AppendLine("sys.component.dca-core-component$Boolean=true");
        sb.AppendLine("sys.component.ews-proxy-component$Boolean=true");
        sb.AppendLine($"sys.installationDir={Escape(@"C:\Program Files (x86)\i2emfw")}");
        sb.AppendLine("sys.languageId=en");
        sb.AppendLine("sys.programGroupAllUsers$Boolean=true");
        sb.AppendLine("sys.programGroupDisabled$Boolean=false");
        sb.AppendLine("sys.programGroupName=EKM Insight Data Collection Application");
        sb.AppendLine($"licenceProvider={DcaProviderCode}");
        sb.AppendLine($"licenceKey={licenseKey}");
        return sb.ToString();
    }

    private const string DcaConsoleExe = @"C:\Program Files (x86)\i2emfw\i2econsole.exe";
    private const string DcaLogPath = @"C:\Program Files (x86)\i2emfw\log\i2emfw.log";

    public record DcaHealth(
        bool Installed,
        string ServiceStatus,
        string? ContractName,
        string? ExpiresLine,
        string? XmppStatus,
        bool ConsoleAvailable);

    public static DcaHealth GetDcaHealth()
    {
        bool installed = Directory.Exists(DcaInstallDir);
        string serviceStatus = "Not installed";

        if (installed)
        {
            try
            {
                using var sc = new ServiceController("i2emfw");
                serviceStatus = sc.Status.ToString();
            }
            catch (InvalidOperationException)
            {
                serviceStatus = "Service not found";
            }
        }

        string? contractName = null;
        string? expiresLine = null;
        string? xmppStatus = null;

        if (File.Exists(DcaLogPath))
        {
            try
            {
                using var stream = new FileStream(DcaLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                foreach (var line in reader.ReadToEnd().Split('\n'))
                {
                    if (line.Contains("Licence validated, using contract name:"))
                        contractName = line.Trim();
                    else if (line.Contains("Licence expires in"))
                        expiresLine = line.Trim();
                    else if (line.Contains("XMPP connection has been authenticated") || line.Contains("WATCH: Successfully connected"))
                        xmppStatus = "Connected";
                    else if (line.Contains("XMPP connection lost"))
                        xmppStatus = "Connection lost";
                }
            }
            catch (IOException)
            {
                // log locked by the running service - skip, service status still shown
            }
        }

        return new DcaHealth(installed, serviceStatus, contractName, expiresLine, xmppStatus, File.Exists(DcaConsoleExe));
    }

    public static void OpenDcaConsole()
    {
        if (!File.Exists(DcaConsoleExe))
            throw new InvalidOperationException("EKM console not found - is DCA installed?");
        Process.Start(new ProcessStartInfo(DcaConsoleExe) { UseShellExecute = true });
    }
}
