using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace PrinterDetector;

public class Form1 : Form
{
    private readonly DataGridView _grid = new();
    private readonly Button _refreshButton = new();
    private readonly Button _scanButton = new();
    private readonly Label _statusLabel = new();
    private readonly PictureBox _logoBox = new();
    private readonly Label _titleLabel = new();
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 300 };
    private BindingList<PrinterInfo> _printers = new();
    private int _consumablesColumnIndex = -1;
    private int _flagsColumnIndex = -1;
    private PrinterInfo? _hoverPrinter;
    private Form? _hoverPopup;

    public Form1()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Border Tech Solutions - Printer Detector";
        Width = 1400;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;

        SetupIcon();
        SetupHeader();
        SetupGrid();
        SetupFooter();
        SetupMenu();

        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_hoverPrinter != null)
                ShowConsumablesPopup(_hoverPrinter, Cursor.Position);
        };

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

    private void SetupIcon()
    {
        try
        {
            using var bmp = LoadLogoBitmap();
            if (bmp != null)
                Icon = Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            // missing/unreadable icon is non-fatal
        }
    }

    private void SetupHeader()
    {
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = ColorTranslator.FromHtml("#0B1D33")
        };

        _logoBox.SizeMode = PictureBoxSizeMode.Zoom;
        _logoBox.Width = 48;
        _logoBox.Height = 48;
        _logoBox.Location = new Point(8, 8);
        _logoBox.Image = LoadLogoBitmap();
        headerPanel.Controls.Add(_logoBox);

        _titleLabel.Text = "Border Tech Solutions — Printer Detector";
        _titleLabel.ForeColor = Color.White;
        _titleLabel.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        _titleLabel.AutoSize = true;
        _titleLabel.Location = new Point(64, 18);
        headerPanel.Controls.Add(_titleLabel);

        _refreshButton.Text = "Refresh";
        _refreshButton.Width = 100;
        _refreshButton.Height = 32;
        _refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refreshButton.Location = new Point(headerPanel.Width - 116, 16);
        _refreshButton.FlatStyle = FlatStyle.Flat;
        _refreshButton.FlatAppearance.BorderSize = 0;
        _refreshButton.BackColor = ColorTranslator.FromHtml("#1E88E5");
        _refreshButton.ForeColor = Color.White;
        _refreshButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _refreshButton.Cursor = Cursors.Hand;
        _refreshButton.Click += async (_, _) => await RefreshPrintersAsync();
        headerPanel.Controls.Add(_refreshButton);

        _scanButton.Text = "Scan Network";
        _scanButton.Width = 130;
        _scanButton.Height = 32;
        _scanButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _scanButton.Location = new Point(headerPanel.Width - 254, 16);
        _scanButton.FlatStyle = FlatStyle.Flat;
        _scanButton.FlatAppearance.BorderSize = 0;
        _scanButton.BackColor = ColorTranslator.FromHtml("#37474F");
        _scanButton.ForeColor = Color.White;
        _scanButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _scanButton.Cursor = Cursors.Hand;
        _scanButton.Click += (_, _) => ShowNetworkScanDialog();
        headerPanel.Controls.Add(_scanButton);

        headerPanel.Resize += (_, _) =>
        {
            _refreshButton.Location = new Point(headerPanel.Width - 116, 16);
            _scanButton.Location = new Point(headerPanel.Width - 254, 16);
        };

        Controls.Add(headerPanel);
    }

    private void SetupMenu()
    {
        var menuStrip = new MenuStrip();
        var utilitiesMenu = new ToolStripMenuItem("Utilities");
        utilitiesMenu.DropDownItems.Add("Install EKM", null, (_, _) => ShowInstallDcaDialog());
        utilitiesMenu.DropDownItems.Add("EKM Status", null, (_, _) => ShowDcaStatusDialog());
        menuStrip.Items.Add(utilitiesMenu);

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private void ShowInstallDcaDialog()
    {
        var dialog = new Form
        {
            Text = "Install EKM Insight DCA",
            Width = 420,
            Height = 200,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var keyLabel = new Label { Text = "License key:", Location = new Point(12, 18), AutoSize = true };
        var keyBox = new TextBox { Location = new Point(12, 40), Width = 380 };

        var installButton = new Button { Text = "Install", Location = new Point(12, 75), Width = 100, Height = 30 };
        var status = new Label
        {
            Location = new Point(12, 115),
            Width = 380,
            Height = 40,
            Text = "Downloads the latest build from EKM and installs it silently."
        };

        dialog.Controls.Add(keyLabel);
        dialog.Controls.Add(keyBox);
        dialog.Controls.Add(installButton);
        dialog.Controls.Add(status);

        installButton.Click += async (_, _) =>
        {
            var licenseKey = keyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                MessageBox.Show(dialog, "Paste a license key first.", "Install EKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            installButton.Enabled = false;
            keyBox.Enabled = false;
            var progress = new Progress<string>(msg => status.Text = msg);

            try
            {
                var (success, message) = await PrinterActions.DownloadAndInstallDcaAsync(licenseKey, progress);
                status.Text = message;
                MessageBox.Show(dialog, message, "Install EKM",
                    MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                if (success) dialog.Close();
            }
            catch (Exception ex)
            {
                status.Text = "Install failed: " + ex.Message;
                MessageBox.Show(dialog, "Install failed:\n" + ex.Message, "Install EKM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                installButton.Enabled = true;
                keyBox.Enabled = true;
            }
        };

        dialog.ShowDialog(this);
    }

    private void ShowDcaStatusDialog()
    {
        var health = PrinterActions.GetDcaHealth();

        var dialog = new Form
        {
            Text = "EKM Status",
            Width = 440,
            Height = 260,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var lines = new List<string>();
        lines.Add(health.Installed ? "DCA is installed." : "DCA is NOT installed.");
        if (health.Installed)
        {
            lines.Add($"Service (i2emfw): {health.ServiceStatus}");
            lines.Add(health.ContractName ?? "License: no validation entry found in current log.");
            if (health.ExpiresLine != null) lines.Add(health.ExpiresLine);
            lines.Add(health.XmppStatus != null ? $"XMPP: {health.XmppStatus}" : "XMPP: no recent entry in log.");
        }

        var text = new Label
        {
            Location = new Point(12, 12),
            Width = 400,
            Height = 150,
            Text = string.Join(Environment.NewLine + Environment.NewLine, lines)
        };
        dialog.Controls.Add(text);

        var openConsoleButton = new Button
        {
            Text = "Open EKM Console",
            Location = new Point(12, 175),
            Width = 160,
            Height = 30,
            Enabled = health.ConsoleAvailable
        };
        openConsoleButton.Click += (_, _) => TryRun(PrinterActions.OpenDcaConsole);
        dialog.Controls.Add(openConsoleButton);

        dialog.ShowDialog(this);
    }

    private void SetupGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", DataPropertyName = "Name" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Driver", HeaderText = "Driver", DataPropertyName = "Driver" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = "Status" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Connection", HeaderText = "Connection", DataPropertyName = "ConnectionType" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "IP / Port", DataPropertyName = "Address" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Flags", HeaderText = "Flags", DataPropertyName = "WarningFlags" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Consumables", HeaderText = "Consumables", DataPropertyName = "Consumables" });
        _consumablesColumnIndex = _grid.Columns["Consumables"]!.Index;
        _flagsColumnIndex = _grid.Columns["Flags"]!.Index;

        _grid.ShowCellToolTips = true;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.CellMouseDown += Grid_CellMouseDown;
        _grid.CellPainting += Grid_CellPainting;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        _grid.CellMouseEnter += Grid_CellMouseEnter;
        _grid.CellMouseLeave += Grid_CellMouseLeave;
        _grid.Scroll += (_, _) => CloseHoverPopup();
        _grid.MouseLeave += (_, _) => { _hoverTimer.Stop(); CloseHoverPopup(); };

        Controls.Add(_grid);
        _grid.BringToFront();
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        CloseHoverPopup();
        if (e.RowIndex < 0 || e.RowIndex >= _printers.Count) return;
        ShowPrinterMenu(_printers[e.RowIndex]);
    }

    private void Grid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        CloseHoverPopup();
        if (e.RowIndex < 0 || e.RowIndex >= _printers.Count) return;
        _grid.ClearSelection();
        _grid.Rows[e.RowIndex].Selected = true;
        ShowPrinterMenu(_printers[e.RowIndex]);
    }

    private void Grid_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _consumablesColumnIndex || e.RowIndex >= _printers.Count)
            return;

        _hoverPrinter = _printers[e.RowIndex];
        _hoverTimer.Start();
    }

    private void Grid_CellMouseLeave(object? sender, DataGridViewCellEventArgs e)
    {
        _hoverTimer.Stop();
        _hoverPrinter = null;
        CloseHoverPopup();
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex != _flagsColumnIndex || e.RowIndex < 0 || e.RowIndex >= _printers.Count)
            return;

        var printer = _printers[e.RowIndex];
        if (printer.IsWsd || printer.IsRedirected || printer.IsReadyPrint)
        {
            e.CellStyle!.ForeColor = printer.IsRedirected ? Color.Firebrick
                : printer.IsReadyPrint ? Color.MediumPurple
                : Color.DarkOrange;
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        }
    }

    private void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.ColumnIndex != _flagsColumnIndex || e.RowIndex < 0 || e.RowIndex >= _printers.Count)
            return;

        var printer = _printers[e.RowIndex];
        var tips = new List<string>();
        if (printer.IsWsd)
            tips.Add("WSD port - discovered via Web Services for Devices. Status and consumable queries over WSD are slower and less reliable than a standard TCP/IP port.");
        if (printer.IsRedirected)
            tips.Add("Redirected via Remote Desktop Easy Print - this printer belongs to the client PC of the current remote session, not this machine. Queue/properties/pause actions may not behave as expected.");
        if (printer.IsReadyPrint)
            tips.Add("Installed via Windows Ready Print (Microsoft IPP Class Driver) - a driverless IPP install rather than a vendor driver. Some vendor-specific features and SNMP consumable data may not be available.");

        e.ToolTipText = string.Join("\n\n", tips);
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _consumablesColumnIndex || e.RowIndex >= _printers.Count)
            return;

        var printer = _printers[e.RowIndex];
        if (printer.Supplies is not { Count: > 0 } supplies || e.Graphics == null)
            return;

        e.PaintBackground(e.CellBounds, true);

        var rect = e.CellBounds;
        rect.Inflate(-4, -6);

        int gap = 3;
        int barWidth = Math.Max(14, (rect.Width - (supplies.Count - 1) * gap) / supplies.Count);
        int x = rect.Left;

        foreach (var supply in supplies)
        {
            var barRect = new Rectangle(x, rect.Top, barWidth, rect.Height);
            e.Graphics.FillRectangle(Brushes.Gainsboro, barRect);

            if (supply.Percent is int pct)
            {
                int fillHeight = (int)Math.Round(barRect.Height * Math.Clamp(pct, 0, 100) / 100.0);
                var fillRect = new Rectangle(barRect.Left, barRect.Bottom - fillHeight, barRect.Width, fillHeight);
                using var brush = new SolidBrush(supply.BarColor);
                e.Graphics.FillRectangle(brush, fillRect);
            }

            e.Graphics.DrawRectangle(Pens.DarkGray, barRect);
            x += barWidth + gap;
        }

        e.Handled = true;
    }

    private void ShowConsumablesPopup(PrinterInfo printer, Point screenPoint)
    {
        CloseHoverPopup();

        const int width = 260;
        var inner = new Panel { Location = new Point(1, 1), BackColor = Color.White, AutoSize = false };

        var title = new Label
        {
            Text = printer.Name,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(10, 8)
        };
        inner.Controls.Add(title);

        int y = 32;

        if (printer.Supplies is { Count: > 0 } supplies)
        {
            foreach (var s in supplies)
            {
                var label = new Label
                {
                    Text = s.Percent is int p ? $"{s.Description} - {p}%" : $"{s.Description} - n/a",
                    AutoSize = true,
                    Location = new Point(10, y),
                    Font = new Font("Segoe UI", 8.5f)
                };
                inner.Controls.Add(label);
                y += 18;

                var barBack = new Panel { Location = new Point(10, y), Size = new Size(width, 14), BackColor = Color.Gainsboro };
                if (s.Percent is int pct)
                {
                    int fillWidth = (int)Math.Round(width * Math.Clamp(pct, 0, 100) / 100.0);
                    barBack.Controls.Add(new Panel { Location = new Point(0, 0), Size = new Size(fillWidth, 14), BackColor = s.BarColor });
                }
                inner.Controls.Add(barBack);
                y += 22;
            }
        }
        else
        {
            var label = new Label
            {
                Text = printer.Consumables,
                AutoSize = true,
                Location = new Point(10, y),
                Font = new Font("Segoe UI", 8.5f)
            };
            inner.Controls.Add(label);
            y += 22;
        }

        inner.Size = new Size(width + 20, y + 8);

        var popup = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            BackColor = Color.FromArgb(60, 60, 60),
            ClientSize = new Size(inner.Width + 2, inner.Height + 2)
        };
        popup.Controls.Add(inner);

        var screen = Screen.FromPoint(screenPoint).WorkingArea;
        int px = screenPoint.X + 12;
        int py = screenPoint.Y + 12;
        if (px + popup.Width > screen.Right) px = screenPoint.X - popup.Width - 12;
        if (py + popup.Height > screen.Bottom) py = screenPoint.Y - popup.Height - 12;
        popup.Location = new Point(px, py);

        popup.Show(this);
        _hoverPopup = popup;
    }

    private void CloseHoverPopup()
    {
        _hoverPopup?.Close();
        _hoverPopup?.Dispose();
        _hoverPopup = null;
    }

    private void ShowPrinterMenu(PrinterInfo printer)
    {
        var menu = new ContextMenuStrip();
        bool isNetwork = printer.ConnectionType == "Network";
        bool hasIp = IPAddress.TryParse(printer.Address, out _);

        menu.Items.Add("Search for driver (Google)", null, (_, _) => TryRun(() => PrinterActions.SearchDriverOnGoogle(printer.Name)));

        if (isNetwork)
        {
            menu.Items.Add("Open web GUI (http)", null, (_, _) => TryRun(() => PrinterActions.OpenWebGui(printer.Address)));
            menu.Items.Add("Open web GUI (https)", null, (_, _) => TryRun(() => PrinterActions.OpenWebGuiHttps(printer.Address)));
            menu.Items.Add(new ToolStripSeparator());
        }

        if (hasIp)
        {
            menu.Items.Add("View SNMP details", null, async (_, _) => await ShowSnmpDetailAsync(printer));
            menu.Items.Add("Ping / connectivity check", null, async (_, _) => await ShowPingAsync(printer));
            menu.Items.Add("Continuous ping (cmd window)", null, (_, _) => TryRun(() => PrinterActions.OpenContinuousPing(printer.Address)));
            menu.Items.Add("Traceroute (cmd window)", null, (_, _) => TryRun(() => PrinterActions.OpenTraceroute(printer.Address)));
            menu.Items.Add("Test raw print port (9100)", null, async (_, _) => await ShowPort9100Async(printer));
            menu.Items.Add("Get MAC address (ARP)", null, async (_, _) => await ShowArpAsync(printer));
            menu.Items.Add("Wake printer (Wake-on-LAN)", null, async (_, _) => await WakeOnLanAsync(printer));
            menu.Items.Add("Copy IP to clipboard", null, (_, _) => TryRun(() => PrinterActions.CopyAddressToClipboard(printer.Address)));
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Copy printer name to clipboard", null, (_, _) => TryRun(() => PrinterActions.CopyText(printer.Name)));
        menu.Items.Add("Open print queue", null, (_, _) => TryRun(() => PrinterActions.OpenPrintQueue(printer.Name)));
        menu.Items.Add("Printer properties", null, (_, _) => TryRun(() => PrinterActions.OpenProperties(printer.Name)));
        menu.Items.Add("Printing preferences", null, (_, _) => TryRun(() => PrinterActions.OpenPrintingPreferences(printer.Name)));
        menu.Items.Add("Print test page", null, (_, _) => TryRunWmi(() => PrinterActions.PrintTestPage(printer.Name), "Test page sent."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Pause", null, (_, _) => TryRunWmi(() => PrinterActions.Pause(printer.Name), "Printer paused."));
        menu.Items.Add("Resume", null, (_, _) => TryRunWmi(() => PrinterActions.Resume(printer.Name), "Printer resumed."));
        menu.Items.Add("Cancel all jobs", null, (_, _) => TryRunWmi(() => PrinterActions.CancelAllJobs(printer.Name), "All jobs cancelled."));
        menu.Items.Add("Set as default", null, (_, _) => TryRunWmi(() => PrinterActions.SetAsDefault(printer.Name), "Set as default printer."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Rename printer...", null, (_, _) => RenamePrinterDialog(printer));
        menu.Items.Add("Edit comment / location...", null, (_, _) => EditCommentLocationDialog(printer));
        menu.Items.Add("Share / unshare...", null, (_, _) => ShareDialog(printer));
        menu.Items.Add("Delete printer...", null, (_, _) => DeletePrinterConfirm(printer));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restart print spooler", null, async (_, _) => await RestartSpoolerAsync());
        menu.Items.Add("Clear stuck print queue (all printers)...", null, async (_, _) => await ClearStuckQueueAsync());

        var toolsMenu = new ToolStripMenuItem("System tools");
        toolsMenu.DropDownItems.Add("Add a printer...", null, (_, _) => TryRun(PrinterActions.OpenAddPrinterWizard));
        toolsMenu.DropDownItems.Add("Devices and Printers", null, (_, _) => TryRun(PrinterActions.OpenDevicesAndPrinters));
        toolsMenu.DropDownItems.Add("Print Management console", null, (_, _) => TryRun(PrinterActions.OpenPrintManagementConsole));
        toolsMenu.DropDownItems.Add("Device Manager", null, (_, _) => TryRun(PrinterActions.OpenDeviceManager));
        toolsMenu.DropDownItems.Add("Services console", null, (_, _) => TryRun(PrinterActions.OpenServicesConsole));
        toolsMenu.DropDownItems.Add("Event Viewer", null, (_, _) => TryRun(PrinterActions.OpenEventViewer));
        toolsMenu.DropDownItems.Add("Open spool folder", null, (_, _) => TryRun(PrinterActions.OpenSpoolFolder));
        toolsMenu.DropDownItems.Add("Export printer list (CSV)...", null, (_, _) => ExportCsv());
        menu.Items.Add(toolsMenu);

        menu.Show(Cursor.Position);
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private async Task ShowSnmpDetailAsync(PrinterInfo printer)
    {
        _statusLabel.Text = "Querying SNMP...";
        var detail = await PrinterService.GetSnmpDetailAsync(printer.Address);
        _statusLabel.Text = "Ready.";
        ShowTextDialog($"SNMP details - {printer.Name}", detail);
    }

    private async Task ShowPingAsync(PrinterInfo printer)
    {
        _statusLabel.Text = "Pinging...";
        var result = await PrinterActions.PingAsync(printer.Address);
        _statusLabel.Text = "Ready.";
        MessageBox.Show(this, result, $"Ping - {printer.Name}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ShowPort9100Async(PrinterInfo printer)
    {
        _statusLabel.Text = "Testing port 9100...";
        var result = await PrinterActions.TestPort9100Async(printer.Address);
        _statusLabel.Text = "Ready.";
        MessageBox.Show(this, result, $"Port test - {printer.Name}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ShowArpAsync(PrinterInfo printer)
    {
        _statusLabel.Text = "Resolving MAC address...";
        var (found, result) = await PrinterActions.GetArpMacAsync(printer.Address);
        _statusLabel.Text = "Ready.";
        if (found) TryRun(() => PrinterActions.CopyText(result));
        MessageBox.Show(this, found ? $"{result}\n\n(copied to clipboard)" : result,
            $"MAC address - {printer.Name}", MessageBoxButtons.OK, found ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task WakeOnLanAsync(PrinterInfo printer)
    {
        _statusLabel.Text = "Resolving MAC address...";
        var (found, result) = await PrinterActions.GetArpMacAsync(printer.Address);
        if (!found)
        {
            _statusLabel.Text = "Ready.";
            MessageBox.Show(this, result, "Wake-on-LAN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            PrinterActions.SendWakeOnLan(result);
            _statusLabel.Text = $"Wake-on-LAN packet sent to {result}.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Ready.";
            MessageBox.Show(this, ex.Message, "Wake-on-LAN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenamePrinterDialog(PrinterInfo printer)
    {
        var newName = PromptInput("Rename printer", $"New name for '{printer.Name}':", printer.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == printer.Name) return;

        TryRunWmi(() => PrinterActions.Rename(printer.Name, newName), $"Renamed to '{newName}'. Click Refresh to update the list.");
    }

    private void EditCommentLocationDialog(PrinterInfo printer)
    {
        (string Comment, string Location, bool Shared, string ShareName) current;
        try
        {
            current = PrinterActions.GetPrinterDetails(printer.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var result = PromptMultiInput($"Comment / location - {printer.Name}",
            ("Comment:", current.Comment), ("Location:", current.Location));
        if (result == null) return;

        TryRunWmi(() => PrinterActions.SetCommentLocation(printer.Name, result[0], result[1]), "Comment / location updated.");
    }

    private void ShareDialog(PrinterInfo printer)
    {
        (string Comment, string Location, bool Shared, string ShareName) current;
        try
        {
            current = PrinterActions.GetPrinterDetails(printer.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dialog = new Form
        {
            Text = $"Share - {printer.Name}",
            Width = 380,
            Height = 160,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var chk = new CheckBox { Text = "Share this printer", Checked = current.Shared, Location = new Point(12, 12), AutoSize = true };
        var lbl = new Label { Text = "Share name:", Location = new Point(12, 42), AutoSize = true };
        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(current.ShareName) ? printer.Name : current.ShareName,
            Location = new Point(12, 62),
            Width = 340
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(178, 96), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(259, 96), Width = 75 };
        dialog.Controls.AddRange(new Control[] { chk, lbl, box, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        TryRunWmi(() => PrinterActions.SetShared(printer.Name, chk.Checked, box.Text), chk.Checked ? "Printer shared." : "Printer unshared.");
    }

    private void DeletePrinterConfirm(PrinterInfo printer)
    {
        var confirm = MessageBox.Show(this, $"Delete printer '{printer.Name}'? This cannot be undone.",
            "Delete printer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        TryRunWmi(() => PrinterActions.Delete(printer.Name), $"Deleted '{printer.Name}'. Click Refresh to update the list.");
    }

    private async Task ClearStuckQueueAsync()
    {
        var confirm = MessageBox.Show(this,
            "This stops the print spooler, deletes ALL pending jobs for every printer on this PC, then restarts the spooler. Continue?",
            "Clear stuck print queue", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _statusLabel.Text = "Clearing stuck print queue...";
        try
        {
            await Task.Run(PrinterActions.ClearStuckQueue);
            _statusLabel.Text = "Print queue cleared.";
            MessageBox.Show(this, "Print queue cleared and spooler restarted.", "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed to clear print queue.";
            MessageBox.Show(this, "Failed (try running as Administrator):\n" + ex.Message,
                "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            foreach (var p in _printers)
            {
                sb.AppendLine(string.Join(",", new[] { p.Name, p.Driver, p.Status, p.ConnectionType, p.Address, p.Consumables, p.WarningFlags }
                    .Select(CsvEscape)));
            }
            File.WriteAllText(dialog.FileName, sb.ToString());
            _statusLabel.Text = $"Exported {_printers.Count} printer(s) to {dialog.FileName}.";
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

    private string? PromptInput(string title, string label, string initial)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 400,
            Height = 150,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var lbl = new Label { Text = label, Location = new Point(12, 12), AutoSize = true };
        var box = new TextBox { Text = initial, Location = new Point(12, 34), Width = 360 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(216, 70), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(297, 70), Width = 75 };
        dialog.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    private string[]? PromptMultiInput(string title, params (string Label, string Initial)[] fields)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 420,
            Height = 100 + fields.Length * 50,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var boxes = new TextBox[fields.Length];
        int y = 12;
        for (int i = 0; i < fields.Length; i++)
        {
            var lbl = new Label { Text = fields[i].Label, Location = new Point(12, y), AutoSize = true };
            var box = new TextBox { Text = fields[i].Initial, Location = new Point(12, y + 18), Width = 380 };
            boxes[i] = box;
            dialog.Controls.Add(lbl);
            dialog.Controls.Add(box);
            y += 50;
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(216, y), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(297, y), Width = 75 };
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK ? boxes.Select(b => b.Text).ToArray() : null;
    }

    private void ShowNetworkScanDialog()
    {
        var excludeIps = _printers
            .Where(p => p.ConnectionType == "Network" && IPAddress.TryParse(p.Address, out _))
            .Select(p => p.Address)
            .ToHashSet();

        var dialog = new Form
        {
            Text = "Scan network for printers",
            Width = 640,
            Height = 480,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP address" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hostname", HeaderText = "Hostname" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ports", HeaderText = "Open ports" });

        var status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Text = "Scanning local subnet(s)..."
        };

        dialog.Controls.Add(grid);
        dialog.Controls.Add(status);

        var results = new List<PrinterActions.DiscoveredDevice>();

        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= results.Count) return;
            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;
            var device = results[e.RowIndex];

            var rowMenu = new ContextMenuStrip();
            rowMenu.Items.Add("Open web GUI (http)", null, (_, _) => TryRun(() => PrinterActions.OpenWebGui(device.Ip)));
            rowMenu.Items.Add("Open web GUI (https)", null, (_, _) => TryRun(() => PrinterActions.OpenWebGuiHttps(device.Ip)));
            rowMenu.Items.Add("Search for driver (Google)", null,
                (_, _) => TryRun(() => PrinterActions.SearchDriverOnGoogle(device.Hostname != device.Ip ? device.Hostname : device.Ip)));
            rowMenu.Items.Add("Copy IP to clipboard", null, (_, _) => TryRun(() => PrinterActions.CopyText(device.Ip)));
            rowMenu.Items.Add("Ping", null, async (_, _) =>
            {
                var result = await PrinterActions.PingAsync(device.Ip);
                MessageBox.Show(dialog, result, $"Ping - {device.Ip}", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add("Add a printer...", null, (_, _) => TryRun(PrinterActions.OpenAddPrinterWizard));
            rowMenu.Show(Cursor.Position);
        };

        var cts = new CancellationTokenSource();
        dialog.FormClosed += (_, _) => cts.Cancel();

        var progress = new Progress<(int Done, int Total)>(p =>
            status.Text = $"Scanning... {p.Done}/{p.Total} host(s) checked, {results.Count} candidate(s) found.");

        dialog.Shown += async (_, _) =>
        {
            try
            {
                var found = await PrinterActions.ScanNetworkForPrintersAsync(excludeIps, progress, cts.Token);
                if (cts.IsCancellationRequested || dialog.IsDisposed) return;

                results.Clear();
                results.AddRange(found);
                grid.Rows.Clear();
                foreach (var d in found)
                    grid.Rows.Add(d.Ip, d.Hostname, string.Join(", ", d.OpenPorts));

                status.Text = found.Count > 0
                    ? $"Scan complete. {found.Count} candidate device(s) found (not already added to this PC)."
                    : "Scan complete. No un-added network printers found.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested && !dialog.IsDisposed)
                    status.Text = "Scan failed: " + ex.Message;
            }
        };

        dialog.ShowDialog(this);
    }

    private async Task RestartSpoolerAsync()
    {
        _statusLabel.Text = "Restarting spooler...";
        try
        {
            await Task.Run(PrinterActions.RestartSpooler);
            _statusLabel.Text = "Spooler restarted.";
            MessageBox.Show(this, "Print spooler restarted.", "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Spooler restart failed.";
            MessageBox.Show(this, "Failed to restart spooler (try running as Administrator):\n" + ex.Message,
                "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowTextDialog(string title, string text)
    {
        using var dialog = new Form
        {
            Text = title,
            Width = 560,
            Height = 420,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            Text = text
        };

        dialog.Controls.Add(box);
        dialog.ShowDialog(this);
    }

    private void SetupFooter()
    {
        var brandBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = ColorTranslator.FromHtml("#0B1D33")
        };

        var brandLink = new LinkLabel
        {
            Text = "A Border Tech Solutions product - bordertechsolutions.com.au",
            AutoSize = true,
            LinkColor = Color.White,
            ActiveLinkColor = Color.LightSkyBlue,
            VisitedLinkColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(8, 5)
        };
        brandLink.LinkClicked += (_, _) => TryRun(() =>
            Process.Start(new ProcessStartInfo("https://bordertechsolutions.com.au") { UseShellExecute = true }));
        brandBar.Controls.Add(brandLink);
        Controls.Add(brandBar);

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 24;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Padding = new Padding(8, 0, 0, 0);
        _statusLabel.Text = "Ready.";
        Controls.Add(_statusLabel);
    }

    private async Task RefreshPrintersAsync()
    {
        CloseHoverPopup();
        _refreshButton.Enabled = false;
        _statusLabel.Text = "Refreshing...";

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
            _statusLabel.Text = "Refresh failed.";
            return;
        }

        _printers = new BindingList<PrinterInfo>(printers);
        _grid.DataSource = _printers;

        var consumableTasks = printers
            .Where(p => p.ConnectionType == "Network")
            .Select(async p =>
            {
                var result = await PrinterService.GetConsumablesAsync(p.Address);
                p.Consumables = result.Summary;
                p.Supplies = result.Supplies;
                if (!IsDisposed)
                    _grid.Refresh();
            });

        await Task.WhenAll(consumableTasks);

        _refreshButton.Enabled = true;
        _statusLabel.Text = $"Last refreshed at {DateTime.Now:HH:mm:ss} — {printers.Count} printer(s) found.";
    }
}
