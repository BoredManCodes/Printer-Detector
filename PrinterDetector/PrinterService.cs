using System.Drawing;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace PrinterDetector;

public static partial class PrinterService
{
    private static readonly ObjectIdentifier DescriptionOid = new("1.3.6.1.2.1.43.11.1.1.6");
    private static readonly ObjectIdentifier LevelOid = new("1.3.6.1.2.1.43.11.1.1.9");
    private static readonly ObjectIdentifier MaxCapacityOid = new("1.3.6.1.2.1.43.11.1.1.8");
    private static readonly ObjectIdentifier SupplyUnitOid = new("1.3.6.1.2.1.43.11.1.1.7");
    private static readonly ObjectIdentifier SysDescrOid = new("1.3.6.1.2.1.1.1.0");
    private static readonly ObjectIdentifier LifeCountOid = new("1.3.6.1.2.1.43.10.2.1.4");
    private static readonly ObjectIdentifier ErrorStateOid = new("1.3.6.1.2.1.25.3.5.1.2");

    private static readonly string[] VirtualPortPrefixes = { "nul", "portprompt", "shrfax", "file", "xpsport" };

    [GeneratedRegex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}")]
    private static partial Regex IpPrefixRegex();

    public static List<PrinterInfo> GetPrinters()
    {
        var tcpIpPorts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var portSearcher = new ManagementObjectSearcher("SELECT Name, HostAddress FROM Win32_TCPIPPrinterPort");
            foreach (ManagementObject port in portSearcher.Get())
            {
                var name = port["Name"]?.ToString();
                var host = port["HostAddress"]?.ToString();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(host))
                    tcpIpPorts[name] = host;
            }
        }
        catch
        {
            // no TCP/IP printer ports readable - network printers fall back to raw port name
        }

        try
        {
            // MSFT_PrinterPort (the class behind Get-PrinterPort) also resolves WSD ports,
            // which Win32_TCPIPPrinterPort above does not cover.
            var scope = new ManagementScope(@"root\StandardCimv2");
            using var wsdSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, PrinterHostAddress FROM MSFT_PrinterPort"));
            foreach (ManagementObject port in wsdSearcher.Get())
            {
                var name = port["Name"]?.ToString();
                var host = port["PrinterHostAddress"]?.ToString();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(host))
                    tcpIpPorts[name] = host;
            }
        }
        catch
        {
            // MSFT_PrinterPort unavailable on this system - WSD ports fall back to raw port name
        }

        var printers = new List<PrinterInfo>();

        try
        {
            using var printerSearcher = new ManagementObjectSearcher("SELECT Name, DriverName, PortName, PrinterStatus FROM Win32_Printer");
            foreach (ManagementObject printer in printerSearcher.Get())
            {
                string name = printer["Name"]?.ToString() ?? "Unknown";
                string driver = printer["DriverName"]?.ToString() ?? "Unknown";
                string portName = printer["PortName"]?.ToString() ?? "";
                string status = MapStatus(printer["PrinterStatus"]);

                var (connectionType, address) = ClassifyConnection(portName, tcpIpPorts);

                printers.Add(new PrinterInfo
                {
                    Name = name,
                    Driver = driver,
                    Status = status,
                    ConnectionType = connectionType,
                    Address = address,
                    IsWsd = portName.StartsWith("WSD-", StringComparison.OrdinalIgnoreCase),
                    IsRedirected = driver.Contains("Easy Print", StringComparison.OrdinalIgnoreCase) || IsRedirectedPortName(portName),
                    IsReadyPrint = driver.Contains("IPP Class Driver", StringComparison.OrdinalIgnoreCase),
                    Consumables = connectionType switch
                    {
                        "Network" => "Checking...",
                        "USB" => "N/A (USB)",
                        "Virtual" => "N/A (Virtual)",
                        _ => "N/A (Local)"
                    }
                });
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("WMI printer query failed: " + ex.Message, ex);
        }

        return printers;
    }

    private static string MapStatus(object? raw)
    {
        if (raw == null) return "Unknown";

        return Convert.ToInt32(raw) switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "Idle",
            4 => "Printing",
            5 => "Warming Up",
            6 => "Stopped Printing",
            7 => "Offline",
            _ => "Unknown"
        };
    }

    private static (string ConnectionType, string Address) ClassifyConnection(string portName, Dictionary<string, string> tcpIpPorts)
    {
        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            return ("USB", portName);

        if (portName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) || portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return ("Local", portName);

        if (VirtualPortPrefixes.Any(prefix => portName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return ("Virtual", portName);

        if (tcpIpPorts.TryGetValue(portName, out var ip))
            return ("Network", ip);

        // some drivers (Brother, HP) embed the model name after the IP in the port name itself
        var match = IpPrefixRegex().Match(portName);
        if (match.Success)
            return ("Network", match.Value);

        return ("Network", portName);
    }

    private static bool IsRedirectedPortName(string portName)
    {
        // Remote Desktop session-redirected printers use port names like "TS001", "TS002", ...
        if (!portName.StartsWith("TS", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = portName[2..];
        return rest.Length > 0 && rest.All(char.IsDigit);
    }

    public static async Task<ConsumablesResult> GetConsumablesAsync(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
            return new ConsumablesResult { Summary = "Unavailable", Supplies = null };

        try
        {
            var endpoint = new IPEndPoint(ip, 161);
            var community = new OctetString("public");

            var descList = new List<Variable>();
            var levelList = new List<Variable>();
            var maxList = new List<Variable>();

            var walkTask = Task.Run(() =>
            {
                Messenger.Walk(VersionCode.V1, endpoint, community, DescriptionOid, descList, 1000, WalkMode.WithinSubtree);
                Messenger.Walk(VersionCode.V1, endpoint, community, LevelOid, levelList, 1000, WalkMode.WithinSubtree);
                Messenger.Walk(VersionCode.V1, endpoint, community, MaxCapacityOid, maxList, 1000, WalkMode.WithinSubtree);
            });

            var completed = await Task.WhenAny(walkTask, Task.Delay(3500));
            if (completed != walkTask || descList.Count == 0)
                return new ConsumablesResult { Summary = "Unavailable", Supplies = null };

            var supplies = new List<SupplyReading>();
            for (int i = 0; i < descList.Count; i++)
            {
                string desc = descList[i].Data.ToString() ?? $"Supply {i + 1}";
                string levelStr = i < levelList.Count ? levelList[i].Data.ToString() ?? "" : "";
                string maxStr = i < maxList.Count ? maxList[i].Data.ToString() ?? "" : "";

                int? pct = null;
                if (int.TryParse(levelStr, out int level) && int.TryParse(maxStr, out int max) && max > 0 && level >= 0)
                    pct = (int)Math.Round(level * 100.0 / max);

                supplies.Add(new SupplyReading { Description = desc, Percent = pct, BarColor = InferBarColor(desc) });
            }

            var withReadings = supplies.Where(s => s.Percent.HasValue).ToList();
            string summary = withReadings.Count > 0
                ? string.Join(", ", withReadings.Select(s => $"{s.Description}: {s.Percent}%"))
                : "Unavailable";

            return new ConsumablesResult { Summary = summary, Supplies = supplies.Count > 0 ? supplies : null };
        }
        catch
        {
            return new ConsumablesResult { Summary = "Unavailable", Supplies = null };
        }
    }

    private static Color InferBarColor(string description)
    {
        var d = description.ToLowerInvariant();
        if (d.Contains("cyan")) return Color.DeepSkyBlue;
        if (d.Contains("magenta")) return Color.Magenta;
        if (d.Contains("yellow")) return Color.Gold;
        if (d.Contains("black")) return Color.Black;
        if (d.Contains("waste") || d.Contains("maintenance")) return Color.SaddleBrown;
        if (d.Contains("drum") || d.Contains("unit") || d.Contains("fuser")) return Color.SlateGray;
        return Color.SteelBlue;
    }

    public static async Task<string> GetSnmpDetailAsync(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
            return "Not a valid IP address - SNMP detail unavailable.";

        var endpoint = new IPEndPoint(ip, 161);
        var community = new OctetString("public");
        var sb = new StringBuilder();

        var walkTask = Task.Run(() =>
        {
            try
            {
                var sysDescr = Messenger.Get(VersionCode.V1, endpoint, community,
                    new List<Variable> { new(SysDescrOid) }, 1500);
                sb.AppendLine("Device: " + sysDescr[0].Data);
            }
            catch
            {
                sb.AppendLine("Device: unavailable");
            }

            sb.AppendLine();

            try
            {
                var errorList = new List<Variable>();
                Messenger.Walk(VersionCode.V1, endpoint, community, ErrorStateOid, errorList, 1000, WalkMode.WithinSubtree);
                string errorText = errorList.Count > 0 && errorList[0].Data is OctetString errOctet
                    ? DecodeErrorState(errOctet.GetRaw())
                    : "unavailable";
                sb.AppendLine("Error state: " + errorText);
            }
            catch
            {
                sb.AppendLine("Error state: unavailable");
            }

            try
            {
                var lifeCountList = new List<Variable>();
                Messenger.Walk(VersionCode.V1, endpoint, community, LifeCountOid, lifeCountList, 1000, WalkMode.WithinSubtree);
                sb.AppendLine("Lifetime page count: " + (lifeCountList.Count > 0 ? lifeCountList[0].Data.ToString() : "unavailable"));
            }
            catch
            {
                sb.AppendLine("Lifetime page count: unavailable");
            }

            sb.AppendLine();
            sb.AppendLine("Consumables:");

            try
            {
                var descList = new List<Variable>();
                var levelList = new List<Variable>();
                var maxList = new List<Variable>();
                var unitList = new List<Variable>();

                Messenger.Walk(VersionCode.V1, endpoint, community, DescriptionOid, descList, 1000, WalkMode.WithinSubtree);
                Messenger.Walk(VersionCode.V1, endpoint, community, LevelOid, levelList, 1000, WalkMode.WithinSubtree);
                Messenger.Walk(VersionCode.V1, endpoint, community, MaxCapacityOid, maxList, 1000, WalkMode.WithinSubtree);
                Messenger.Walk(VersionCode.V1, endpoint, community, SupplyUnitOid, unitList, 1000, WalkMode.WithinSubtree);

                if (descList.Count == 0)
                    sb.AppendLine("  (none reported)");

                for (int i = 0; i < descList.Count; i++)
                {
                    string desc = descList[i].Data.ToString() ?? $"Supply {i + 1}";
                    string levelStr = i < levelList.Count ? levelList[i].Data.ToString() ?? "" : "";
                    string maxStr = i < maxList.Count ? maxList[i].Data.ToString() ?? "" : "";
                    string unitStr = i < unitList.Count ? unitList[i].Data.ToString() ?? "" : "";

                    string pctText = "n/a";
                    if (int.TryParse(levelStr, out int level) && int.TryParse(maxStr, out int max) && max > 0 && level >= 0)
                        pctText = $"{Math.Round(level * 100.0 / max)}%";

                    sb.AppendLine($"  {desc}: {pctText}  (raw level {levelStr}, max {maxStr}, unit code {unitStr})");
                }
            }
            catch
            {
                sb.AppendLine("  SNMP walk failed.");
            }
        });

        var completed = await Task.WhenAny(walkTask, Task.Delay(6000));
        return completed == walkTask ? sb.ToString() : "SNMP query timed out - printer unreachable or SNMP disabled.";
    }

    private static string DecodeErrorState(byte[] data)
    {
        if (data.Length == 0) return "none reported";

        var flags = new List<string>();
        byte b1 = data[0];
        if ((b1 & 0x80) != 0) flags.Add("Low Paper");
        if ((b1 & 0x40) != 0) flags.Add("No Paper");
        if ((b1 & 0x20) != 0) flags.Add("Low Toner");
        if ((b1 & 0x10) != 0) flags.Add("No Toner");
        if ((b1 & 0x08) != 0) flags.Add("Door Open");
        if ((b1 & 0x04) != 0) flags.Add("Jammed");
        if ((b1 & 0x02) != 0) flags.Add("Offline");
        if ((b1 & 0x01) != 0) flags.Add("Service Requested");

        if (data.Length > 1)
        {
            byte b2 = data[1];
            if ((b2 & 0x80) != 0) flags.Add("Input Tray Missing");
            if ((b2 & 0x40) != 0) flags.Add("Output Tray Missing");
            if ((b2 & 0x20) != 0) flags.Add("Marker Supplies Missing");
            if ((b2 & 0x10) != 0) flags.Add("Output Near Full");
            if ((b2 & 0x08) != 0) flags.Add("Output Full");
            if ((b2 & 0x04) != 0) flags.Add("Input Tray Empty");
            if ((b2 & 0x02) != 0) flags.Add("Overdue Preventive Maintenance");
        }

        return flags.Count > 0 ? string.Join(", ", flags) : "No errors reported";
    }
}
