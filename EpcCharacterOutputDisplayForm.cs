using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImpinjR700
{
    public sealed class EpcCharacterOutputDisplayForm : Form
    {
        private readonly TextBox _textOutput = new();

        public EpcCharacterOutputDisplayForm()
        {
            Text = "字符输出演示";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 360);
            Size = new Size(900, 520);
            BackColor = Color.Black;

            _textOutput.Dock = DockStyle.Fill;
            _textOutput.ForeColor = Color.White;
            _textOutput.BackColor = Color.Black;
            _textOutput.BorderStyle = BorderStyle.None;
            _textOutput.Font = new Font("Microsoft YaHei", 56F, FontStyle.Bold, GraphicsUnit.Point);
            _textOutput.Multiline = true;
            _textOutput.ReadOnly = true;
            _textOutput.ScrollBars = ScrollBars.Vertical;
            _textOutput.WordWrap = true;
            _textOutput.TextAlign = HorizontalAlignment.Center;
            Controls.Add(_textOutput);
        }

        public void SetOutputText(string output)
        {
            _textOutput.Text = string.IsNullOrEmpty(output) ? " " : output;
            _textOutput.SelectionStart = _textOutput.TextLength;
            _textOutput.ScrollToCaret();
        }
    }
}
