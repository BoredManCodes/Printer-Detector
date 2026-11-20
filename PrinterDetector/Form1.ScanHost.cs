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

        const int W = 620, H = 640;
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
                   $"  •  give that account full access to the folder and read / write on a share called '{ScanHost.ShareName}'",
            Font = new Font("Segoe UI", 8.75f), ForeColor = Theme.TextMuted, BackColor = Color.White,
            Location = new Point(20, 44), Size = new Size(W - 40, 84)
        });

        int y = 140;
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
            var folder = folderBox.Text.Trim();
            if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
            {
                problem.Text = "Enter a full folder path such as C:\\Scans.";
                return;
            }
            var bad = ScanHost.CheckPassword(passBox.Text);
            if (bad != null) { problem.Text = bad; return; }
            if (passBox.Text != confirmBox.Text) { problem.Text = "The two passwords do not match."; return; }
            problem.Text = "";

            configure.Enabled = browse.Enabled = false;
            folderBox.Enabled = passBox.Enabled = confirmBox.Enabled = false;
            dialog.UseWaitCursor = true;
            log.Clear();
            try
            {
                await ScanHost.ConfigureAsync(folder, passBox.Text, progress);
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
                    folderBox.Enabled = passBox.Enabled = confirmBox.Enabled = true;
                    dialog.UseWaitCursor = false;
                }
            }
        };

        dialog.ShowDialog(this);
    }
}
