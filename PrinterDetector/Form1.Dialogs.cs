using System.Drawing.Drawing2D;
using System.Net;

namespace PrinterDetector;

public partial class Form1
{
    private static (FlatButton Ok, FlatButton Cancel) AddOkCancelButtons(Form dialog, int y)
    {
        int right = dialog.ClientSize.Width - 20;
        var cancel = new FlatButton { Text = "Cancel", Style = ButtonStyle.Outline, Size = new Size(90, 34) };
        cancel.Location = new Point(right - cancel.Width, y);
        var ok = new FlatButton { Text = "OK", Style = ButtonStyle.Primary, Size = new Size(90, 34) };
        ok.Location = new Point(cancel.Left - 12 - ok.Width, y);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        return (ok, cancel);
    }

    private static Form NewDialog(string title, int width, int height, Form owner)
    {
        var dialog = new Form
        {
            Text = title,
            ClientSize = new Size(width, height),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        return dialog;
    }

    // ---------- Install EKM ----------

    private void ShowInstallDcaDialog()
    {
        var dialog = NewDialog("Install EKM Insight DCA", 460, 250, this);

        var keyLabel = new Label
        {
            Text = "LICENSE KEY", Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = Theme.TextFaint,
            BackColor = dialog.BackColor, Location = new Point(20, 20), AutoSize = true
        };
        dialog.Controls.Add(keyLabel);

        var keyBox = new TextBox
        {
            Location = new Point(20, 40), Size = new Size(420, 26), Font = Theme.FontMono,
            BorderStyle = BorderStyle.FixedSingle
        };
        dialog.Controls.Add(keyBox);
        dialog.HandleCreated += (_, _) => CueBanner.Set(keyBox, "XXXX-XXXX-XXXX-XXXX");

        var status = new Label
        {
            Text = "Downloads the latest build from EKM and installs it silently. Requires administrator rights.",
            Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.TextMuted,
            BackColor = dialog.BackColor, Location = new Point(20, 76), Size = new Size(420, 40)
        };
        dialog.Controls.Add(status);

        var cancelButton = new FlatButton { Text = "Cancel", Style = ButtonStyle.Outline, Size = new Size(90, 34), Location = new Point(240, 190) };
        var installButton = new FlatButton { Text = "Install", Style = ButtonStyle.Primary, Size = new Size(100, 34), Location = new Point(340, 190) };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(cancelButton);
        dialog.Controls.Add(installButton);

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

    // ---------- EKM status ----------

    private void ShowDcaStatusDialog()
    {
        var health = PrinterActions.GetDcaHealth();
        var dialog = NewDialog("EKM Status", 460, 300, this);

        var (dotColor, headline) = health.Installed
            ? (Theme.StatusIdleFg, "DCA installed and running")
            : (Theme.StatusErrorFg, "DCA is not installed");

        var dot = new Panel { Size = new Size(10, 10), Location = new Point(20, 24) };
        dot.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(dotColor); e.Graphics.FillEllipse(b, 0, 0, 9, 9); };
        dialog.Controls.Add(dot);

        var headlineLabel = new Label
        {
            Text = headline, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Theme.TextDark,
            BackColor = dialog.BackColor, Location = new Point(38, 18), AutoSize = true
        };
        dialog.Controls.Add(headlineLabel);

        var rows = new List<(string K, string V, bool Bold)>();
        if (health.Installed)
        {
            rows.Add(("Service (i2emfw)", health.ServiceStatus, true));
            rows.Add(("Contract", health.ContractName ?? "No validation entry found in current log.", false));
            if (health.ExpiresLine != null) rows.Add(("Expires", health.ExpiresLine, false));
            rows.Add(("XMPP", health.XmppStatus ?? "No recent entry in log.", false));
        }

        int ry = 52;
        foreach (var (k, v, bold) in rows)
        {
            dialog.Controls.Add(new Panel { Location = new Point(20, ry), Size = new Size(420, 1), BackColor = Theme.BorderLighter });
            var kFont = new Font("Segoe UI", 8.75f);
            var vFont = new Font("Segoe UI", 8.75f, bold ? FontStyle.Bold : FontStyle.Regular);
            int rowHeight = Math.Max(LineHeight(kFont), LineHeight(vFont));
            var kLabel = new Label { Text = k, Font = kFont, ForeColor = Theme.TextMuted, BackColor = dialog.BackColor, Location = new Point(20, ry + 8), Size = new Size(120, rowHeight) };
            var vLabel = new Label
            {
                Text = v, Font = vFont, ForeColor = Theme.TextDark,
                BackColor = dialog.BackColor, Location = new Point(140, ry + 8), Size = new Size(300, rowHeight), AutoEllipsis = true
            };
            dialog.Controls.Add(kLabel);
            dialog.Controls.Add(vLabel);
            ry += 34;
        }

        var closeButton = new FlatButton { Text = "Close", Style = ButtonStyle.Primary, Size = new Size(90, 34), Location = new Point(350, 250) };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(closeButton);

        var consoleButton = new FlatButton
        {
            Text = "Open EKM Console", Style = ButtonStyle.Outline, Size = new Size(160, 34), Location = new Point(20, 250),
            Enabled = health.ConsoleAvailable
        };
        consoleButton.Click += (_, _) => TryRun(PrinterActions.OpenDcaConsole);
        dialog.Controls.Add(consoleButton);

        dialog.ShowDialog(this);
    }

    // ---------- Network scan ----------

    private void ShowNetworkScanDialog()
    {
        var excludeIps = _allPrinters
            .Where(p => p.ConnectionType == "Network" && IPAddress.TryParse(p.Address, out _))
            .Select(p => p.Address)
            .ToHashSet();

        var dialog = NewDialog("Scan network for printers", 720, 520, this);

        var titleLabel = new Label
        {
            Text = "Devices not yet added to this PC", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = Theme.TextDark,
            BackColor = Color.White, Location = new Point(20, 16), AutoSize = true
        };
        dialog.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = $"Sweeping the local subnet for open ports {string.Join(", ", PrinterActions.ScanPortsList)}",
            Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextMuted,
            BackColor = Color.White, Location = new Point(20, 38), AutoSize = true
        };
        dialog.Controls.Add(subtitleLabel);

        var rescanButton = new FlatButton { Text = "Rescan", Style = ButtonStyle.Outline, Size = new Size(90, 30), Location = new Point(610, 22) };
        dialog.Controls.Add(rescanButton);
        dialog.Controls.Add(new Panel { Location = new Point(0, 68), Size = new Size(720, 1), BackColor = Theme.BorderLight });

        var grid = new DataGridView
        {
            Location = new Point(0, 69), Size = new Size(720, 520 - 69 - 44),
            AutoGenerateColumns = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false,
            BackgroundColor = Color.White, RowTemplate = { Height = 46 }, ColumnHeadersHeight = 38
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.GridHeaderBg;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.SelectionBackColor = Color.White;
        grid.DefaultCellStyle.SelectionForeColor = Theme.TextDark;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP ADDRESS", FillWeight = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hostname", HeaderText = "HOSTNAME", FillWeight = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ports", HeaderText = "OPEN PORTS", FillWeight = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Actions", HeaderText = "", FillWeight = 140 });
        dialog.Controls.Add(grid);

        var status = new Label
        {
            Location = new Point(20, 520 - 44 + 14), Size = new Size(500, 20),
            Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextMuted, BackColor = dialog.BackColor,
            Text = "Scanning local subnet(s)…"
        };
        dialog.Controls.Add(new Panel { Location = new Point(0, 520 - 44), Size = new Size(720, 1), BackColor = Theme.BorderLight });
        dialog.Controls.Add(status);

        var results = new List<PrinterActions.DiscoveredDevice>();

        var addPrinterButton = new FlatButton { Text = "Add a printer…", Style = ButtonStyle.Primary, Size = new Size(140, 32), Location = new Point(720 - 20 - 140, 520 - 44 + 6) };
        addPrinterButton.Click += (_, _) => TryRun(PrinterActions.OpenAddPrinterWizard);
        dialog.Controls.Add(addPrinterButton);

        (Rectangle Web, Rectangle Ping) ActionRects(Rectangle cellBounds)
        {
            var r = cellBounds;
            r.Inflate(-8, -9);
            var web = new Rectangle(r.Left, r.Top, 64, r.Height);
            var ping = new Rectangle(web.Right + 6, r.Top, 46, r.Height);
            return (web, ping);
        }

        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= results.Count || e.Graphics == null) return;
            var device = results[e.RowIndex];
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bgBrush = new SolidBrush(Color.White)) e.Graphics.FillRectangle(bgBrush, e.CellBounds);
            using (var pen = new Pen(Theme.BorderLighter)) e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);

            if (e.ColumnIndex == 0)
            {
                var r = e.CellBounds; r.Inflate(-16, 0);
                TextRenderer.DrawText(e.Graphics, device.Ip, Theme.FontMono, r, Theme.TextDark, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                e.Handled = true;
            }
            else if (e.ColumnIndex == 1)
            {
                var r = e.CellBounds; r.Inflate(-16, 0);
                TextRenderer.DrawText(e.Graphics, device.Hostname, Theme.FontBase, r, Theme.TextDark, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                e.Handled = true;
            }
            else if (e.ColumnIndex == 2)
            {
                int x = e.CellBounds.Left + 16;
                foreach (var port in device.OpenPorts)
                {
                    var text = port.ToString();
                    var size = TextRenderer.MeasureText(e.Graphics, text, Theme.FontMono, Size.Empty, TextFormatFlags.NoPadding);
                    var chip = new Rectangle(x, e.CellBounds.Top + e.CellBounds.Height / 2 - 9, size.Width + 12, 18);
                    if (chip.Right > e.CellBounds.Right - 8) break;
                    Gfx.FillRoundedRect(e.Graphics, chip, 4, Theme.ChipIpBg);
                    TextRenderer.DrawText(e.Graphics, text, new Font("Consolas", 7.5f), chip, Theme.ChipIpFg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x = chip.Right + 4;
                }
                e.Handled = true;
            }
            else if (e.ColumnIndex == 3)
            {
                var (web, ping) = ActionRects(e.CellBounds);
                Gfx.DrawRoundedRect(e.Graphics, web, 5, Theme.ButtonBorder);
                TextRenderer.DrawText(e.Graphics, "Web GUI", new Font("Segoe UI", 7.5f, FontStyle.Bold), web, Theme.TextDark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Gfx.DrawRoundedRect(e.Graphics, ping, 5, Theme.ButtonBorder);
                TextRenderer.DrawText(e.Graphics, "Ping", new Font("Segoe UI", 7.5f, FontStyle.Bold), ping, Theme.TextDark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                e.Handled = true;
            }
        };

        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= results.Count || e.ColumnIndex != 3) return;
            var device = results[e.RowIndex];
            var cellBounds = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            var (web, ping) = ActionRects(cellBounds);
            var pt = grid.PointToClient(Cursor.Position);
            if (web.Contains(pt)) TryRun(() => PrinterActions.OpenWebGui(device.Ip));
            else if (ping.Contains(pt))
            {
                _ = Task.Run(() => PrinterActions.PingAsync(device.Ip)).ContinueWith(t =>
                {
                    if (dialog.IsDisposed) return;
                    dialog.Invoke(() => MessageBox.Show(dialog, t.Result, $"Ping - {device.Ip}", MessageBoxButtons.OK, MessageBoxIcon.Information));
                });
            }
        };

        var cts = new CancellationTokenSource();
        dialog.FormClosed += (_, _) => cts.Cancel();

        var progress = new Progress<(int Done, int Total)>(p =>
            status.Text = $"Scanning… {p.Done}/{p.Total} host(s) checked, {results.Count} candidate(s) found.");

        async Task RunScanAsync()
        {
            results.Clear();
            grid.Rows.Clear();
            status.Text = "Scanning local subnet(s)…";
            try
            {
                var found = await PrinterActions.ScanNetworkForPrintersAsync(excludeIps, progress, cts.Token);
                if (cts.IsCancellationRequested || dialog.IsDisposed) return;

                results.AddRange(found);
                grid.RowCount = found.Count;

                status.Text = found.Count > 0
                    ? $"Scan complete · {found.Count} candidate(s) found."
                    : "Scan complete. No un-added network printers found.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested && !dialog.IsDisposed)
                    status.Text = "Scan failed: " + ex.Message;
            }
        }

        rescanButton.Click += async (_, _) => await RunScanAsync();
        dialog.Shown += async (_, _) => await RunScanAsync();

        dialog.ShowDialog(this);
    }

    // ---------- Per-printer action dialogs ----------

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

    private void OpenVncViewerDialog(PrinterInfo printer)
    {
        var confirm = MessageBox.Show(this,
            $"Open a VNC remote-screen connection to {printer.Address}?\n\n" +
            "Only supported on printer models with a built-in VNC/remote-panel server " +
            "(some Xerox, Konica Minolta and Kyocera controllers). If the printer doesn't " +
            "support it, the connection will simply time out or be refused.",
            "Remote screen (VNC)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _ = ConnectVncAsync(printer);
    }

    private async Task ConnectVncAsync(PrinterInfo printer)
    {
        try
        {
            if (!PrinterActions.VncViewerCached)
                _statusLabel.Text = "Downloading VNC viewer...";

            await PrinterActions.EnsureVncViewerDownloadedAsync();
            PrinterActions.LaunchVncViewer(printer.Address);
            _statusLabel.Text = "Ready.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Ready.";
            MessageBox.Show(this, $"Couldn't get VNC viewer: {ex.Message}", "Remote screen (VNC)",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        try { current = PrinterActions.GetPrinterDetails(printer.Name); }
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
        try { current = PrinterActions.GetPrinterDetails(printer.Name); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printer Detector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var dialog = NewDialog($"Share - {printer.Name}", 380, 160, this);

        var chk = new CheckBox { Text = "Share this printer", Checked = current.Shared, Location = new Point(20, 16), AutoSize = true };
        var lbl = new Label { Text = "Share name:", Location = new Point(20, 48), AutoSize = true, ForeColor = Theme.TextMuted, BackColor = dialog.BackColor };
        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(current.ShareName) ? printer.Name : current.ShareName,
            Location = new Point(20, 68), Width = 340, BorderStyle = BorderStyle.FixedSingle
        };
        dialog.Controls.AddRange(new Control[] { chk, lbl, box });
        var (ok, cancel) = AddOkCancelButtons(dialog, 106);
        dialog.AcceptButton = null;
        cancel.Click += (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };
        ok.Click += (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };

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

    private void ShowTextDialog(string title, string text, int width = 560, int height = 420)
    {
        var dialog = NewDialog(title, width, height, this);

        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, Text = text
        };
        dialog.Controls.Add(box);
        dialog.ShowDialog(this);
    }

    private string? PromptInput(string title, string label, string initial)
    {
        var dialog = NewDialog(title, 400, 150, this);

        var lbl = new Label { Text = label, Location = new Point(20, 16), AutoSize = true, ForeColor = Theme.TextMuted, BackColor = dialog.BackColor };
        var box = new TextBox { Text = initial, Location = new Point(20, 38), Width = 360, BorderStyle = BorderStyle.FixedSingle };
        dialog.Controls.Add(lbl);
        dialog.Controls.Add(box);
        var (ok, cancel) = AddOkCancelButtons(dialog, 80);
        ok.Click += (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };
        cancel.Click += (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };

        return dialog.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    private string[]? PromptMultiInput(string title, params (string Label, string Initial)[] fields)
    {
        var dialog = NewDialog(title, 420, 100 + fields.Length * 50, this);

        var boxes = new TextBox[fields.Length];
        int y = 16;
        for (int i = 0; i < fields.Length; i++)
        {
            var lbl = new Label { Text = fields[i].Label, Location = new Point(20, y), AutoSize = true, ForeColor = Theme.TextMuted, BackColor = dialog.BackColor };
            var box = new TextBox { Text = fields[i].Initial, Location = new Point(20, y + 18), Width = 380, BorderStyle = BorderStyle.FixedSingle };
            boxes[i] = box;
            dialog.Controls.Add(lbl);
            dialog.Controls.Add(box);
            y += 50;
        }

        var (ok, cancel) = AddOkCancelButtons(dialog, y);
        ok.Click += (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };
        cancel.Click += (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };

        return dialog.ShowDialog(this) == DialogResult.OK ? boxes.Select(b => b.Text).ToArray() : null;
    }
}
