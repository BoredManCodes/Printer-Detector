using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace PrinterDetector;

public partial class Form1 : Form
{
    private readonly DataGridView _grid = new();
    private readonly FlatButton _refreshButton = new() { Text = "↻  Refresh", Style = ButtonStyle.Primary, Backdrop = Theme.Navy };
    private readonly FlatButton _scanButton = new() { Text = "Scan Network", Style = ButtonStyle.OutlineOnDark, Backdrop = Theme.Navy };
    private readonly Label _statusLabel = new();
    private readonly Label _countsLabel = new();
    private readonly PictureBox _logoBox = new();
    private readonly TextBox _filterBox = new();
    private readonly MarqueeBar _marquee = new();
    private readonly Panel _detailsPanel = new() { BackColor = Theme.PanelBg };
    private readonly Panel _detailsContent = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.PanelBg };
    private readonly ToolTip _tip = new();

    private BindingList<PrinterInfo> _printers = new();
    private List<PrinterInfo> _allPrinters = new();
    private int _consumablesColumnIndex = -1;
    private int _statusColumnIndex = -1;
    private int _flagsColumnIndex = -1;
    private int _selectedIndex = -1;
    private int _hoverRow = -1;

    public Form1()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Border Tech Solutions - Printer Detector";
        Icon = SetupIcon();
        MinimumSize = new Size(1200, 650);
        Width = 1440;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.White;

        var header = BuildHeader();
        var headerDivider = new Panel { Dock = DockStyle.Top, Height = 3, BackColor = Theme.PanelBg };
        var body = BuildBody();
        var brandBar = BuildBrandBar();
        var statusBar = BuildStatusBar();

        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(headerDivider);
        Controls.Add(brandBar);
        Controls.Add(statusBar);

        Load += async (_, _) => await RefreshPrintersAsync();
    }

    private static Bitmap? LoadLogoBitmap()
    {
        var asm = typeof(Form1).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;

        using var stream = asm.GetManifestResourceStream(name);
        return stream != null ? new Bitmap(stream) : null;
    }

    private Icon? SetupIcon()
    {
        try
        {
            using var bmp = LoadLogoBitmap();
            return bmp != null ? Icon.FromHandle(bmp.GetHicon()) : null;
        }
        catch
        {
            return null;
        }
    }

    // ---------- Header ----------

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Navy };

        _logoBox.SizeMode = PictureBoxSizeMode.Zoom;
        _logoBox.Size = new Size(40, 40);
        _logoBox.Location = new Point(20, 12);
        _logoBox.Image = LoadLogoBitmap();
        header.Controls.Add(_logoBox);

        var brandLabel = new Label
        {
            Text = "BORDER TECH SOLUTIONS",
            ForeColor = Theme.NavBrandLabel,
            BackColor = Theme.Navy,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(72, 15)
        };
        header.Controls.Add(brandLabel);

        var titleLabel = new Label
        {
            Text = "Printer Detector",
            ForeColor = Color.White,
            BackColor = Theme.Navy,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(72, 30)
        };
        header.Controls.Add(titleLabel);

        var utilitiesBtn = MakeHeaderMenuButton("Utilities  ▾", (s, _) =>
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Install EKM", null, (_, _) => ShowInstallDcaDialog());
            m.Items.Add("EKM Status", null, (_, _) => ShowDcaStatusDialog());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Configure as network scanning host...", null, (_, _) => ShowScanHostDialog());
            m.Show((Control)s!, new Point(0, ((Control)s!).Height));
        });
        int menuStart = Math.Max(brandLabel.Right, titleLabel.Right) + 32;
        utilitiesBtn.Location = new Point(menuStart, 16);
        header.Controls.Add(utilitiesBtn);

        var bundlesBtn = MakeHeaderMenuButton("Bundles  ▾", (s, _) =>
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Save printers to a bundle...", null, (_, _) => ShowBundleDialog(BundleMode.Save));
            m.Items.Add("Install printers from a bundle...", null, (_, _) => ShowBundleDialog(BundleMode.Install));
            m.Items.Add("Remove several printers...", null, (_, _) => ShowBundleDialog(BundleMode.Remove));
            m.Show((Control)s!, new Point(0, ((Control)s!).Height));
        });
        bundlesBtn.Location = new Point(utilitiesBtn.Right + 8, 16);
        header.Controls.Add(bundlesBtn);
        int afterUtilities = bundlesBtn.Right + 8;

        var systemToolsBtn = MakeHeaderMenuButton("System tools  ▾", (s, _) =>
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Add a printer...", null, (_, _) => TryRun(PrinterActions.OpenAddPrinterWizard));
            m.Items.Add("Devices and Printers", null, (_, _) => TryRun(PrinterActions.OpenDevicesAndPrinters));
            m.Items.Add("Print Management console", null, (_, _) => TryRun(PrinterActions.OpenPrintManagementConsole));
            m.Items.Add("Device Manager", null, (_, _) => TryRun(PrinterActions.OpenDeviceManager));
            m.Items.Add("Services console", null, (_, _) => TryRun(PrinterActions.OpenServicesConsole));
            m.Items.Add("Event Viewer", null, (_, _) => TryRun(PrinterActions.OpenEventViewer));
            m.Items.Add("Open spool folder", null, (_, _) => TryRun(PrinterActions.OpenSpoolFolder));
            m.Items.Add("Export printer list (CSV)...", null, (_, _) => ExportCsv());
            m.Show((Control)s!, new Point(0, ((Control)s!).Height));
        });
        systemToolsBtn.Location = new Point(afterUtilities, 16);
        header.Controls.Add(systemToolsBtn);

        _filterBox.Width = 280;
        _filterBox.Height = 34;
        _filterBox.BorderStyle = BorderStyle.FixedSingle;
        _filterBox.BackColor = ColorTranslator.FromHtml("#16314F");
        _filterBox.ForeColor = Color.White;
        _filterBox.Font = new Font("Segoe UI", 9.5f);
        _filterBox.TextChanged += (_, _) => ApplyFilter();
        header.Controls.Add(_filterBox);

        header.HandleCreated += (_, _) => CueBanner.Set(_filterBox, "Filter by name, driver or IP…");

        _scanButton.Size = new Size(MeasureButtonWidth(_scanButton), 34);
        _scanButton.Click += (_, _) => ShowNetworkScanDialog();
        header.Controls.Add(_scanButton);

        _refreshButton.Size = new Size(MeasureButtonWidth(_refreshButton), 34);
        _refreshButton.Click += async (_, _) => await RefreshPrintersAsync();
        header.Controls.Add(_refreshButton);

        _marquee.Dock = DockStyle.Bottom;
        header.Controls.Add(_marquee);

        void Reflow()
        {
            int right = header.Width - 20;
            _refreshButton.Location = new Point(right - _refreshButton.Width, 15);
            right -= _refreshButton.Width + 8;
            _scanButton.Location = new Point(right - _scanButton.Width, 15);
            right -= _scanButton.Width + 16;
            _filterBox.Location = new Point(right - _filterBox.Width, 15);
        }

        header.Resize += (_, _) => Reflow();
        header.HandleCreated += (_, _) => Reflow();
        Shown += (_, _) => Reflow();

        return header;
    }

    private static int MeasureButtonWidth(FlatButton btn)
    {
        var textWidth = TextRenderer.MeasureText(btn.Text, btn.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        return textWidth + 40;
    }

    private static FlatButton MakeHeaderMenuButton(string text, EventHandler onClick)
    {
        var font = new Font("Segoe UI", 9f);
        var textWidth = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var btn = new FlatButton
        {
            Text = text,
            Style = ButtonStyle.Ghost,
            Size = new Size(textWidth + 24, 30),
            Radius = 4,
            Font = font,
            Backdrop = Theme.Navy
        };
        btn.Click += onClick;
        return btn;
    }

    // ---------- Body (grid + details panel) ----------

    private Panel BuildBody()
    {
        var body = new Panel { Dock = DockStyle.Fill };

        _detailsPanel.Width = 430;
        _detailsPanel.Dock = DockStyle.Right;
        var detailsBorder = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.BorderLight };
        _detailsPanel.Controls.Add(_detailsContent);
        _detailsPanel.Controls.Add(detailsBorder);

        BuildGrid();
        _grid.Dock = DockStyle.Fill;

        body.Controls.Add(_grid);
        body.Controls.Add(_detailsPanel);

        RenderEmptyDetails();

        return body;
    }

    private Panel BuildBrandBar()
    {
        var brandBar = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Navy };
        var brandLink = new LinkLabel
        {
            Text = "A Border Tech Solutions product — bordertechsolutions.com.au - Custom programs, websites, apps and more",
            AutoSize = true,
            LinkColor = Theme.NavItem,
            ActiveLinkColor = Color.LightSkyBlue,
            VisitedLinkColor = Theme.NavItem,
            BackColor = Theme.Navy,
            Font = new Font("Segoe UI", 9f)
        };
        brandLink.Location = new Point(20, (brandBar.Height - brandLink.Height) / 2);
        brandLink.LinkClicked += (_, _) => TryRun(() =>
            Process.Start(new ProcessStartInfo("https://bordertechsolutions.com.au") { UseShellExecute = true }));
        brandBar.Controls.Add(brandLink);

        var localIpLabel = new Label
        {
            Text = $"This PC: {PrinterActions.GetLocalIpAddress()}",
            AutoSize = true,
            ForeColor = Theme.NavItem,
            BackColor = Theme.Navy,
            Font = new Font("Segoe UI", 9f)
        };
        brandBar.Controls.Add(localIpLabel);
        void PositionIpLabel() => localIpLabel.Location =
            new Point(brandBar.Width - localIpLabel.Width - 20, (brandBar.Height - localIpLabel.Height) / 2);
        PositionIpLabel();
        brandBar.Resize += (_, _) => PositionIpLabel();

        return brandBar;
    }

    private Panel BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = ColorTranslator.FromHtml("#FAFBFC") };
        var topBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.BorderLight };
        bar.Controls.Add(topBorder);

        int barMidY = topBorder.Height + (bar.Height - topBorder.Height) / 2;

        _statusLabel.Text = "Ready.";
        _statusLabel.ForeColor = Theme.TextMuted;
        _statusLabel.BackColor = bar.BackColor;
        _statusLabel.Font = new Font("Segoe UI", 9.5f);
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(20, barMidY - _statusLabel.Height / 2);
        bar.Controls.Add(_statusLabel);

        _countsLabel.ForeColor = Theme.TextMuted;
        _countsLabel.BackColor = bar.BackColor;
        _countsLabel.Font = new Font("Segoe UI", 9.5f);
        _countsLabel.AutoSize = true;
        bar.Controls.Add(_countsLabel);
        bar.Resize += (_, _) => _countsLabel.Location = new Point(bar.Width - _countsLabel.Width - 20, barMidY - _countsLabel.Height / 2);
        _countsLabel.TextChanged += (_, _) => _countsLabel.Location = new Point(bar.Width - _countsLabel.Width - 20, barMidY - _countsLabel.Height / 2);

        return bar;
    }

    private void TryRun(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void TryRunWmi(Action action, string successMessage)
    {
        try
        {
            action();
            _statusLabel.Text = successMessage;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"printers_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Name,Driver,Status,Connection,Address,Consumables,Flags");
            foreach (var p in _allPrinters)
            {
                sb.AppendLine(string.Join(",", new[] { p.Name, p.Driver, p.Status, p.ConnectionType, p.Address, p.Consumables, p.WarningFlags }
                    .Select(CsvEscape)));
            }
            File.WriteAllText(dialog.FileName, sb.ToString());
            _statusLabel.Text = $"Exported {_allPrinters.Count} printer(s) to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ---------- Data refresh ----------

    private async Task RefreshPrintersAsync()
    {
        _refreshButton.Enabled = false;
        _marquee.Run(true);
        _statusLabel.Text = "Refreshing… querying SNMP (manual refresh only — no polling)";

        List<PrinterInfo> printers;
        try
        {
            printers = await Task.Run(PrinterService.GetPrinters);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to query printers: {ex.Message}", "Printer Detector",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _refreshButton.Enabled = true;
            _marquee.Run(false);
            _statusLabel.Text = "Refresh failed.";
            return;
        }

        _allPrinters = printers;
        _selectedIndex = printers.Count > 0 ? 0 : -1;
        ApplyFilter();

        var consumableTasks = printers
            .Where(p => p.ConnectionType == "Network")
            .Select(async p =>
            {
                var result = await PrinterService.GetConsumablesAsync(p.Address);
                p.Consumables = result.Summary;
                p.Supplies = result.Supplies;
                if (!IsDisposed)
                {
                    _grid.Refresh();
                    if (_selectedIndex >= 0 && _selectedIndex < _printers.Count && ReferenceEquals(_printers[_selectedIndex], p))
                        RenderDetails(p);
                }
            });

        await Task.WhenAll(consumableTasks);
        ShimmerStrip.Unsubscribe(_grid);

        _refreshButton.Enabled = true;
        _marquee.Run(false);
        _statusLabel.Text = $"Last refreshed {DateTime.Now:HH:mm:ss} · {printers.Count} printer(s) found.";
        UpdateCounts();
    }

    private void ApplyFilter()
    {
        var q = _filterBox.Text.Trim();
        var selected = _selectedIndex >= 0 && _selectedIndex < _printers.Count ? _printers[_selectedIndex] : null;

        var filtered = string.IsNullOrEmpty(q)
            ? _allPrinters
            : _allPrinters.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Driver.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Address.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _printers = new BindingList<PrinterInfo>(filtered);
        _grid.DataSource = _printers;

        _selectedIndex = selected != null ? filtered.IndexOf(selected) : -1;
        if (_selectedIndex < 0 && filtered.Count > 0) _selectedIndex = 0;

        UpdateEmptyState();
        UpdateCounts();
        RenderDetails(_selectedIndex >= 0 ? _printers[_selectedIndex] : null);
        _grid.Invalidate();
    }

    private void UpdateCounts()
    {
        int net = _allPrinters.Count(p => p.ConnectionType == "Network");
        int usb = _allPrinters.Count(p => p.ConnectionType == "USB");
        int other = _allPrinters.Count - net - usb;
        var parts = new List<string>();
        if (net > 0) parts.Add($"{net} network");
        if (usb > 0) parts.Add($"{usb} USB");
        if (other > 0) parts.Add($"{other} local");
        _countsLabel.Text = string.Join(" · ", parts);
    }
}
