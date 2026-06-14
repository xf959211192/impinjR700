using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ScottPlot.WinForms;

namespace ImpinjR700
{
    internal sealed class FullscreenPlotForm : Form
    {
        private readonly Panel _plotPanel = new();
        private readonly List<FormsPlot> _plotControls = new();
        private int _plotHeight;

        public FullscreenPlotForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 360);
            Size = new Size(1100, 640);
            BackColor = Color.White;
            KeyPreview = true;

            _plotPanel.Dock = DockStyle.Fill;
            _plotPanel.AutoScroll = true;
            _plotPanel.BackColor = Color.White;
            _plotPanel.SizeChanged += (_, _) => LayoutPlotControls();
            Controls.Add(_plotPanel);

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };
        }

        public IReadOnlyList<FormsPlot> SetPlotCount(int count, int plotHeight)
        {
            _plotHeight = Math.Max(120, plotHeight);

            while (_plotControls.Count < count)
            {
                var plotControl = new FormsPlot
                {
                    Margin = Padding.Empty
                };
                _plotControls.Add(plotControl);
                _plotPanel.Controls.Add(plotControl);
            }

            while (_plotControls.Count > count)
            {
                var lastIndex = _plotControls.Count - 1;
                var plotControl = _plotControls[lastIndex];
                _plotPanel.Controls.Remove(plotControl);
                _plotControls.RemoveAt(lastIndex);
                plotControl.Dispose();
            }

            LayoutPlotControls();
            return _plotControls;
        }

        public void RefreshPlot()
        {
            foreach (var plotControl in _plotControls)
            {
                plotControl.Refresh();
            }
        }

        private void LayoutPlotControls()
        {
            if (_plotControls.Count == 0)
            {
                _plotPanel.AutoScrollMinSize = Size.Empty;
                return;
            }

            var width = Math.Max(120, _plotPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            var height = _plotControls.Count == 1
                ? Math.Max(120, _plotPanel.ClientSize.Height)
                : _plotHeight;

            var y = 0;
            foreach (var plotControl in _plotControls)
            {
                plotControl.SetBounds(0, y, width, height);
                y += height;
            }

            _plotPanel.AutoScrollMinSize = new Size(0, y);
        }
    }
}
