namespace PrinterDetector;

public partial class Form1
{
    private enum BundleMode { Save, Install, Remove }

    // Drivers behind virtual queues; there is nothing worth copying from these.
    private static readonly string[] VirtualDrivers =
    {
        "Microsoft Print To PDF", "Microsoft XPS Document Writer v4", "Microsoft Shared Fax Driver",
        "Send to Microsoft OneNote 16 Driver", "Remote Desktop Easy Print"
    };

    /// <summary>
    /// One dialog for the three bundle jobs: save queues from this PC into a named bundle, install queues from a bundle,
    /// or remove several queues at once. Inspect shows what is saved and what an install would change.
    /// </summary>
    private void ShowBundleDialog(BundleMode mode, string? preselect = null)
    {
        const int W = 720, H = 660;
        var titles = new Dictionary<BundleMode, (string Title, string Sub, string Action)>
        {
            [BundleMode.Save] = ("Save printers to a bundle",
                "Saves the driver, port and tray / paper / weight settings so they can be installed on another PC.", "Save to bundle"),
            [BundleMode.Install] = ("Install printers from a bundle",
                "Adds the ticked queues to this PC with their saved settings. Administrator rights are needed.", "Install selected"),
            [BundleMode.Remove] = ("Remove several printers",
                "Deletes the ticked print queues from this PC. Drivers and ports stay installed.", "Remove selected")
        };
        var (title, sub, actionText) = titles[mode];

        var dialog = NewDialog(title, W, H, this);
        dialog.Controls.Add(new Label
        {
            Text = title, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = Theme.TextDark,
            BackColor = Color.White, Location = new Point(20, 16), AutoSize = true
        });
        dialog.Controls.Add(new Label
        {
            Text = sub, Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextMuted,
            BackColor = Color.White, Location = new Point(20, 40), Size = new Size(W - 40, 18)
        });

        int y = 68;
        ComboBox? bundleBox = null;
        Label? bundleInfo = null;
        if (mode != BundleMode.Remove)
        {
            dialog.Controls.Add(new Label
            {
                Text = mode == BundleMode.Save ? "BUNDLE NAME (FOR EXAMPLE A CUSTOMER)" : "BUNDLE TO INSTALL",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = Theme.TextFaint,
                BackColor = Color.White, Location = new Point(20, y), AutoSize = true
            });
            bundleBox = new ComboBox
            {
                Location = new Point(20, y + 18), Width = 320, Font = new Font("Segoe UI", 9.5f),
                DropDownStyle = mode == BundleMode.Save ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList
            };
            dialog.Controls.Add(bundleBox);
            bundleInfo = new Label
            {
                Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.TextMuted, BackColor = Color.White,
                Location = new Point(356, y + 20), Size = new Size(W - 376, 20), AutoEllipsis = true
            };
            dialog.Controls.Add(bundleInfo);
            y += 54;
        }

        var list = new CheckedListBox
        {
            Location = new Point(20, y), Size = new Size(W - 40, 250), CheckOnClick = true, IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f)
        };
        dialog.Controls.Add(list);
        var queueNames = new List<string>();
        y += 256;

        var selectAll = new FlatButton { Text = "Select all", Style = ButtonStyle.Outline, Size = new Size(90, 30), Location = new Point(20, y) };
        var selectNone = new FlatButton { Text = "Select none", Style = ButtonStyle.Outline, Size = new Size(100, 30), Location = new Point(118, y) };
        dialog.Controls.Add(selectAll);
        dialog.Controls.Add(selectNone);
        selectAll.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
        selectNone.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };

        var replaceBox = new CheckBox
        {
            Text = "Replace queues that already exist", AutoSize = true, Location = new Point(240, y + 5),
            Visible = mode == BundleMode.Install, BackColor = Color.White
        };
        dialog.Controls.Add(replaceBox);
        y += 42;

        var inspectButton = new FlatButton
        {
            Text = "Inspect selected", Style = ButtonStyle.Outline, Size = new Size(140, 34),
            Location = new Point(20, y), Visible = mode != BundleMode.Remove
        };
        var actionButton = new FlatButton
        {
            Text = actionText, Style = mode == BundleMode.Remove ? ButtonStyle.Danger : ButtonStyle.Primary,
            Size = new Size(160, 34), Location = new Point(W - 20 - 90 - 12 - 160, y)
        };
        var closeButton = new FlatButton { Text = "Close", Style = ButtonStyle.Outline, Size = new Size(90, 34), Location = new Point(W - 20 - 90, y) };
        dialog.Controls.Add(inspectButton);
        dialog.Controls.Add(actionButton);
        dialog.Controls.Add(closeButton);
        closeButton.Click += (_, _) => dialog.Close();
        y += 46;

        var log = new TextBox
        {
            Location = new Point(20, y), Size = new Size(W - 40, H - y - 16), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.PanelBg
        };
        dialog.Controls.Add(log);
        void Log(string line) => log.AppendText(line + Environment.NewLine);
        var progress = new Progress<string>(Log);

        bool changed = false;
        void SetBusy(bool busy)
        {
            actionButton.Enabled = inspectButton.Enabled = selectAll.Enabled = selectNone.Enabled = !busy;
            dialog.UseWaitCursor = busy;
        }

        // ----- populate the list -----

        void FillFromThisPc()
        {
            list.Items.Clear();
            queueNames.Clear();
            foreach (var p in _allPrinters.Where(p => !VirtualDrivers.Contains(p.Driver, StringComparer.OrdinalIgnoreCase)
                                                      && !p.Name.StartsWith(@"\\")))
            {
                queueNames.Add(p.Name);
                int idx = list.Items.Add($"{p.Name}    [{p.Driver} | {p.Address}]");
                if (preselect != null && string.Equals(p.Name, preselect, StringComparison.OrdinalIgnoreCase))
                    list.SetItemChecked(idx, true);
            }
        }

        void FillFromBundle()
        {
            list.Items.Clear();
            queueNames.Clear();
            var name = bundleBox?.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                bundleInfo!.Text = $"No bundles found in {QueueBundles.BundleRoot}";
                return;
            }
            try
            {
                var (header, queues) = QueueBundles.ReadBundle(name);
                bundleInfo!.Text = header;
                foreach (var q in queues)
                {
                    queueNames.Add(q.Name);
                    int idx = list.Items.Add($"{q.Name}    [{q.Driver} | {q.Where}]");
                    list.SetItemChecked(idx, true);
                }
            }
            catch (Exception ex)
            {
                bundleInfo!.Text = "Could not read bundle: " + ex.Message;
            }
        }

        void FillBundleNames(string? select)
        {
            if (bundleBox == null) return;
            var typed = bundleBox.Text;
            var names = QueueBundles.FindBundles();
            bundleBox.Items.Clear();
            foreach (var n in names) bundleBox.Items.Add(n);
            if (mode == BundleMode.Save)
            {
                bundleBox.Text = select ?? typed;
                bundleInfo!.Text = $"Saved under {QueueBundles.BundleRoot}";
            }
            else
            {
                if (select != null && names.Contains(select)) bundleBox.SelectedItem = select;
                else if (bundleBox.Items.Count > 0 && bundleBox.SelectedIndex < 0) bundleBox.SelectedIndex = 0;
                FillFromBundle();
            }
        }

        if (mode == BundleMode.Install) bundleBox!.SelectedIndexChanged += (_, _) => FillFromBundle();
        if (mode == BundleMode.Install) FillBundleNames(null);
        else
        {
            FillFromThisPc();
            FillBundleNames(null);
        }

        List<string> Picked() => list.CheckedIndices.Cast<int>().Select(i => queueNames[i]).ToList();

        // ----- actions -----

        inspectButton.Click += async (_, _) =>
        {
            var picked = Picked();
            if (picked.Count == 0) { MessageBox.Show(dialog, "Tick at least one printer.", title); return; }
            SetBusy(true);
            try
            {
                string report;
                if (mode == BundleMode.Save)
                {
                    Log("Reading settings of " + picked.Count + " queue(s)...");
                    report = await QueueBundles.InspectLocalAsync(picked);
                }
                else
                {
                    var bundle = bundleBox!.SelectedItem as string;
                    if (string.IsNullOrEmpty(bundle)) { MessageBox.Show(dialog, "Choose a bundle first.", title); return; }
                    report = await QueueBundles.InspectBundleAsync(bundle, picked, replaceBox.Checked);
                }
                ShowTextDialog(mode == BundleMode.Save ? "Settings that would be saved" : "Saved settings and what will be applied", report, 860, 640);
            }
            catch (Exception ex) { Log("FAILED: " + ex.Message); }
            finally { SetBusy(false); }
        };

        actionButton.Click += async (_, _) =>
        {
            var picked = Picked();
            if (picked.Count == 0) { MessageBox.Show(dialog, "Tick at least one printer.", title); return; }

            string? bundle = null;
            if (mode == BundleMode.Save)
            {
                bundle = QueueBundles.SafeName(bundleBox!.Text);
                if (bundle.Length == 0) { MessageBox.Show(dialog, "Type a bundle name first, for example the customer name.", title); return; }
            }
            else if (mode == BundleMode.Install)
            {
                bundle = bundleBox!.SelectedItem as string;
                if (string.IsNullOrEmpty(bundle)) { MessageBox.Show(dialog, "Choose a bundle first.", title); return; }
            }
            else
            {
                var confirm = MessageBox.Show(dialog,
                    $"Delete {picked.Count} print queue(s) from this PC? This cannot be undone.\n\n{string.Join("\n", picked)}",
                    "Remove printers", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            if (mode != BundleMode.Save && !Program.IsElevated())
                Log("NOTE: not running as administrator. Restart Printer Detector as administrator if this fails.");

            SetBusy(true);
            try
            {
                switch (mode)
                {
                    case BundleMode.Save:
                        Log($"---- Save to bundle '{bundle}' ----");
                        await QueueBundles.SaveAsync(bundle!, picked, progress);
                        FillBundleNames(bundle);
                        break;
                    case BundleMode.Install:
                        Log($"---- Install from bundle '{bundle}' ----");
                        await QueueBundles.InstallAsync(bundle!, picked, replaceBox.Checked, progress);
                        changed = true;
                        break;
                    case BundleMode.Remove:
                        Log("---- Remove ----");
                        await QueueBundles.RemoveAsync(picked, progress);
                        changed = true;
                        foreach (var i in list.CheckedIndices.Cast<int>().OrderByDescending(i => i).ToList())
                        {
                            list.Items.RemoveAt(i);
                            queueNames.RemoveAt(i);
                        }
                        break;
                }
            }
            catch (Exception ex) { Log("FAILED: " + ex.Message); }
            finally { SetBusy(false); }
        };

        dialog.ShowDialog(this);
        if (changed) _ = RefreshPrintersAsync();
    }
}
