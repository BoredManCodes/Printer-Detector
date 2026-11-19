using System.Drawing.Drawing2D;

namespace PrinterDetector;

public partial class Form1
{
    private const int LowSupplyThreshold = 15;
    private Panel? _emptyStatePanel;

    private void BuildGrid()
    {
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.GridColor = Theme.BorderLighter;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowTemplate.Height = 44;
        _grid.ColumnHeadersHeight = 38;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.GridHeaderBg;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _grid.DefaultCellStyle.SelectionBackColor = Color.White;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.TextDark;
        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 9f);
        _grid.DefaultCellStyle.ForeColor = Theme.TextDark;
        _grid.RowTemplate.DefaultCellStyle.Padding = new Padding(12, 0, 12, 0);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "NAME", DataPropertyName = "Name", FillWeight = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Driver", HeaderText = "DRIVER", DataPropertyName = "Driver", FillWeight = 200 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "STATUS", DataPropertyName = "Status", FillWeight = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Connection", HeaderText = "CONNECTION", DataPropertyName = "ConnectionType", FillWeight = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "IP / PORT", DataPropertyName = "Address", FillWeight = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Flags", HeaderText = "FLAGS", DataPropertyName = "WarningFlags", FillWeight = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Consumables", HeaderText = "CONSUMABLES", DataPropertyName = "Consumables", FillWeight = 140 });

        _grid.Columns["Address"]!.DefaultCellStyle.Font = Theme.FontMono;
        _consumablesColumnIndex = _grid.Columns["Consumables"]!.Index;
        _flagsColumnIndex = _grid.Columns["Flags"]!.Index;
        _statusColumnIndex = _grid.Columns["Status"]!.Index;

        _grid.CellClick += Grid_CellClick;
        _grid.CellPainting += Grid_CellPainting;
        _grid.CellMouseEnter += (_, e) => { if (e.RowIndex != _hoverRow && e.RowIndex >= 0 && e.RowIndex < _grid.RowCount) { _hoverRow = e.RowIndex; _grid.InvalidateRow(e.RowIndex); } };
        _grid.CellMouseLeave += (_, e) => { if (e.RowIndex == _hoverRow) { _hoverRow = -1; if (e.RowIndex >= 0 && e.RowIndex < _grid.RowCount) _grid.InvalidateRow(e.RowIndex); } };
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _printers.Count) return;
        if (_selectedIndex == e.RowIndex) return;
        _selectedIndex = e.RowIndex;
        _grid.Invalidate();
        RenderDetails(_printers[_selectedIndex]);
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _printers.Count || e.Graphics == null) return;

        var printer = _printers[e.RowIndex];
        bool selected = e.RowIndex == _selectedIndex;
        bool hover = e.RowIndex == _hoverRow && !selected;
        var bg = selected ? Theme.SelectedRowBg : hover ? Theme.RowHoverBg : Color.White;

        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.CellBounds);

        if (e.ColumnIndex == 0)
        {
            using var edgeBrush = new SolidBrush(selected ? Theme.Accent : Color.Transparent);
            e.Graphics.FillRectangle(edgeBrush, e.CellBounds.Left, e.CellBounds.Top, 4, e.CellBounds.Height);
        }

        using (var borderPen = new Pen(Theme.BorderLighter))
            e.Graphics.DrawLine(borderPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);

        if (e.ColumnIndex == _statusColumnIndex)
        {
            var (fg, _) = Theme.StatusColors(printer.Status);
            var dotRect = new Rectangle(e.CellBounds.Left + 12, e.CellBounds.Top + e.CellBounds.Height / 2 - 4, 8, 8);
            using (var dotBrush = new SolidBrush(fg)) e.Graphics.FillEllipse(dotBrush, dotRect);
            var textRect = new Rectangle(dotRect.Right + 8, e.CellBounds.Top, e.CellBounds.Width - (dotRect.Right + 8 - e.CellBounds.Left) - 8, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, printer.Status, e.CellStyle!.Font, textRect, Theme.TextDark,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.Handled = true;
            return;
        }

        if (e.ColumnIndex == _flagsColumnIndex)
        {
            PaintFlagChips(e.Graphics, e.CellBounds, printer);
            e.Handled = true;
            return;
        }

        if (e.ColumnIndex == _consumablesColumnIndex)
        {
            PaintConsumables(e, printer);
            e.Handled = true;
            return;
        }

        var rect = e.CellBounds;
        rect.Inflate(-12, 0);
        var font = e.ColumnIndex == 4 ? Theme.FontMono : e.CellStyle!.Font;
        var fore = e.ColumnIndex == 0 ? Theme.TextDark : Theme.TextMuted;
        var bold = e.ColumnIndex == 0 ? FontStyle.Bold : FontStyle.Regular;
        using var useFont = new Font(font!, bold);
        TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value), useFont, rect, fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        e.Handled = true;
    }

    internal static void PaintFlagChips(Graphics g, Rectangle cellBounds, PrinterInfo printer)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = cellBounds.Left + 8;
        int cy = cellBounds.Top + cellBounds.Height / 2;

        foreach (var (label, bg, fg) in FlagChipsFor(printer))
        {
            var size = TextRenderer.MeasureText(g, label, Theme.FontBase, Size.Empty, TextFormatFlags.NoPadding);
            var chipRect = new Rectangle(x, cy - 9, size.Width + 16, 18);
            if (chipRect.Right > cellBounds.Right - 4) break;
            Gfx.FillRoundedRect(g, chipRect, 9, bg);
            TextRenderer.DrawText(g, label, new Font("Segoe UI", 7.5f, FontStyle.Bold), chipRect, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = chipRect.Right + 6;
        }
    }

    internal static List<(string Label, Color Bg, Color Fg)> FlagChipsFor(PrinterInfo printer)
    {
        var list = new List<(string, Color, Color)>();
        if (printer.IsWsd) list.Add(("WSD", Theme.FlagWsdBg, Theme.FlagWsdFg));
        if (printer.IsRedirected) list.Add(("Redirected (Easy Print)", Theme.FlagRedirBg, Theme.FlagRedirFg));
        if (printer.IsReadyPrint) list.Add(("Ready Print", Theme.FlagReadyBg, Theme.FlagReadyFg));
        return list;
    }

    internal static string FlagTip(string label) => label switch
    {
        "WSD" => "WSD port — discovered via Web Services for Devices. Status and consumable queries over WSD are slower and less reliable than a standard TCP/IP port.",
        "Redirected (Easy Print)" => "Redirected via Remote Desktop Easy Print — this printer belongs to the client PC of the current remote session, not this machine. Queue/properties/pause actions may not behave as expected.",
        "Ready Print" => "Installed via Windows Ready Print (Microsoft IPP Class Driver) — a driverless IPP install rather than a vendor driver. Some vendor-specific features and SNMP consumable data may not be available.",
        _ => ""
    };

    private void PaintConsumables(DataGridViewCellPaintingEventArgs e, PrinterInfo printer)
    {
        var g = e.Graphics!;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = e.CellBounds;
        rect.Inflate(-8, -8);

        bool loading = printer.ConnectionType == "Network" && printer.Supplies == null && printer.Consumables == "Checking...";
        if (loading)
        {
            ShimmerStrip.Subscribe(_grid);
            var shimmerRect = new Rectangle(rect.Left, rect.Top + rect.Height / 2 - 6, 56, 12);
            ShimmerStrip.Paint(g, shimmerRect);
            TextRenderer.DrawText(g, "Checking…", new Font("Segoe UI", 7.5f), new Rectangle(shimmerRect.Right + 8, rect.Top, rect.Width, rect.Height),
                Theme.TextFaint, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            return;
        }

        if (printer.Supplies is not { Count: > 0 } supplies)
        {
            TextRenderer.DrawText(g, printer.Consumables, new Font("Segoe UI", 8f), rect, Theme.TextFaint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            return;
        }

        int gap = 3;
        int barWidth = Math.Max(10, (rect.Width - (supplies.Count - 1) * gap) / supplies.Count);
        int x = rect.Left;

        foreach (var s in supplies)
        {
            var barRect = new Rectangle(x, rect.Top, barWidth, rect.Height);
            bool low = s.Percent is int lp && lp < LowSupplyThreshold;
            Gfx.FillRoundedRect(g, barRect, 3, ColorTranslator.FromHtml("#EDF0F4"));

            if (s.Percent is int pct)
            {
                int fillHeight = (int)Math.Round(barRect.Height * Math.Clamp(pct, 0, 100) / 100.0);
                var fillRect = new Rectangle(barRect.Left, barRect.Bottom - fillHeight, barRect.Width, fillHeight);
                using var clip = Gfx.RoundedRect(barRect, 3);
                var oldClip = g.Clip;
                g.SetClip(clip, CombineMode.Replace);
                using var brush = new SolidBrush(s.BarColor);
                g.FillRectangle(brush, fillRect);
                g.Clip = oldClip;
            }

            Gfx.DrawRoundedRect(g, barRect, 3, low ? Theme.StatusErrorFg : Color.FromArgb(60, Color.Gray), low ? 1.5f : 1f);
            x += barWidth + gap;
        }
    }

    private void UpdateEmptyState()
    {
        bool empty = _allPrinters.Count == 0;
        _grid.Visible = !empty;

        if (empty && _emptyStatePanel == null)
        {
            _emptyStatePanel = BuildEmptyStatePanel();
            _grid.Parent!.Controls.Add(_emptyStatePanel);
            _emptyStatePanel.BringToFront();
        }

        if (_emptyStatePanel != null) _emptyStatePanel.Visible = empty;
    }

    private Panel BuildEmptyStatePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        var inner = new Panel { Size = new Size(340, 220), Anchor = AnchorStyles.None, BackColor = Color.White };
        panel.Controls.Add(inner);
        panel.Resize += (_, _) => inner.Location = new Point((panel.Width - inner.Width) / 2, (panel.Height - inner.Height) / 2);

        var iconPanel = new Panel { Size = new Size(48, 48), Location = new Point((inner.Width - 48) / 2, 0), BackColor = Color.Transparent };
        iconPanel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Gfx.FillRoundedRect(e.Graphics, new Rectangle(0, 0, 47, 47), 12, Theme.ChipIpBg);
            TextRenderer.DrawText(e.Graphics, "∅", new Font("Segoe UI", 16f), iconPanel.ClientRectangle, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        inner.Controls.Add(iconPanel);

        var title = new Label
        {
            Text = "No printers configured on this PC",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Theme.TextDark,
            BackColor = inner.BackColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 60),
            Size = new Size(340, 24)
        };
        inner.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Windows reports no installed printers. Scan the network to find devices that haven't been added yet.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextMuted,
            BackColor = inner.BackColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 92),
            Size = new Size(340, 48)
        };
        inner.Controls.Add(subtitle);

        var scanBtn = new FlatButton { Text = "Scan Network", Style = ButtonStyle.Primary, Size = new Size(140, 34), Location = new Point(85, 156) };
        scanBtn.Click += (_, _) => ShowNetworkScanDialog();
        inner.Controls.Add(scanBtn);

        var addBtn = new FlatButton { Text = "Add a printer…", Style = ButtonStyle.Outline, Size = new Size(140, 34), Location = new Point(233, 156) };
        addBtn.Click += (_, _) => TryRun(PrinterActions.OpenAddPrinterWizard);
        inner.Controls.Add(addBtn);

        return panel;
    }
}
