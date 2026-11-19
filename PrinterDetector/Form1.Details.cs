using System.Drawing.Drawing2D;
using System.Net;

namespace PrinterDetector;

public partial class Form1
{
    private const int DetailsPad = 20;

    // _detailsContent.ClientSize.Width shrinks by the vertical scrollbar width once AutoScroll
    // kicks in, but that only happens after the (taller) content we're about to build gets added.
    // Reserve the scrollbar width up front so freshly-built rows aren't sized against the wider,
    // pre-scrollbar client area and then clipped once it appears.
    private int DetailsWidth => _detailsContent.ClientSize.Width - DetailsPad * 2 - SystemInformation.VerticalScrollBarWidth;

    private void RenderEmptyDetails()
    {
        _detailsContent.SuspendLayout();
        _detailsContent.Controls.Clear();

        var wrap = new Panel { Size = new Size(340, 160), Location = new Point(30, 190), BackColor = Theme.PanelBg };
        _detailsContent.Controls.Add(wrap);

        var dashed = new Panel { Size = new Size(44, 44), Location = new Point((340 - 44) / 2, 0) };
        dashed.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(ColorTranslator.FromHtml("#B9C3D1"), 1.5f) { DashStyle = DashStyle.Dash };
            using var path = Gfx.RoundedRect(new Rectangle(0, 0, 43, 43), 10);
            e.Graphics.DrawPath(pen, path);
        };
        wrap.Controls.Add(dashed);

        var title = new Label
        {
            Text = "No printer selected",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Theme.TextDark,
            BackColor = wrap.BackColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 56),
            Size = new Size(340, 30)
        };
        wrap.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Select a printer in the list to see its details and run actions.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.TextMuted,
            BackColor = wrap.BackColor,
            TextAlign = ContentAlignment.TopCenter,
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            Location = new Point(0, 90)
        };
        wrap.Controls.Add(subtitle);

        _detailsContent.ResumeLayout();
    }

    private void RenderDetails(PrinterInfo? printer)
    {
        if (printer == null) { RenderEmptyDetails(); return; }

        _detailsContent.SuspendLayout();
        _detailsContent.Controls.Clear();
        int y = 0;

        y = BuildHeaderCard(printer, y);
        y = BuildConsumablesCard(printer, y);
        y = BuildGroups(printer, y);

        _detailsContent.Controls.Add(new Panel { Location = new Point(0, y), Size = new Size(1, 14) });
        _detailsContent.ResumeLayout();
        _detailsContent.AutoScrollPosition = new Point(0, 0);
        BeginInvoke(new Action(() => _detailsContent.AutoScrollPosition = new Point(0, 0)));
    }

    private int BuildHeaderCard(PrinterInfo printer, int y)
    {
        var card = new Panel { Location = new Point(0, y), Size = new Size(DetailsWidth + DetailsPad * 2, 1), BackColor = Color.White };
        _detailsContent.Controls.Add(card);
        int cy = 16;

        var nameFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        int nameHeight = LineHeight(nameFont);
        var name = new MarqueeLabel
        {
            Text = printer.Name, Font = nameFont, ForeColor = Theme.TextDark,
            Backdrop = card.BackColor, BackColor = card.BackColor, Location = new Point(DetailsPad, cy), Size = new Size(DetailsWidth, nameHeight)
        };
        card.Controls.Add(name);
        cy += nameHeight + 4;

        var (fg, bg) = Theme.StatusColors(printer.Status);
        var badge = new Pill { Text = "  " + printer.Status, BgColor = bg, FgColor = fg };
        card.Controls.Add(badge);
        badge.Location = new Point(DetailsPad, cy);
        var dot = new Panel { Size = new Size(7, 7), BackColor = Color.Transparent, Location = new Point(badge.Left + 10, badge.Top + badge.Height / 2 - 3) };
        dot.Paint += (_, e) => { using var b = new SolidBrush(fg); e.Graphics.FillEllipse(b, 0, 0, 6, 6); };
        card.Controls.Add(dot);
        dot.BringToFront();

        cy += badge.Height + 10;
        var driverFont = new Font("Segoe UI", 8.75f);
        int driverHeight = LineHeight(driverFont);
        var driver = new Label
        {
            Text = printer.Driver, Font = driverFont, ForeColor = Theme.TextMuted,
            BackColor = card.BackColor, Location = new Point(DetailsPad, cy), Size = new Size(DetailsWidth, driverHeight), AutoEllipsis = true
        };
        card.Controls.Add(driver);

        cy += driverHeight + 6;
        AddFieldPair(card, "CONNECTION", printer.ConnectionType, DetailsPad, cy, false);
        AddFieldPair(card, "IP / PORT", printer.Address, DetailsPad + DetailsWidth / 2 + 8, cy, true);

        cy += 44;
        var chips = FlagChipsFor(printer);
        if (chips.Count > 0)
        {
            int cx = DetailsPad;
            foreach (var (label, cbg, cfg) in chips)
            {
                var pill = new Pill { Text = label, BgColor = cbg, FgColor = cfg, Font = new Font("Segoe UI", 8f, FontStyle.Bold) };
                pill.Location = new Point(cx, cy);
                _tip.SetToolTip(pill, FlagTip(label));
                card.Controls.Add(pill);
                cx += pill.Width + 6;
                if (cx > card.Width - DetailsPad - 60) { cx = DetailsPad; cy += pill.Height + 6; }
            }
            cy += 24;

            foreach (var (label, cbg, _) in chips)
            {
                var tipBox = new Label
                {
                    Text = FlagTip(label),
                    Font = new Font("Segoe UI", 8.25f),
                    ForeColor = Theme.TextMuted,
                    BackColor = cbg,
                    Location = new Point(DetailsPad, cy),
                    Size = new Size(DetailsWidth, 0),
                    Padding = new Padding(10, 8, 10, 8),
                    AutoSize = false
                };
                int h = MeasureLabelHeight(tipBox, DetailsWidth - 20) + 16;
                tipBox.Height = h;
                card.Controls.Add(tipBox);
                cy += h + 8;
            }
        }
        else
        {
            cy += 4;
        }

        cy += 16;
        card.Height = cy;
        card.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Theme.BorderLight), 0, card.Height - 1, card.Width, card.Height - 1);
        return y + card.Height;
    }

    private int MeasureLabelHeight(Label label, int width)
    {
        using var g = CreateGraphics();
        var size = g.MeasureString(label.Text, label.Font, width - label.Padding.Left - label.Padding.Right);
        return (int)Math.Ceiling(size.Height);
    }

    // A fixed pixel height baked in for a given font clips descenders once the font renders
    // taller than expected (bold weight, a different DPI scale, etc). Measure the actual glyph
    // height instead so labels always have room for what they're about to draw.
    private static int LineHeight(Font font, int pad = 6) =>
        TextRenderer.MeasureText("Ágy1", font, Size.Empty, TextFormatFlags.NoPadding).Height + pad;

    private void AddFieldPair(Control parent, string caption, string value, int x, int y, bool mono)
    {
        var cap = new Label
        {
            Text = caption, Font = new Font("Segoe UI", 7.25f, FontStyle.Bold), ForeColor = Theme.TextFaint,
            BackColor = parent.BackColor, Location = new Point(x, y), AutoSize = true
        };
        parent.Controls.Add(cap);
        var val = new Label
        {
            Text = value, Font = mono ? Theme.FontMono : new Font("Segoe UI", 9f), ForeColor = Theme.TextDark,
            BackColor = parent.BackColor, Location = new Point(x, y + 16), AutoSize = true
        };
        parent.Controls.Add(val);
    }

    private int BuildConsumablesCard(PrinterInfo printer, int y)
    {
        var card = new Panel { Location = new Point(0, y), Size = new Size(DetailsWidth + DetailsPad * 2, 1), BackColor = Color.White };
        _detailsContent.Controls.Add(card);
        int cy = 14;

        var header = new Label
        {
            Text = "CONSUMABLES", Font = new Font("Segoe UI", 7.25f, FontStyle.Bold), ForeColor = Theme.TextFaint,
            BackColor = card.BackColor, Location = new Point(DetailsPad, cy), AutoSize = true
        };
        card.Controls.Add(header);
        cy += 20;

        if (printer.Supplies is { Count: > 0 } supplies)
        {
            foreach (var s in supplies)
            {
                var sq = new Panel { Size = new Size(10, 10), Location = new Point(DetailsPad, cy + 1), BackColor = s.BarColor };
                card.Controls.Add(sq);

                var desc = new Label
                {
                    Text = s.Description, Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextDark,
                    BackColor = card.BackColor, Location = new Point(DetailsPad + 20, cy), Size = new Size(DetailsWidth - 20 - 50, 16), AutoEllipsis = true
                };
                card.Controls.Add(desc);

                bool low = s.Percent is int lp && lp < LowSupplyThreshold;
                var pctLabel = new Label
                {
                    Text = s.Percent is int p ? $"{p}%" : "n/a",
                    Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                    ForeColor = low ? Theme.StatusErrorFg : Theme.TextDark,
                    BackColor = card.BackColor,
                    TextAlign = ContentAlignment.MiddleRight,
                    Location = new Point(DetailsPad + DetailsWidth - 40, cy),
                    Size = new Size(40, 16)
                };
                card.Controls.Add(pctLabel);

                cy += 18;
                var barBack = new Panel { Location = new Point(DetailsPad + 20, cy), Size = new Size(DetailsWidth - 20, 8), BackColor = Color.Transparent };
                barBack.Paint += (_, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Gfx.FillRoundedRect(e.Graphics, new Rectangle(0, 0, barBack.Width, barBack.Height), 4, ColorTranslator.FromHtml("#E6EAF0"));
                    if (s.Percent is int pct)
                    {
                        int fw = (int)Math.Round(barBack.Width * Math.Clamp(pct, 0, 100) / 100.0);
                        if (fw > 0)
                        {
                            using var clip = Gfx.RoundedRect(new Rectangle(0, 0, barBack.Width, barBack.Height), 4);
                            e.Graphics.SetClip(clip, CombineMode.Replace);
                            using var b = new SolidBrush(s.BarColor);
                            e.Graphics.FillRectangle(b, 0, 0, fw, barBack.Height);
                        }
                    }
                    if (low) Gfx.DrawRoundedRect(e.Graphics, new Rectangle(0, 0, barBack.Width - 1, barBack.Height - 1), 4, Theme.StatusErrorFg, 1.5f);
                };
                card.Controls.Add(barBack);
                cy += 18;
            }
        }
        else
        {
            var summary = new Label
            {
                Text = printer.Consumables, Font = new Font("Segoe UI", 9f), ForeColor = Theme.TextFaint,
                BackColor = card.BackColor, Location = new Point(DetailsPad, cy), AutoSize = true
            };
            card.Controls.Add(summary);
            cy += 20;
        }

        cy += 10;
        card.Height = cy;
        card.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Theme.BorderLight), 0, card.Height - 1, card.Width, card.Height - 1);
        return y + card.Height;
    }

    private int BuildGroups(PrinterInfo printer, int y)
    {
        bool isNetwork = printer.ConnectionType == "Network";
        bool hasIp = IPAddress.TryParse(printer.Address, out _);

        if (isNetwork)
        {
            y = BuildGroup(y, "NETWORK", new[]
            {
                ("Open web GUI (http)", ButtonStyle.Outline, (Action)(() => TryRun(() => PrinterActions.OpenWebGui(printer.Address)))),
                ("Open web GUI (https)", ButtonStyle.Outline, () => TryRun(() => PrinterActions.OpenWebGuiHttps(printer.Address))),
                ("View SNMP details", ButtonStyle.Outline, () => { if (hasIp) _ = ShowSnmpDetailAsync(printer); }),
                ("Ping", ButtonStyle.Outline, () => { if (hasIp) _ = ShowPingAsync(printer); }),
                ("Continuous ping", ButtonStyle.Outline, () => TryRun(() => PrinterActions.OpenContinuousPing(printer.Address))),
                ("Traceroute", ButtonStyle.Outline, () => TryRun(() => PrinterActions.OpenTraceroute(printer.Address))),
                ("Test port 9100", ButtonStyle.Outline, () => { if (hasIp) _ = ShowPort9100Async(printer); }),
                ("MAC address (ARP)", ButtonStyle.Outline, () => { if (hasIp) _ = ShowArpAsync(printer); }),
                ("Wake-on-LAN", ButtonStyle.Outline, () => { if (hasIp) _ = WakeOnLanAsync(printer); }),
                ("Copy IP", ButtonStyle.Outline, () => TryRun(() => PrinterActions.CopyAddressToClipboard(printer.Address))),
                ("Remote screen (VNC)…", ButtonStyle.Outline, () => OpenVncViewerDialog(printer)),
            });
        }

        y = BuildGroup(y, "PRINTER", new[]
        {
            ("Open print queue", ButtonStyle.AccentOutline, (Action)(() => TryRun(() => PrinterActions.OpenPrintQueue(printer.Name)))),
            ("Print test page", ButtonStyle.Outline, () => TryRunWmi(() => PrinterActions.PrintTestPage(printer.Name), "Test page sent.")),
            ("Properties", ButtonStyle.Outline, () => TryRun(() => PrinterActions.OpenProperties(printer.Name))),
            ("Printing preferences", ButtonStyle.Outline, () => TryRun(() => PrinterActions.OpenPrintingPreferences(printer.Name))),
            ("Search for driver", ButtonStyle.Outline, () => TryRun(() => PrinterActions.SearchDriverOnGoogle(printer.Name))),
            ("Copy name", ButtonStyle.Outline, () => TryRun(() => PrinterActions.CopyText(printer.Name))),
        });

        y = BuildGroup(y, "QUEUE", new[]
        {
            ("Pause", ButtonStyle.Outline, (Action)(() => TryRunWmi(() => PrinterActions.Pause(printer.Name), "Printer paused."))),
            ("Resume", ButtonStyle.Outline, () => TryRunWmi(() => PrinterActions.Resume(printer.Name), "Printer resumed.")),
            ("Cancel all jobs", ButtonStyle.Outline, () => TryRunWmi(() => PrinterActions.CancelAllJobs(printer.Name), "All jobs cancelled.")),
            ("Set as default", ButtonStyle.Outline, () => TryRunWmi(() => PrinterActions.SetAsDefault(printer.Name), "Set as default printer.")),
        });

        y = BuildGroup(y, "MANAGE", new[]
        {
            ("Rename…", ButtonStyle.Outline, (Action)(() => RenamePrinterDialog(printer))),
            ("Comment / location…", ButtonStyle.Outline, () => EditCommentLocationDialog(printer)),
            ("Share / unshare…", ButtonStyle.Outline, () => ShareDialog(printer)),
            ("Delete printer…", ButtonStyle.Danger, () => DeletePrinterConfirm(printer)),
        });

        y = BuildGroup(y, "SPOOLER (ALL PRINTERS)", new[]
        {
            ("Restart print spooler", ButtonStyle.Outline, (Action)(() => _ = RestartSpoolerAsync())),
            ("Clear stuck print queue…", ButtonStyle.Danger, () => _ = ClearStuckQueueAsync()),
        });

        return y;
    }

    private int BuildGroup(int y, string title, (string Label, ButtonStyle Style, Action OnClick)[] items)
    {
        var section = new Panel { Location = new Point(0, y), Size = new Size(DetailsWidth + DetailsPad * 2, 1), BackColor = Theme.PanelBg };
        _detailsContent.Controls.Add(section);
        int cy = 12;

        var header = new Label
        {
            Text = title, Font = new Font("Segoe UI", 7.25f, FontStyle.Bold), ForeColor = Theme.TextFaint,
            BackColor = section.BackColor, Location = new Point(DetailsPad, cy), AutoSize = true
        };
        section.Controls.Add(header);
        cy += 20;

        const int rowHeight = 42;
        for (int i = 0; i < items.Length; i++)
        {
            var (label, style, onClick) = items[i];
            var btn = new FlatButton
            {
                Text = label, Style = style, Radius = 5,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                Size = new Size(DetailsWidth, 36),
                Location = new Point(DetailsPad, cy + i * rowHeight),
                Backdrop = Theme.PanelBg
            };
            btn.Click += (_, _) => onClick();
            _tip.SetToolTip(btn, label);
            section.Controls.Add(btn);
        }

        cy += items.Length * rowHeight - (items.Length > 0 ? rowHeight - 36 : 0);
        section.Height = cy;
        return y + section.Height;
    }
}
