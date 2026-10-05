using System.Drawing;
using System.Windows.Forms;

namespace NumPaDeck.UI;

/// <summary>Minimal text input dialog (WinForms has no built-in one).</summary>
public sealed class PromptForm : Form
{
    private readonly TextBox _box;

    public string Value { get; private set; } = "";

    public PromptForm(string title, string prompt, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 140);

        var label = new Label
        {
            Text = prompt,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(16, 16)
        };

        _box = new TextBox
        {
            Location = new Point(16, 44),
            Size = new Size(ClientSize.Width - 32, 28),
            Text = initial,
            Font = new Font("Segoe UI", 10f)
        };

        var ok = new Button
        {
            Text = "OK",
            Size = new Size(72, 30),
            Location = new Point(ClientSize.Width - 156, 86)
        };
        ok.Click += (s, e) =>
        {
            string v = _box.Text.Trim();
            if (v.Length == 0)
            {
                MessageBox.Show(this, "Please enter a value.", title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Value = v;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Size = new Size(72, 30),
            Location = new Point(ClientSize.Width - 76, 86)
        };
        cancel.Click += (s, e) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(label);
        Controls.Add(_box);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (s, e) =>
        {
            _box.Focus();
            _box.Select(0, _box.Text.Length);
        };
    }
}
