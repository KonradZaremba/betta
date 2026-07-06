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
    /// Rhino command <c>Betta_Secrets</c> — opens a WinForms dialog to
    /// view, set, and remove secrets in the OS credential store. Backing
    /// storage is <see cref="SecretStore"/> (Windows Credential Manager,
    /// DPAPI-encrypted, per-user). Values are never displayed after entry.
    /// </summary>
    public sealed class BettaSecretsCommand : Command
    {
        public override string EnglishName => "Betta_Secrets";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ShowDialog();
            return Result.Success;
        }

        public static void ShowDialog()
        {
            using var dlg = new SecretsDialog();
            dlg.ShowDialog();
        }
    }

    internal sealed class SecretsDialog : Form
    {
        private readonly ListView _list;

        public SecretsDialog()
        {
            Text = "Betta — Secrets";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new System.Drawing.Size(500, 340);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = new Padding(10);

            var hint = new Label
            {
                Text = "Values are stored in Windows Credential Manager (DPAPI-encrypted, per-user). They are never displayed here.",
                Dock = DockStyle.Top,
                Height = 42,
                AutoSize = false,
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
            };
            _list.Columns.Add("Service", 320);
            _list.Columns.Add("State", 120);
            RefreshList();

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 40,
                Padding = new Padding(0, 8, 0, 0),
            };
            var setBtn = new Button { Text = "Set…", AutoSize = true };
            var removeBtn = new Button { Text = "Remove", AutoSize = true };
            var closeBtn = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            row.Controls.AddRange(new Control[] { setBtn, removeBtn, closeBtn });

            setBtn.Click += (_, __) => SetSecret();
            removeBtn.Click += (_, __) => RemoveSelected();

            CancelButton = closeBtn;

            Controls.Add(_list);
            Controls.Add(hint);
            Controls.Add(row);
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            foreach (var svc in SecretStore.List().OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                _list.Items.Add(new ListViewItem(new[] { svc, "set" }));
        }

        private void SetSecret()
        {
            using var input = new SetSecretDialog();
            if (input.ShowDialog(this) != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(input.ServiceKey)) return;

            if (SecretStore.Set(input.ServiceKey.Trim(), input.SecretValue))
                RhinoApp.WriteLine("[Betta] Secret set for '{0}'.", input.ServiceKey.Trim());
            else
                RhinoApp.WriteLine("[Betta] Could not set secret for '{0}'.", input.ServiceKey.Trim());

            RefreshList();
        }

        private void RemoveSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var svc = _list.SelectedItems[0].SubItems[0].Text;
            if (MessageBox.Show(this, $"Remove stored secret for '{svc}'?", "Betta",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            SecretStore.Delete(svc);
            RhinoApp.WriteLine("[Betta] Secret cleared for '{0}'.", svc);
            RefreshList();
        }
    }

    internal sealed class SetSecretDialog : Form
    {
        private readonly TextBox _serviceBox;
        private readonly TextBox _valueBox;
        public string ServiceKey => _serviceBox.Text;
        public string SecretValue => _valueBox.Text;

        public SetSecretDialog()
        {
            Text = "Betta — Set secret";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(420, 180);
            Padding = new Padding(10);

            var svcLbl = new Label { Text = "Service key (e.g. openai.api_key):", Top = 10, Left = 10, Width = 400 };
            _serviceBox = new TextBox { Top = 32, Left = 10, Width = 400 };
            var valLbl = new Label { Text = "Value:", Top = 62, Left = 10, Width = 400 };
            _valueBox = new TextBox { Top = 84, Left = 10, Width = 400, UseSystemPasswordChar = true };

            var ok = new Button { Text = "OK", Left = 224, Top = 130, DialogResult = DialogResult.OK, Width = 80 };
            var cancel = new Button { Text = "Cancel", Left = 320, Top = 130, DialogResult = DialogResult.Cancel, Width = 80 };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { svcLbl, _serviceBox, valLbl, _valueBox, ok, cancel });
        }
    }
}
