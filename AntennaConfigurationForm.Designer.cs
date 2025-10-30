using System.ComponentModel;
using System.Windows.Forms;

namespace ImpinjR700
{
    partial class AntennaConfigurationForm
    {
        /// <summary>
        ///  设计器支持的组件容器。
        /// </summary>
        private IContainer components = null!;

        /// <summary>
        ///  清理所使用的资源。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows 窗体设计生成的代码

        private void InitializeComponent()
        {
            this.gridAntennas = new System.Windows.Forms.DataGridView();
            this.columnPort = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnIsEnabled = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.columnTxPower = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.columnRxSensitivity = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.columnConnectionStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.buttonRefresh = new System.Windows.Forms.Button();
            this.buttonApply = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.labelStatus = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.gridAntennas)).BeginInit();
            this.SuspendLayout();
            // 
            // gridAntennas
            // 
            this.gridAntennas.AllowUserToAddRows = false;
            this.gridAntennas.AllowUserToDeleteRows = false;
            this.gridAntennas.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.gridAntennas.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridAntennas.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridAntennas.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.columnPort,
            this.columnIsEnabled,
            this.columnTxPower,
            this.columnRxSensitivity,
            this.columnConnectionStatus});
            this.gridAntennas.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            this.gridAntennas.Location = new System.Drawing.Point(12, 12);
            this.gridAntennas.MultiSelect = false;
            this.gridAntennas.Name = "gridAntennas";
            this.gridAntennas.RowHeadersVisible = false;
            this.gridAntennas.RowTemplate.Height = 28;
            this.gridAntennas.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridAntennas.Size = new System.Drawing.Size(656, 312);
            this.gridAntennas.TabIndex = 0;
            this.gridAntennas.DataError += new System.Windows.Forms.DataGridViewDataErrorEventHandler(this.gridAntennas_DataError);
            this.gridAntennas.CurrentCellDirtyStateChanged += new System.EventHandler(this.gridAntennas_CurrentCellDirtyStateChanged);
            // 
            // columnPort
            // 
            this.columnPort.DataPropertyName = "Port";
            this.columnPort.FillWeight = 60F;
            this.columnPort.HeaderText = "端口号";
            this.columnPort.MinimumWidth = 60;
            this.columnPort.Name = "columnPort";
            this.columnPort.ReadOnly = true;
            // 
            // columnIsEnabled
            // 
            this.columnIsEnabled.DataPropertyName = "IsEnabled";
            this.columnIsEnabled.FillWeight = 70F;
            this.columnIsEnabled.HeaderText = "启用";
            this.columnIsEnabled.MinimumWidth = 70;
            this.columnIsEnabled.Name = "columnIsEnabled";
            // 
            // columnTxPower
            // 
            this.columnTxPower.DataPropertyName = "TxPower";
            this.columnTxPower.FillWeight = 140F;
            this.columnTxPower.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.columnTxPower.HeaderText = "发射功率 (dBm)";
            this.columnTxPower.MinimumWidth = 140;
            this.columnTxPower.Name = "columnTxPower";
            this.columnTxPower.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.columnTxPower.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            // 
            // columnRxSensitivity
            // 
            this.columnRxSensitivity.DataPropertyName = "RxSensitivity";
            this.columnRxSensitivity.FillWeight = 150F;
            this.columnRxSensitivity.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.columnRxSensitivity.HeaderText = "接收灵敏度 (dBm)";
            this.columnRxSensitivity.MinimumWidth = 150;
            this.columnRxSensitivity.Name = "columnRxSensitivity";
            this.columnRxSensitivity.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.columnRxSensitivity.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            // 
            // columnConnectionStatus
            // 
            this.columnConnectionStatus.DataPropertyName = "ConnectionStatus";
            this.columnConnectionStatus.FillWeight = 160F;
            this.columnConnectionStatus.HeaderText = "连接状态";
            this.columnConnectionStatus.MinimumWidth = 120;
            this.columnConnectionStatus.Name = "columnConnectionStatus";
            this.columnConnectionStatus.ReadOnly = true;
            // 
            // buttonRefresh
            // 
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.buttonRefresh.Location = new System.Drawing.Point(12, 338);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(120, 32);
            this.buttonRefresh.TabIndex = 1;
            this.buttonRefresh.Text = "刷新状态";
            this.buttonRefresh.UseVisualStyleBackColor = true;
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            // 
            // buttonApply
            // 
            this.buttonApply.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonApply.Location = new System.Drawing.Point(440, 338);
            this.buttonApply.Name = "buttonApply";
            this.buttonApply.Size = new System.Drawing.Size(120, 32);
            this.buttonApply.TabIndex = 2;
            this.buttonApply.Text = "保存并应用";
            this.buttonApply.UseVisualStyleBackColor = true;
            this.buttonApply.Click += new System.EventHandler(this.buttonApply_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(568, 338);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(100, 32);
            this.buttonCancel.TabIndex = 3;
            this.buttonCancel.Text = "取消";
            this.buttonCancel.UseVisualStyleBackColor = true;
            // 
            // labelStatus
            // 
            this.labelStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labelStatus.AutoEllipsis = true;
            this.labelStatus.Location = new System.Drawing.Point(12, 377);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Size = new System.Drawing.Size(656, 20);
            this.labelStatus.TabIndex = 4;
            this.labelStatus.Text = "正在初始化...";
            // 
            // AntennaConfigurationForm
            // 
            this.AcceptButton = this.buttonApply;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(680, 406);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonApply);
            this.Controls.Add(this.buttonRefresh);
            this.Controls.Add(this.gridAntennas);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AntennaConfigurationForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "详细天线配置";
            ((System.ComponentModel.ISupportInitialize)(this.gridAntennas)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.DataGridView gridAntennas;
        private System.Windows.Forms.Button buttonRefresh;
        private System.Windows.Forms.Button buttonApply;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnPort;
        private System.Windows.Forms.DataGridViewCheckBoxColumn columnIsEnabled;
        private System.Windows.Forms.DataGridViewComboBoxColumn columnTxPower;
        private System.Windows.Forms.DataGridViewComboBoxColumn columnRxSensitivity;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnConnectionStatus;
    }
}
