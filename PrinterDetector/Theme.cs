namespace PrinterDetector;

internal static class Theme
{
    public static readonly Color Navy = ColorTranslator.FromHtml("#0B1D33");
    public static readonly Color Accent = ColorTranslator.FromHtml("#1E88E5");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#1976D2");
    public static readonly Color AccentText = ColorTranslator.FromHtml("#1565C0");

    public static readonly Color TextDark = ColorTranslator.FromHtml("#0B1D33");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#5C6B7F");
    public static readonly Color TextFaint = ColorTranslator.FromHtml("#8A97A8");

    public static readonly Color BorderLight = ColorTranslator.FromHtml("#E1E5EB");
    public static readonly Color BorderLighter = ColorTranslator.FromHtml("#EEF1F5");
    public static readonly Color ButtonBorder = ColorTranslator.FromHtml("#CBD2DC");

    public static readonly Color GridHeaderBg = ColorTranslator.FromHtml("#F4F6F9");
    public static readonly Color PanelBg = ColorTranslator.FromHtml("#F8F9FB");
    public static readonly Color SelectedRowBg = ColorTranslator.FromHtml("#E8F1FC");
    public static readonly Color RowHoverBg = ColorTranslator.FromHtml("#F5F8FC");

    public static readonly Color StatusIdleFg = ColorTranslator.FromHtml("#43A047");
    public static readonly Color StatusIdleBg = ColorTranslator.FromHtml("#E8F5E9");
    public static readonly Color StatusPrintingFg = ColorTranslator.FromHtml("#1E88E5");
    public static readonly Color StatusPrintingBg = ColorTranslator.FromHtml("#E3F2FD");
    public static readonly Color StatusErrorFg = ColorTranslator.FromHtml("#D32F2F");
    public static readonly Color StatusErrorBg = ColorTranslator.FromHtml("#FDECEC");
    public static readonly Color StatusOfflineFg = ColorTranslator.FromHtml("#8A97A8");
    public static readonly Color StatusOfflineBg = ColorTranslator.FromHtml("#EEF1F5");

    public static readonly Color FlagWsdBg = ColorTranslator.FromHtml("#FFF3E0");
    public static readonly Color FlagWsdFg = ColorTranslator.FromHtml("#B85C00");
    public static readonly Color FlagRedirBg = ColorTranslator.FromHtml("#FDECEC");
    public static readonly Color FlagRedirFg = ColorTranslator.FromHtml("#B22222");
    public static readonly Color FlagReadyBg = ColorTranslator.FromHtml("#F1EBFA");
    public static readonly Color FlagReadyFg = ColorTranslator.FromHtml("#6A3FB5");

    public static readonly Color DangerText = ColorTranslator.FromHtml("#B22222");
    public static readonly Color DangerBorder = ColorTranslator.FromHtml("#EBC4C4");

    public static readonly Color ChipIpBg = ColorTranslator.FromHtml("#EEF3FA");
    public static readonly Color ChipIpFg = ColorTranslator.FromHtml("#1565C0");

    public static readonly Color NavBrandLabel = ColorTranslator.FromHtml("#7FA8D8");
    public static readonly Color NavItem = ColorTranslator.FromHtml("#D6E2F0");
    public static readonly Color NavPlaceholder = ColorTranslator.FromHtml("#9FB4CC");

    public static readonly Font FontBase = new("Segoe UI", 9f);
    public static readonly Font FontMono = new("Consolas", 9f);

    public static (Color Fg, Color Bg) StatusColors(string status) => status switch
    {
        "Idle" => (StatusIdleFg, StatusIdleBg),
        "Printing" => (StatusPrintingFg, StatusPrintingBg),
        "Warming Up" => (StatusPrintingFg, StatusPrintingBg),
        "Error" or "Stopped Printing" => (StatusErrorFg, StatusErrorBg),
        "Offline" => (StatusOfflineFg, StatusOfflineBg),
        _ => (StatusOfflineFg, StatusOfflineBg)
    };
}
