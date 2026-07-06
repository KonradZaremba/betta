// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Windows.Forms;
using Betta.Services;
using Rhino;
using Rhino.Commands;

namespace Betta.Commands
{
    /// <summary>
    /// Rhino command <c>Betta_Trust</c> — opens a WinForms dialog for
    /// managing the plugin-signing policy: enforcement mode, allowed
    /// publisher certificate thumbprints, import from cert file or signed
    /// DLL, remove entries. Persists to
    /// <c>%AppData%\Grasshopper\Libraries\Betta\trust.json</c>.
    ///
    /// The same dialog is wired to the GH main-window menu at startup so
    /// users don't have to memorize the command name.
    /// </summary>
    public sealed class BettaTrustCommand : Command
    {
        public override string EnglishName => "Betta_Trust";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ShowDialog();
            return Result.Success;
        }

        /// <summary>
        /// Shared entry point so the GH menu wire and the Rhino command open
        /// the same UI. Runs on the current thread; caller marshals to the UI
        /// thread if needed.
        /// </summary>
        public static void ShowDialog()
        {
            var policy = PluginTrustPolicy.LoadOrOff();
            using var dlg = new PluginTrustDialog(policy);
            var result = dlg.ShowDialog();
            if (result == DialogResult.OK)
            {
                dlg.Result.Save();
                RhinoApp.WriteLine("[Betta] Trust policy saved — mode: {0}, {1} publisher(s) trusted.",
                    dlg.Result.Mode, dlg.Result.AllowedThumbprints.Count);
            }
        }
    }

    /// <summary>
    /// Trusted-publisher settings dialog. Kept in the same file as the
    /// command because it's a leaf UI component — pulling it out would mean
    /// four files for a screen that fits on a single laptop pane.
    /// </summary>
    internal sealed class PluginTrustDialog : Form
    {
        private readonly ComboBox _modeCombo;
        private readonly ListView _list;
        public PluginTrustPolicy Result { get; }

        public PluginTrustDialog(PluginTrustPolicy policy)
        {
            Result = new PluginTrustPolicy
            {
                Mode = policy.Mode,
                AllowedThumbprints = new System.Collections.Generic.List<string>(policy.AllowedThumbprints ?? new System.Collections.Generic.List<string>())
            };

            Text = "Betta — Trusted publishers";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new System.Drawing.Size(560, 380);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = new Padding(10);

            var modeLabel = new Label
            {
                Text = "Enforcement:",
                Dock = DockStyle.Top,
                Height = 22,
            };
            _modeCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _modeCombo.Items.AddRange(new object[] { PluginTrustMode.Off, PluginTrustMode.WarnOnly, PluginTrustMode.Enforce });
            _modeCombo.SelectedItem = Result.Mode;
            _modeCombo.SelectedIndexChanged += (_, __) => Result.Mode = (PluginTrustMode)_modeCombo.SelectedItem;

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
            };
            _list.Columns.Add("Thumbprint", 260);
            _list.Columns.Add("Subject", 260);
            RefreshList();

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 40,
                Padding = new Padding(0, 8, 0, 0),
            };
            var importCert = new Button { Text = "Import cert…", AutoSize = true };
            var importDll = new Button { Text = "Import from signed DLL…", AutoSize = true };
            var remove = new Button { Text = "Remove", AutoSize = true };
            var spacer = new Panel { Width = 20, Height = 1 };
            var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttonRow.Controls.AddRange(new Control[] { importCert, importDll, remove, spacer, ok, cancel });

            importCert.Click += (_, __) => Import(fromDll: false);
            importDll.Click += (_, __) => Import(fromDll: true);
            remove.Click += (_, __) => RemoveSelected();

            AcceptButton = ok;
            CancelButton = cancel;

            // Add in reverse-visual order for Docking (bottom, top, then fill).
            Controls.Add(_list);
            Controls.Add(_modeCombo);
            Controls.Add(modeLabel);
            Controls.Add(buttonRow);
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            foreach (var t in Result.AllowedThumbprints)
                _list.Items.Add(new ListViewItem(new[] { t, "" }));
        }

        private void Import(bool fromDll)
        {
            using var ofd = new OpenFileDialog
            {
                Title = fromDll ? "Pick a signed DLL" : "Pick a certificate (.cer / .crt)",
                Filter = fromDll ? "Assembly (*.dll)|*.dll" : "Certificate (*.cer;*.crt)|*.cer;*.crt|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            bool ok = fromDll
                ? PluginTrustVerifier.TryReadSigner(ofd.FileName, out var thumb, out var subject)
                : PluginTrustVerifier.TryReadCertFile(ofd.FileName, out thumb, out subject);
            if (!ok)
            {
                MessageBox.Show(this,
                    fromDll ? "Could not read a signature from that DLL." : "Could not read that certificate.",
                    "Betta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Result.AllowedThumbprints.Any(t => string.Equals(t, thumb, StringComparison.OrdinalIgnoreCase)))
                Result.AllowedThumbprints.Add(thumb);

            _list.Items.Clear();
            foreach (var t in Result.AllowedThumbprints)
            {
                var subj = string.Equals(t, thumb, StringComparison.OrdinalIgnoreCase) ? subject : "";
                _list.Items.Add(new ListViewItem(new[] { t, subj }));
            }
        }

        private void RemoveSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in _list.SelectedItems)
                Result.AllowedThumbprints.Remove(item.SubItems[0].Text);
            RefreshList();
        }
    }
}
