// InputBoxValidate: show an inputbox and optionally validate its return value
// based on https://www.csharp-examples.net/inputbox-class/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sql2SqlCloner.Components
{
    public class InputBoxValidate : Form
    {
        private readonly Button buttonOk = new Button();
        private readonly Button buttonCancel = new Button();
        private readonly Label label = new Label();
        private readonly TextBox textBox = new TextBox();

        public InputBoxValidate(string title, string promptText, bool validateLong = false, bool hideChars = false, Icon icon = null)
        {
            if (icon != null)
            {
                Icon = icon;
            }

            Text = title;
            label.Text = promptText;

            if (hideChars)
            {
                textBox.PasswordChar = '*';
            }

            buttonOk.Text = nameof(DialogResult.OK);
            buttonCancel.Text = nameof(DialogResult.Cancel);
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            buttonOk.SetBounds(228, 72, 75, 23);
            buttonCancel.SetBounds(309, 72, 75, 23);
            label.SetBounds(9, 20, 372, 13);
            textBox.SetBounds(12, 36, 372, 20);

            buttonOk.Anchor = buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            label.AutoSize = true;
            textBox.Anchor |= AnchorStyles.Right;

            ClientSize = new Size(396, 107);
            Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            ClientSize = new Size(Math.Max(300, label.Right + 10), ClientSize.Height);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = false;
            AcceptButton = buttonOk;
            CancelButton = buttonCancel;
            if (validateLong)
            {
                FormClosing += (sender, e) =>
                {
                    if (DialogResult == DialogResult.OK && !long.TryParse(textBox.Text, out long _))
                    {
                        MessageBox.Show(this, "Value is not numeric", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        textBox.Focus();
                        e.Cancel = true;
                    }
                };
            }
        }

        public DialogResult ShowDialog(ref string value)
        {
            textBox.Text = value;
            DialogResult dialogResult = ShowDialog();
            value = textBox.Text;
            return dialogResult;
        }
    }
}
