namespace PrinterDetector;

public partial class Form1
{
    /// <summary>One dialog that sets this PC up as a network scanning host. See ScanHost and scan-host.ps1.</summary>
    private void ShowScanHostDialog()
    {
        if (!Program.IsElevated())
        {
            MessageBox.Show(this, "This changes accounts, shares and network settings, so it needs administrator rights.\n\nClose Printer Detector and run it as administrator.",
                "Network scanning host", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        const int W = 620, H = 690;
        var dialog = NewDialog("Configure as network scanning host", W, H, this);

        Label Caption(string text, int y) => new()
        {
            Text = text, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = Theme.TextFaint,
            BackColor = Color.White, Location = new Point(20, y), AutoSize = true
        };

        dialog.Controls.Add(new Label
        {
            Text = "Configure as network scanning host", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.TextDark, BackColor = Color.White, Location = new Point(20, 16), AutoSize = true
        });
        dialog.Controls.Add(new Label
        {
            Text = $"Sets this PC up so a network scanner can save scans into a shared folder. It will:\n" +
                   $"  •  switch public networks to private and turn on network discovery\n" +
                   $"  •  turn on SMB file sharing if it is off\n" +
                   $"  •  create (or reset) a local account '{ScanHost.UserName}' whose password never expires\n" +
                   $"  •  give that account full access to the folder and read / write on a share called '{ScanHost.ShareName}'\n" +
                   $"     (you will be asked for another name if that one is taken)",
            Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextMuted, BackColor = Color.White,
            Location = new Point(20, 44), Size = new Size(W - 40, 100)
        });

        int y = 156;
        dialog.Controls.Add(Caption("SCAN FOLDER (CREATED IF IT DOES NOT EXIST)", y));
        var folderBox = new TextBox
        {
            Location = new Point(20, y + 18), Size = new Size(W - 40 - 100, 26), Text = ScanHost.DefaultFolder,
            Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle
        };
        var browse = new FlatButton { Text = "Browse...", Style = ButtonStyle.Outline, Size = new Size(90, 28), Location = new Point(W - 20 - 90, y + 17) };
        dialog.Controls.Add(folderBox);
        dialog.Controls.Add(browse);
        browse.Click += (_, _) =>
        {
            using var fb = new FolderBrowserDialog
            {
                Description = "Choose the folder scans should be saved into, or make a new one.",
                UseDescriptionForTitle = true, ShowNewFolderButton = true
            };
            var current = folderBox.Text.Trim();
            if (current.Length > 0 && Directory.Exists(current)) fb.InitialDirectory = current;
            if (fb.ShowDialog(dialog) == DialogResult.OK) folderBox.Text = fb.SelectedPath;
        };
        y += 58;

        dialog.Controls.Add(Caption("ADD THE FOLDER TO", y));
        var desktopBox = new CheckBox { Text = "Desktop (shortcut)", AutoSize = true, Checked = true, Location = new Point(20, y + 18), BackColor = Color.White };
        var quickBox = new CheckBox { Text = "Quick Access in File Explorer", AutoSize = true, Checked = true, Location = new Point(180, y + 18), BackColor = Color.White };
        dialog.Controls.Add(desktopBox);
        dialog.Controls.Add(quickBox);
        y += 50;

        dialog.Controls.Add(Caption($"PASSWORD FOR '{ScanHost.UserName.ToUpperInvariant()}'", y));
        var passBox = new TextBox
        {
            Location = new Point(20, y + 18), Size = new Size(W - 40, 26), UseSystemPasswordChar = true,
            Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle
        };
        dialog.Controls.Add(passBox);
        y += 54;

        dialog.Controls.Add(Caption("CONFIRM PASSWORD", y));
        var confirmBox = new TextBox
        {
            Location = new Point(20, y + 18), Size = new Size(W - 40, 26), UseSystemPasswordChar = true,
            Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle
        };
        dialog.Controls.Add(confirmBox);
        y += 50;

        var showBox = new CheckBox { Text = "Show password", AutoSize = true, Location = new Point(20, y), BackColor = Color.White };
        showBox.CheckedChanged += (_, _) => passBox.UseSystemPasswordChar = confirmBox.UseSystemPasswordChar = !showBox.Checked;
        dialog.Controls.Add(showBox);

        var problem = new Label
        {
            Font = new Font("Segoe UI", 8.5f), ForeColor = ColorTranslator.FromHtml("#B3261E"), BackColor = Color.White,
            Location = new Point(160, y + 1), Size = new Size(W - 180, 36)
        };
        dialog.Controls.Add(problem);
        y += 42;

        var close = new FlatButton { Text = "Close", Style = ButtonStyle.Outline, Size = new Size(90, 34), Location = new Point(W - 20 - 90, y) };
        var configure = new FlatButton { Text = "Configure", Style = ButtonStyle.Primary, Size = new Size(130, 34), Location = new Point(W - 20 - 90 - 12 - 130, y) };
        dialog.Controls.Add(configure);
        dialog.Controls.Add(close);
        close.Click += (_, _) => dialog.Close();
        y += 46;

        var log = new TextBox
        {
            Location = new Point(20, y), Size = new Size(W - 40, H - y - 16), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.PanelBg
        };
        dialog.Controls.Add(log);
        var progress = new Progress<string>(line => log.AppendText(line + Environment.NewLine));

        void Validate()
        {
            problem.Text = passBox.Text.Length == 0 ? "" : ScanHost.CheckPassword(passBox.Text) ?? "";
        }
        passBox.TextChanged += (_, _) => Validate();

        configure.Click += async (_, _) =>
        {
            var folderText = folderBox.Text.Trim();
            var badFolder = ScanHost.CheckFolder(folderText);
            if (badFolder != null) { problem.Text = badFolder; return; }
            var folder = ScanHost.NormalizeFolder(folderText);
            var bad = ScanHost.CheckPassword(passBox.Text);
            if (bad != null) { problem.Text = bad; return; }
            if (passBox.Text != confirmBox.Text) { problem.Text = "The two passwords do not match."; return; }
            problem.Text = "";
            log.Clear();

            var shareName = ScanHost.ShareName;
            if (!PreflightScanHost(dialog, log, ref folder, ref shareName)) return;
            folderBox.Text = folder;

            configure.Enabled = browse.Enabled = false;
            folderBox.Enabled = passBox.Enabled = confirmBox.Enabled = desktopBox.Enabled = quickBox.Enabled = false;
            dialog.UseWaitCursor = true;
            bool ready = false;
            var savedPassword = passBox.Text;
            try
            {
                var output = await ScanHost.ConfigureAsync(folder, shareName, passBox.Text, desktopBox.Checked, quickBox.Checked, progress);
                ready = output.Contains("This PC is ready to receive scans", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                log.AppendText("FAILED: " + ex.Message + Environment.NewLine);
            }
            finally
            {
                if (!dialog.IsDisposed)
                {
                    passBox.Clear();
                    confirmBox.Clear();
                    configure.Enabled = browse.Enabled = true;
                    folderBox.Enabled = passBox.Enabled = confirmBox.Enabled = desktopBox.Enabled = quickBox.Enabled = true;
                    dialog.UseWaitCursor = false;
                }
            }

            if (ready && !dialog.IsDisposed)
                ShowScannerValuesDialog(dialog, ScanHost.LanAddress(), shareName, savedPassword);
        };

        dialog.ShowDialog(this);
    }

    /// <summary>
    /// Checks the account and share name before anything is changed, asking about the ones that need a decision.
    /// Returns false if the run should not go ahead. May change the folder or share name to what the user picked.
    /// </summary>
    private static bool PreflightScanHost(Form owner, TextBox log, ref string folder, ref string shareName)
    {
        try
        {
            var (state, reason) = ScanHost.CheckAccount();
            if (state == ScanHost.AccountState.Blocked)
            {
                MessageBox.Show(owner, reason, "Network scanning host", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (state == ScanHost.AccountState.Exists &&
                MessageBox.Show(owner,
                    $"A local account called '{ScanHost.UserName}' already exists on this PC.\n\n" +
                    "Its password will be changed to the one you entered. Any scanner already saving to this PC " +
                    "with the old password will stop working until it is given the new one.\n\nContinue?",
                    "Network scanning host", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;

            var existing = ScanHost.FindShare(shareName);
            if (existing != null && !(existing.IsFolder && existing.Path.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                var choice = ResolveShareConflict(owner, folder, existing);
                if (choice == null) return false;
                (folder, shareName) = choice.Value;
            }
        }
        catch (Exception ex)
        {
            // WMI can be broken on some PCs. The script repeats the share check, so carry on and let it report.
            log.AppendText("Could not check existing accounts and shares first (" + ex.Message + "). Continuing." + Environment.NewLine);
        }
        return true;
    }

    /// <summary>Asks what to do when the share name is already used: save into that share's folder, or pick a new share name.</summary>
    private static (string Folder, string Share)? ResolveShareConflict(Form owner, string folder, ScanHost.ExistingShare existing)
    {
        const int W = 560, H = 330;
        var dialog = NewDialog("Share name already in use", W, H, owner);

        string? existingProblem = !existing.IsFolder
            ? "Not available: that share is a printer or device, not a folder."
            : ScanHost.CheckFolder(existing.Path) is { } why ? "Not available: " + why : null;

        dialog.Controls.Add(new Label
        {
            Text = $"The share name '{existing.Name}' is already in use", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.TextDark, BackColor = Color.White, Location = new Point(20, 16), AutoSize = true
        });
        dialog.Controls.Add(new Label
        {
            Text = existing.IsFolder
                ? $"This PC already shares a different folder as '{existing.Name}':\n{existing.Path}"
                : $"'{existing.Name}' is already used by a shared printer or device on this PC.",
            Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextMuted, BackColor = Color.White,
            Location = new Point(20, 44), Size = new Size(W - 40, 36), AutoEllipsis = true
        });

        var useExisting = new RadioButton
        {
            Text = "Save scans into that folder instead", AutoSize = true, Location = new Point(20, 92),
            BackColor = Color.White, Enabled = existingProblem == null
        };
        dialog.Controls.Add(useExisting);
        dialog.Controls.Add(new Label
        {
            Text = existingProblem ?? $"Keeps the share '{existing.Name}' and gives '{ScanHost.UserName}' access to it.",
            Font = new Font("Segoe UI", 8.5f), ForeColor = existingProblem == null ? Theme.TextFaint : ColorTranslator.FromHtml("#B3261E"),
            BackColor = Color.White, Location = new Point(38, 114), Size = new Size(W - 58, 34), AutoEllipsis = true
        });

        var useNew = new RadioButton
        {
            Text = "Keep my folder and share it under a different name:", AutoSize = true, Location = new Point(20, 152), BackColor = Color.White
        };
        dialog.Controls.Add(useNew);
        dialog.Controls.Add(new Label
        {
            Text = folder, Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.TextFaint, BackColor = Color.White,
            Location = new Point(38, 174), Size = new Size(W - 58, 18), AutoEllipsis = true
        });
        var nameBox = new TextBox
        {
            Text = ScanHost.SuggestShareName(ScanHost.ShareName), Location = new Point(38, 196), Size = new Size(240, 26),
            Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle
        };
        dialog.Controls.Add(nameBox);
        // Clicking or typing in the name box picks that option (not Enter, which also fires when the dialog first gets focus).
        nameBox.MouseDown += (_, _) => useNew.Checked = true;
        nameBox.TextChanged += (_, _) => useNew.Checked = true;

        var problem = new Label
        {
            Font = new Font("Segoe UI", 8.5f), ForeColor = ColorTranslator.FromHtml("#B3261E"), BackColor = Color.White,
            Location = new Point(20, 232), Size = new Size(W - 40, 34)
        };
        dialog.Controls.Add(problem);

        if (existingProblem == null) useExisting.Checked = true; else useNew.Checked = true;

        var cancel = new FlatButton { Text = "Cancel", Style = ButtonStyle.Outline, Size = new Size(90, 34), Location = new Point(W - 20 - 90, H - 50) };
        var ok = new FlatButton { Text = "Continue", Style = ButtonStyle.Primary, Size = new Size(110, 34), Location = new Point(W - 20 - 90 - 12 - 110, H - 50) };
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        (string, string)? result = null;
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) =>
        {
            if (useExisting.Checked)
            {
                result = (existing.Path, existing.Name);
                dialog.Close();
                return;
            }
            var name = nameBox.Text;
            var bad = ScanHost.CheckShareName(name);
            if (bad != null) { problem.Text = bad; nameBox.Focus(); return; }
            result = (folder, name);
            dialog.Close();
        };

        dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Shows the values to type into the scanner's web interface, each with a Copy button.</summary>
    private void ShowScannerValuesDialog(Form owner, string host, string shareName, string password)
    {
        const int W = 560;
        var machine = Environment.MachineName;
        var rows = new (string Label, string Value, bool Secret)[]
        {
            ("HOST NAME OR IP ADDRESS", host, false),
            ("NETWORK FOLDER PATH", $@"\\{host}\{shareName}", false),
            ("USER NAME", ScanHost.UserName, false),
            ("PASSWORD", password, true),
        };
        int H = 150 + rows.Length * 58 + 90;
        var dialog = NewDialog("Scanner settings", W, H, owner);

        dialog.Controls.Add(new Label
        {
            Text = "Enter these on the scanner's web interface", Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.TextDark, BackColor = Color.White, Location = new Point(20, 16), AutoSize = true
        });
        dialog.Controls.Add(new Label
        {
            Text = "Save the password somewhere safe now, such as a password manager. It is not stored on this PC, " +
                   "and if it is lost you will need to run this again and update the scanner.",
            Font = new Font("Segoe UI", 8.75f), ForeColor = ColorTranslator.FromHtml("#B3261E"), BackColor = Color.White,
            Location = new Point(20, 44), Size = new Size(W - 40, 52)
        });

        int y = 104;
        foreach (var (label, value, secret) in rows)
        {
            dialog.Controls.Add(new Label
            {
                Text = label, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = Theme.TextFaint,
                BackColor = Color.White, Location = new Point(20, y), AutoSize = true
            });
            var box = new TextBox
            {
                Text = value, ReadOnly = true, Location = new Point(20, y + 18), Size = new Size(W - 40 - 160, 26),
                Font = Theme.FontMono, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White,
                UseSystemPasswordChar = secret
            };
            dialog.Controls.Add(box);
            int x = W - 20 - 150;
            if (secret)
            {
                var show = new FlatButton { Text = "Show", Style = ButtonStyle.Outline, Size = new Size(70, 28), Location = new Point(x, y + 17) };
                show.Click += (_, _) =>
                {
                    box.UseSystemPasswordChar = !box.UseSystemPasswordChar;
                    show.Text = box.UseSystemPasswordChar ? "Show" : "Hide";
                };
                dialog.Controls.Add(show);
                x += 78;
            }
            var copy = new FlatButton { Text = "Copy", Style = ButtonStyle.Primary, Size = new Size(70, 28), Location = new Point(x, y + 17) };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(value); copy.Text = "Copied"; }
                catch (Exception ex) { MessageBox.Show(dialog, "Could not copy: " + ex.Message, "Scanner settings"); }
            };
            dialog.Controls.Add(copy);
            y += 58;
        }

        dialog.Controls.Add(new Label
        {
            Text = $"If the scanner rejects the user name, try {machine}\\{ScanHost.UserName}. " +
                   $"Some models ask for the share name ({shareName}) and the host separately.",
            Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.TextMuted, BackColor = Color.White,
            Location = new Point(20, y + 4), Size = new Size(W - 40, 36)
        });
        var close = new FlatButton { Text = "Close", Style = ButtonStyle.Primary, Size = new Size(90, 34), Location = new Point(W - 20 - 90, H - 50) };
        close.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(close);
        dialog.ShowDialog(owner);
    }
}
