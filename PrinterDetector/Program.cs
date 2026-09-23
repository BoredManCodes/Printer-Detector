using System.Diagnostics;
using System.Security.Principal;

namespace PrinterDetector;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!IsElevated() && PromptForElevation())
        {
            if (RelaunchElevated())
                return;
        }

        Application.Run(new Form1());
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool PromptForElevation()
    {
        var result = MessageBox.Show(
            "Run Printer Detector as administrator?\n\nAdmin is needed for some actions (restarting the spooler, installing EKM, etc). Choose No to run as the current user.",
            "Border Tech Solutions - Printer Detector",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        return result == DialogResult.Yes;
    }

    private static bool RelaunchElevated()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // user declined the UAC prompt - fall back to running as the current user
            return false;
        }
    }
}
