using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PrinterDetector;

/// <summary>
/// Saves print queues (driver package, port, tray/paper/weight settings) into named bundles next to the exe,
/// installs them on another PC, removes queues, and explains what a bundle holds. The work is done by the
/// PowerShell scripts embedded under Assets\Scripts so the logic matches the standalone PrinterCopy tool.
/// </summary>
internal static class QueueBundles
{
    public record BundleQueue(string Name, string Driver, string Where);

    private static string AppDir =>
        Path.GetDirectoryName(Environment.ProcessPath ?? Application.ExecutablePath) ?? AppContext.BaseDirectory;

    public static string BundleRoot => Path.Combine(AppDir, "Bundles");

    public static string BundleDir(string name) => Path.Combine(BundleRoot, name);

    public static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().TrimEnd('.');
    }

    /// <summary>Bundle names are the folders under Bundles that hold a manifest.json.</summary>
    public static List<string> FindBundles()
    {
        if (!Directory.Exists(BundleRoot)) return new List<string>();
        return Directory.GetDirectories(BundleRoot)
            .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static (string Header, List<BundleQueue> Queues) ReadBundle(string name)
    {
        var file = Path.Combine(BundleDir(name), "manifest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
        var root = doc.RootElement;

        string Str(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var header = $"Saved from {Str(root, "ExportedFrom")} at {Str(root, "ExportedAt")}";
        var list = new List<BundleQueue>();
        if (root.TryGetProperty("Printers", out var printers))
        {
            var items = printers.ValueKind == JsonValueKind.Array ? printers.EnumerateArray().ToList() : new List<JsonElement> { printers };
            foreach (var p in items)
            {
                var host = Str(p, "HostAddress");
                list.Add(new BundleQueue(Str(p, "Name"), Str(p, "DriverName"), host.Length > 0 ? host : Str(p, "PortName")));
            }
        }
        return (header, list);
    }

    // ---------- Script runner ----------

    private static string ExtractScript(string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PrinterDetector_scripts");
        Directory.CreateDirectory(dir);
        var asm = typeof(QueueBundles).Assembly;
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Script {fileName} is not embedded in this build.");
        var path = Path.Combine(dir, fileName);
        using var src = asm.GetManifestResourceStream(resource)!;
        using var dst = File.Create(path);
        src.CopyTo(dst);
        return path;
    }

    private static string WriteNames(IEnumerable<string> names)
    {
        var path = Path.Combine(Path.GetTempPath(), "PrinterDetector_scripts", "names_" + Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, names, new UTF8Encoding(true));
        return path;
    }

    private static async Task<string> RunScriptAsync(string script, IEnumerable<string> args, IProgress<string>? progress)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ExtractScript(script) }) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);

        var sb = new StringBuilder();
        void Line(string? l)
        {
            if (l == null) return;
            lock (sb) sb.AppendLine(l);
            progress?.Report(l);
        }

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => Line(e.Data);
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Line("ERROR: " + e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        return sb.ToString();
    }

    // ---------- Operations ----------

    /// <summary>Saves the named queues into Bundles\bundleName, adding to the bundle if it already exists.</summary>
    public static Task<string> SaveAsync(string bundleName, IReadOnlyList<string> queues, IProgress<string>? progress) =>
        RunScriptAsync("export.ps1", new[] { "-OutDir", BundleDir(bundleName), "-NamesFile", WriteNames(queues) }, progress);

    /// <summary>Installs the named queues from a bundle on this PC.</summary>
    public static Task<string> InstallAsync(string bundleName, IReadOnlyList<string> queues, bool replace, IProgress<string>? progress)
    {
        var args = new List<string> { "-BundleDir", BundleDir(bundleName), "-NamesFile", WriteNames(queues) };
        if (replace) args.Add("-Replace");
        return RunScriptAsync("import.ps1", args, progress);
    }

    public static Task<string> RemoveAsync(IReadOnlyList<string> queues, IProgress<string>? progress) =>
        RunScriptAsync("remove.ps1", new[] { "-NamesFile", WriteNames(queues) }, progress);

    /// <summary>Explains what a saved bundle holds and what installing it here would change.</summary>
    public static Task<string> InspectBundleAsync(string bundleName, IReadOnlyList<string> queues, bool replace) =>
        RunScriptAsync("inspect.ps1", BuildInspectArgs(BundleDir(bundleName), queues, replace, savedOnly: false), null);

    /// <summary>Shows what would be saved for local queues, without writing a bundle or copying drivers.</summary>
    public static async Task<string> InspectLocalAsync(IReadOnlyList<string> queues)
    {
        var preview = Path.Combine(Path.GetTempPath(), "PrinterDetector_preview_" + Guid.NewGuid().ToString("N"));
        try
        {
            var names = WriteNames(queues);
            var exportOut = await RunScriptAsync("export.ps1",
                new[] { "-OutDir", preview, "-NamesFile", names, "-NoDriverCopy" }, null);
            var report = await RunScriptAsync("inspect.ps1", BuildInspectArgs(preview, queues, false, savedOnly: true), null);
            var warn = exportOut.Contains("FAILED") || exportOut.Contains("WARNING") ? exportOut + Environment.NewLine : "";
            return warn + report;
        }
        finally
        {
            try { Directory.Delete(preview, true); } catch { /* best effort */ }
        }
    }

    private static List<string> BuildInspectArgs(string dir, IReadOnlyList<string> queues, bool replace, bool savedOnly)
    {
        var args = new List<string> { "-BundleDir", dir, "-NamesFile", WriteNames(queues) };
        if (replace) args.Add("-Replace");
        if (savedOnly) args.Add("-SavedOnly");
        return args;
    }
}
