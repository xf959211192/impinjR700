using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ImpinjR700
{
    public sealed class StatisticsForm : Form
    {
        private readonly Label _labelRecordCount = new();
        private readonly ListView _listStatistics = new();

        public StatisticsForm()
        {
            Text = "统计信息";
            StartPosition = FormStartPosition.Manual;
            Size = new Size(980, 520);
            MinimumSize = new Size(760, 360);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var panel = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _labelRecordCount.AutoSize = true;
            _labelRecordCount.Font = new Font(Font, FontStyle.Bold);
            _labelRecordCount.Margin = new Padding(0, 0, 0, 8);
            _labelRecordCount.Text = "记录数：0";

            _listStatistics.Dock = DockStyle.Fill;
            _listStatistics.FullRowSelect = true;
            _listStatistics.GridLines = true;
            _listStatistics.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _listStatistics.HideSelection = false;
            _listStatistics.MultiSelect = false;
            _listStatistics.UseCompatibleStateImageBehavior = false;
            _listStatistics.View = View.Details;
            _listStatistics.Columns.Add("统计对象", 320);
            _listStatistics.Columns.Add("读取次数", 90);
            _listStatistics.Columns.Add("读取速率(次/秒)", 120);
            _listStatistics.Columns.Add("当前 RSSI", 100);
            _listStatistics.Columns.Add("最大值", 90);
            _listStatistics.Columns.Add("最小值", 90);
            _listStatistics.Columns.Add("RSSI 均值", 100);
            _listStatistics.Columns.Add("标准差", 90);
            _listStatistics.Columns.Add("变异系数", 100);

            panel.Controls.Add(_labelRecordCount, 0, 0);
            panel.Controls.Add(_listStatistics, 0, 1);
            Controls.Add(panel);
        }

        public void UpdateStatistics(int recordCount, IReadOnlyList<string[]> rows)
        {
            _labelRecordCount.Text = $"记录数：{recordCount}";

            _listStatistics.BeginUpdate();
            try
            {
                _listStatistics.Items.Clear();
                foreach (var row in rows)
                {
                    _listStatistics.Items.Add(new ListViewItem(row));
                }
            }
            finally
            {
                _listStatistics.EndUpdate();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }
    }
}
