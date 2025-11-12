namespace ImpinjR700
{
    partial class Form1
    {
        /// <summary>
        ///  必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，则为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计生成的代码

        /// <summary>
        ///  设计器支持所需的方法 - 请勿使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            // 主界面分为上部数据展示区与下部日志统计区
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.tableTop = new System.Windows.Forms.TableLayoutPanel();
            // 顶部布局包含设备管理、读取控制、数据导出三个区域
            this.tableHeader = new System.Windows.Forms.TableLayoutPanel();
            this.groupConnection = new System.Windows.Forms.GroupBox();
            this.labelStatusValue = new System.Windows.Forms.Label();
            this.labelStatusCaption = new System.Windows.Forms.Label();
            this.buttonDisconnect = new System.Windows.Forms.Button();
            this.buttonReaderInfo = new System.Windows.Forms.Button();
            this.buttonConnect = new System.Windows.Forms.Button();
            this.textReaderIp = new System.Windows.Forms.TextBox();
            this.labelReaderIp = new System.Windows.Forms.Label();
            this.groupControl = new System.Windows.Forms.GroupBox();
            this.checkPlotSelectionOnly = new System.Windows.Forms.CheckBox();
            this.buttonAntennaConfig = new System.Windows.Forms.Button();
            this.checkedListAntennas = new System.Windows.Forms.CheckedListBox();
            this.labelAntennaSelection = new System.Windows.Forms.Label();
            this.checkAutoReconnect = new System.Windows.Forms.CheckBox();
            this.buttonStop = new System.Windows.Forms.Button();
            this.buttonStart = new System.Windows.Forms.Button();
            this.groupExport = new System.Windows.Forms.GroupBox();
            this.buttonClear = new System.Windows.Forms.Button();
            this.buttonExportExcel = new System.Windows.Forms.Button();
            this.buttonExportCsv = new System.Windows.Forms.Button();
            this.labelRecordCountValue = new System.Windows.Forms.Label();
            this.labelRecordCountCaption = new System.Windows.Forms.Label();
            this.gridTags = new System.Windows.Forms.DataGridView();
            this.columnEpc = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnAntenna = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnRssi = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnPhase = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnReadCount = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnFirstSeen = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.columnLastSeen = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.tabBottom = new System.Windows.Forms.TabControl();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.textLog = new System.Windows.Forms.TextBox();
            this.tabStatistics = new System.Windows.Forms.TabPage();
            this.tableStats = new System.Windows.Forms.TableLayoutPanel();
            this.listStatistics = new System.Windows.Forms.ListView();
            this.columnStatName = new System.Windows.Forms.ColumnHeader();
            this.columnStatValue = new System.Windows.Forms.ColumnHeader();
            this.groupEpcSelection = new System.Windows.Forms.GroupBox();
            this.checkedListEpcSelection = new System.Windows.Forms.CheckedListBox();
            this.tabChart = new System.Windows.Forms.TabPage();
            this.formsPlotRssi = new ScottPlot.WinForms.FormsPlot();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.tableTop.SuspendLayout();
            this.tableHeader.SuspendLayout();
            this.groupConnection.SuspendLayout();
            this.groupControl.SuspendLayout();
            this.groupExport.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridTags)).BeginInit();
            this.tabBottom.SuspendLayout();
            this.tabLog.SuspendLayout();
            this.tabStatistics.SuspendLayout();
            this.tableStats.SuspendLayout();
            this.groupEpcSelection.SuspendLayout();
            this.tabChart.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(0, 0);
            this.splitMain.Name = "splitMain";
            this.splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.tableTop);
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.tabBottom);
            this.splitMain.Size = new System.Drawing.Size(1100, 720);
            this.splitMain.SplitterDistance = 360;
            this.splitMain.TabIndex = 0;
            // 
            // tableTop
            // 
            this.tableTop.ColumnCount = 1;
            this.tableTop.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableTop.Controls.Add(this.tableHeader, 0, 0);
            this.tableTop.Controls.Add(this.gridTags, 0, 1);
            this.tableTop.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableTop.Location = new System.Drawing.Point(0, 0);
            this.tableTop.Name = "tableTop";
            this.tableTop.RowCount = 2;
            this.tableTop.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tableTop.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableTop.Size = new System.Drawing.Size(1100, 500);
            this.tableTop.TabIndex = 0;
            // 
            // tableHeader
            // 
            this.tableHeader.ColumnCount = 3;
            this.tableHeader.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableHeader.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableHeader.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.tableHeader.Controls.Add(this.groupConnection, 0, 0);
            this.tableHeader.Controls.Add(this.groupControl, 1, 0);
            this.tableHeader.Controls.Add(this.groupExport, 2, 0);
            this.tableHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableHeader.Location = new System.Drawing.Point(3, 3);
            this.tableHeader.Name = "tableHeader";
            this.tableHeader.RowCount = 1;
            this.tableHeader.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableHeader.Size = new System.Drawing.Size(1094, 160);
            this.tableHeader.TabIndex = 0;
            // 
            // groupConnection
            // 
            this.groupConnection.Controls.Add(this.labelStatusValue);
            this.groupConnection.Controls.Add(this.labelStatusCaption);
            this.groupConnection.Controls.Add(this.buttonReaderInfo);
            this.groupConnection.Controls.Add(this.buttonDisconnect);
            this.groupConnection.Controls.Add(this.buttonConnect);
            this.groupConnection.Controls.Add(this.textReaderIp);
            this.groupConnection.Controls.Add(this.labelReaderIp);
            this.groupConnection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupConnection.Location = new System.Drawing.Point(3, 3);
            this.groupConnection.Name = "groupConnection";
            this.groupConnection.Size = new System.Drawing.Size(358, 138);
            this.groupConnection.TabIndex = 0;
            this.groupConnection.TabStop = false;
            this.groupConnection.Text = "设备管理";
            // 
            // labelStatusValue
            // 
            this.labelStatusValue.AutoSize = true;
            this.labelStatusValue.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.labelStatusValue.ForeColor = System.Drawing.Color.DarkRed;
            this.labelStatusValue.Location = new System.Drawing.Point(260, 109);
            this.labelStatusValue.Name = "labelStatusValue";
            this.labelStatusValue.Size = new System.Drawing.Size(56, 17);
            this.labelStatusValue.TabIndex = 5;
            this.labelStatusValue.Text = "未连接";
            // 
            // labelStatusCaption
            // 
            this.labelStatusCaption.AutoSize = true;
            this.labelStatusCaption.Location = new System.Drawing.Point(190, 109);
            this.labelStatusCaption.Name = "labelStatusCaption";
            this.labelStatusCaption.Size = new System.Drawing.Size(56, 17);
            this.labelStatusCaption.TabIndex = 4;
            this.labelStatusCaption.Text = "当前状态";
            // 
            // buttonDisconnect
            // 
            this.buttonDisconnect.Location = new System.Drawing.Point(190, 62);
            this.buttonDisconnect.Name = "buttonDisconnect";
            this.buttonDisconnect.Size = new System.Drawing.Size(120, 30);
            this.buttonDisconnect.TabIndex = 3;
            this.buttonDisconnect.Text = "断开";
            this.buttonDisconnect.UseVisualStyleBackColor = true;
            // 
            // buttonReaderInfo
            // 
            this.buttonReaderInfo.Location = new System.Drawing.Point(50, 102);
            this.buttonReaderInfo.Name = "buttonReaderInfo";
            this.buttonReaderInfo.Size = new System.Drawing.Size(120, 30);
            this.buttonReaderInfo.TabIndex = 6;
            this.buttonReaderInfo.Text = "读写器信息";
            this.buttonReaderInfo.UseVisualStyleBackColor = true;
            // 
            // buttonConnect
            // 
            this.buttonConnect.Location = new System.Drawing.Point(50, 62);
            this.buttonConnect.Name = "buttonConnect";
            this.buttonConnect.Size = new System.Drawing.Size(120, 30);
            this.buttonConnect.TabIndex = 2;
            this.buttonConnect.Text = "连接";
            this.buttonConnect.UseVisualStyleBackColor = true;
            // 
            // textReaderIp
            // 
            this.textReaderIp.Location = new System.Drawing.Point(86, 27);
            this.textReaderIp.Name = "textReaderIp";
            this.textReaderIp.Size = new System.Drawing.Size(224, 23);
            this.textReaderIp.TabIndex = 1;
            this.textReaderIp.Text = "169.254.1.1";
            // 
            // labelReaderIp
            // 
            this.labelReaderIp.AutoSize = true;
            this.labelReaderIp.Location = new System.Drawing.Point(16, 30);
            this.labelReaderIp.Name = "labelReaderIp";
            this.labelReaderIp.Size = new System.Drawing.Size(68, 17);
            this.labelReaderIp.TabIndex = 0;
            this.labelReaderIp.Text = "读写器 IP";
            // 
            // groupControl
            // 
            this.groupControl.Controls.Add(this.buttonAntennaConfig);
            this.groupControl.Controls.Add(this.checkedListAntennas);
            this.groupControl.Controls.Add(this.labelAntennaSelection);
            this.groupControl.Controls.Add(this.checkAutoReconnect);
            this.groupControl.Controls.Add(this.buttonStop);
            this.groupControl.Controls.Add(this.buttonStart);
            this.groupControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupControl.Location = new System.Drawing.Point(367, 3);
            this.groupControl.Name = "groupControl";
            this.groupControl.Size = new System.Drawing.Size(358, 160);
            this.groupControl.TabIndex = 1;
            this.groupControl.TabStop = false;
            this.groupControl.Text = "读取控制";
            // 
            // checkPlotSelectionOnly
            // 
            this.checkPlotSelectionOnly.AutoSize = true;
            this.checkPlotSelectionOnly.Location = new System.Drawing.Point(150, 24);
            this.checkPlotSelectionOnly.Name = "checkPlotSelectionOnly";
            this.checkPlotSelectionOnly.Size = new System.Drawing.Size(138, 21);
            this.checkPlotSelectionOnly.TabIndex = 6;
            this.checkPlotSelectionOnly.Text = "仅绘制选中标签";
            this.checkPlotSelectionOnly.UseVisualStyleBackColor = true;
            // 
            // buttonAntennaConfig
            // 
            this.buttonAntennaConfig.Location = new System.Drawing.Point(200, 48);
            this.buttonAntennaConfig.Name = "buttonAntennaConfig";
            this.buttonAntennaConfig.Size = new System.Drawing.Size(120, 30);
            this.buttonAntennaConfig.TabIndex = 2;
            this.buttonAntennaConfig.Text = "详细配置...";
            this.buttonAntennaConfig.UseVisualStyleBackColor = true;
            // 
            // checkedListAntennas
            // 
            this.checkedListAntennas.CheckOnClick = true;
            this.checkedListAntennas.FormattingEnabled = true;
            this.checkedListAntennas.IntegralHeight = false;
            this.checkedListAntennas.Location = new System.Drawing.Point(26, 63);
            this.checkedListAntennas.Name = "checkedListAntennas";
            this.checkedListAntennas.Size = new System.Drawing.Size(150, 96);
            this.checkedListAntennas.TabIndex = 1;
            // 
            // labelAntennaSelection
            // 
            this.labelAntennaSelection.AutoSize = true;
            this.labelAntennaSelection.Location = new System.Drawing.Point(24, 32);
            this.labelAntennaSelection.Name = "labelAntennaSelection";
            this.labelAntennaSelection.Size = new System.Drawing.Size(104, 17);
            this.labelAntennaSelection.TabIndex = 0;
            this.labelAntennaSelection.Text = "选择天线端口：";
            // 
            // checkAutoReconnect
            // 
            this.checkAutoReconnect.AutoSize = true;
            this.checkAutoReconnect.Location = new System.Drawing.Point(200, 138);
            this.checkAutoReconnect.Name = "checkAutoReconnect";
            this.checkAutoReconnect.Size = new System.Drawing.Size(138, 21);
            this.checkAutoReconnect.TabIndex = 5;
            this.checkAutoReconnect.Text = "连接异常自动重试";
            this.checkAutoReconnect.UseVisualStyleBackColor = true;
            // 
            // buttonStop
            // 
            this.buttonStop.Location = new System.Drawing.Point(200, 118);
            this.buttonStop.Name = "buttonStop";
            this.buttonStop.Size = new System.Drawing.Size(120, 30);
            this.buttonStop.TabIndex = 4;
            this.buttonStop.Text = "停止读取";
            this.buttonStop.UseVisualStyleBackColor = true;
            // 
            // buttonStart
            // 
            this.buttonStart.Location = new System.Drawing.Point(200, 84);
            this.buttonStart.Name = "buttonStart";
            this.buttonStart.Size = new System.Drawing.Size(120, 30);
            this.buttonStart.TabIndex = 3;
            this.buttonStart.Text = "开始读取";
            this.buttonStart.UseVisualStyleBackColor = true;
            // 
            // groupExport
            // 
            this.groupExport.Controls.Add(this.buttonClear);
            this.groupExport.Controls.Add(this.checkPlotSelectionOnly);
            this.groupExport.Controls.Add(this.buttonExportExcel);
            this.groupExport.Controls.Add(this.buttonExportCsv);
            this.groupExport.Controls.Add(this.labelRecordCountValue);
            this.groupExport.Controls.Add(this.labelRecordCountCaption);
            this.groupExport.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupExport.Location = new System.Drawing.Point(731, 3);
            this.groupExport.Name = "groupExport";
            this.groupExport.Size = new System.Drawing.Size(360, 138);
            this.groupExport.TabIndex = 2;
            this.groupExport.TabStop = false;
            this.groupExport.Text = "数据导出与统计";
            // 
            // buttonClear
            // 
            this.buttonClear.Location = new System.Drawing.Point(242, 62);
            this.buttonClear.Name = "buttonClear";
            this.buttonClear.Size = new System.Drawing.Size(100, 30);
            this.buttonClear.TabIndex = 4;
            this.buttonClear.Text = "清空记录";
            this.buttonClear.UseVisualStyleBackColor = true;
            // 
            // buttonExportExcel
            // 
            this.buttonExportExcel.Location = new System.Drawing.Point(132, 62);
            this.buttonExportExcel.Name = "buttonExportExcel";
            this.buttonExportExcel.Size = new System.Drawing.Size(100, 30);
            this.buttonExportExcel.TabIndex = 3;
            this.buttonExportExcel.Text = "导出 Excel";
            this.buttonExportExcel.UseVisualStyleBackColor = true;
            // 
            // buttonExportCsv
            // 
            this.buttonExportCsv.Location = new System.Drawing.Point(22, 62);
            this.buttonExportCsv.Name = "buttonExportCsv";
            this.buttonExportCsv.Size = new System.Drawing.Size(100, 30);
            this.buttonExportCsv.TabIndex = 2;
            this.buttonExportCsv.Text = "导出 CSV";
            this.buttonExportCsv.UseVisualStyleBackColor = true;
            // 
            // labelRecordCountValue
            // 
            this.labelRecordCountValue.AutoSize = true;
            this.labelRecordCountValue.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.labelRecordCountValue.Location = new System.Drawing.Point(100, 30);
            this.labelRecordCountValue.Name = "labelRecordCountValue";
            this.labelRecordCountValue.Size = new System.Drawing.Size(15, 17);
            this.labelRecordCountValue.TabIndex = 1;
            this.labelRecordCountValue.Text = "0";
            // 
            // labelRecordCountCaption
            // 
            this.labelRecordCountCaption.AutoSize = true;
            this.labelRecordCountCaption.Location = new System.Drawing.Point(19, 30);
            this.labelRecordCountCaption.Name = "labelRecordCountCaption";
            this.labelRecordCountCaption.Size = new System.Drawing.Size(68, 17);
            this.labelRecordCountCaption.TabIndex = 0;
            this.labelRecordCountCaption.Text = "标签总数";
            // 
            // gridTags
            // 
            this.gridTags.AllowUserToAddRows = false;
            this.gridTags.AllowUserToDeleteRows = false;
            this.gridTags.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridTags.BackgroundColor = System.Drawing.Color.White;
            this.gridTags.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridTags.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.columnEpc,
            this.columnAntenna,
            this.columnRssi,
            this.columnPhase,
            this.columnReadCount,
            this.columnFirstSeen,
            this.columnLastSeen});
            this.gridTags.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridTags.Location = new System.Drawing.Point(3, 153);
            this.gridTags.MultiSelect = false;
            this.gridTags.Name = "gridTags";
            this.gridTags.ReadOnly = true;
            this.gridTags.RowHeadersVisible = false;
            this.gridTags.RowTemplate.Height = 25;
            this.gridTags.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridTags.Size = new System.Drawing.Size(1094, 344);
            this.gridTags.TabIndex = 1;
            // 
            // columnEpc
            // 
            this.columnEpc.HeaderText = "EPC";
            this.columnEpc.Name = "columnEpc";
            this.columnEpc.ReadOnly = true;
            // 
            // columnAntenna
            // 
            this.columnAntenna.HeaderText = "天线";
            this.columnAntenna.Name = "columnAntenna";
            this.columnAntenna.ReadOnly = true;
            // 
            // columnRssi
            // 
            this.columnRssi.HeaderText = "RSSI (dBm)";
            this.columnRssi.Name = "columnRssi";
            this.columnRssi.ReadOnly = true;
            // 
            // columnPhase
            // 
            this.columnPhase.HeaderText = "相位 (°)";
            this.columnPhase.Name = "columnPhase";
            this.columnPhase.ReadOnly = true;
            // 
            // columnReadCount
            // 
            this.columnReadCount.HeaderText = "读取次数";
            this.columnReadCount.Name = "columnReadCount";
            this.columnReadCount.ReadOnly = true;
            // 
            // columnFirstSeen
            // 
            this.columnFirstSeen.HeaderText = "首次读取时间";
            this.columnFirstSeen.Name = "columnFirstSeen";
            this.columnFirstSeen.ReadOnly = true;
            // 
            // columnLastSeen
            // 
            this.columnLastSeen.HeaderText = "最后读取时间";
            this.columnLastSeen.Name = "columnLastSeen";
            this.columnLastSeen.ReadOnly = true;
            // 
            // tabBottom
            // 
            this.tabBottom.Controls.Add(this.tabLog);
            this.tabBottom.Controls.Add(this.tabStatistics);
            this.tabBottom.Controls.Add(this.tabChart);
            this.tabBottom.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabBottom.Location = new System.Drawing.Point(0, 0);
            this.tabBottom.Name = "tabBottom";
            this.tabBottom.SelectedIndex = 0;
            this.tabBottom.Size = new System.Drawing.Size(1100, 216);
            this.tabBottom.TabIndex = 0;
            // 
            // tabLog
            // 
            this.tabLog.Controls.Add(this.textLog);
            this.tabLog.Location = new System.Drawing.Point(4, 26);
            this.tabLog.Name = "tabLog";
            this.tabLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabLog.Size = new System.Drawing.Size(1092, 186);
            this.tabLog.TabIndex = 0;
            this.tabLog.Text = "运行日志";
            this.tabLog.UseVisualStyleBackColor = true;
            // 
            // textLog
            // 
            this.textLog.BackColor = System.Drawing.Color.White;
            this.textLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textLog.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.textLog.Location = new System.Drawing.Point(3, 3);
            this.textLog.Multiline = true;
            this.textLog.Name = "textLog";
            this.textLog.ReadOnly = true;
            this.textLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textLog.Size = new System.Drawing.Size(1086, 180);
            this.textLog.TabIndex = 0;
            this.textLog.Text = "系统尚未开始运行...";
            // 
            // tabStatistics
            // 
            this.tabStatistics.Controls.Add(this.tableStats);
            this.tabStatistics.Location = new System.Drawing.Point(4, 26);
            this.tabStatistics.Name = "tabStatistics";
            this.tabStatistics.Padding = new System.Windows.Forms.Padding(3);
            this.tabStatistics.Size = new System.Drawing.Size(1092, 186);
            this.tabStatistics.TabIndex = 1;
            this.tabStatistics.Text = "统计信息";
            this.tabStatistics.UseVisualStyleBackColor = true;
            // 
            // tableStats
            // 
            this.tableStats.ColumnCount = 1;
            this.tableStats.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableStats.Controls.Add(this.listStatistics, 0, 0);
            this.tableStats.Controls.Add(this.groupEpcSelection, 0, 1);
            this.tableStats.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableStats.Location = new System.Drawing.Point(3, 3);
            this.tableStats.Name = "tableStats";
            this.tableStats.RowCount = 2;
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.tableStats.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableStats.Size = new System.Drawing.Size(1086, 180);
            this.tableStats.TabIndex = 1;
            // 
            // listStatistics
            // 
            this.listStatistics.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnStatName,
            this.columnStatValue});
            this.listStatistics.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listStatistics.FullRowSelect = true;
            this.listStatistics.GridLines = true;
            this.listStatistics.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.listStatistics.HideSelection = false;
            this.listStatistics.Location = new System.Drawing.Point(3, 3);
            this.listStatistics.MultiSelect = false;
            this.listStatistics.Name = "listStatistics";
            this.listStatistics.Size = new System.Drawing.Size(1080, 104);
            this.listStatistics.TabIndex = 0;
            this.listStatistics.UseCompatibleStateImageBehavior = false;
            this.listStatistics.View = System.Windows.Forms.View.Details;
            // 
            // groupEpcSelection
            // 
            this.groupEpcSelection.Controls.Add(this.checkedListEpcSelection);
            this.groupEpcSelection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupEpcSelection.Location = new System.Drawing.Point(3, 113);
            this.groupEpcSelection.Name = "groupEpcSelection";
            this.groupEpcSelection.Size = new System.Drawing.Size(1080, 64);
            this.groupEpcSelection.TabIndex = 1;
            this.groupEpcSelection.TabStop = false;
            this.groupEpcSelection.Text = "EPC 筛选（用于绘图）";
            // 
            // checkedListEpcSelection
            // 
            this.checkedListEpcSelection.CheckOnClick = true;
            this.checkedListEpcSelection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.checkedListEpcSelection.HorizontalScrollbar = true;
            this.checkedListEpcSelection.FormattingEnabled = true;
            this.checkedListEpcSelection.IntegralHeight = false;
            this.checkedListEpcSelection.Location = new System.Drawing.Point(3, 19);
            this.checkedListEpcSelection.Name = "checkedListEpcSelection";
            this.checkedListEpcSelection.Size = new System.Drawing.Size(1074, 42);
            this.checkedListEpcSelection.TabIndex = 0;
            // 
            // columnStatName
            // 
            this.columnStatName.Text = "指标";
            this.columnStatName.Width = 280;
            // 
            // columnStatValue
            // 
            this.columnStatValue.Text = "当前值";
            this.columnStatValue.Width = 280;
            // 
            // tabChart
            // 
            this.tabChart.Controls.Add(this.formsPlotRssi);
            this.tabChart.Location = new System.Drawing.Point(4, 26);
            this.tabChart.Name = "tabChart";
            this.tabChart.Padding = new System.Windows.Forms.Padding(3);
            this.tabChart.Size = new System.Drawing.Size(1092, 186);
            this.tabChart.TabIndex = 2;
            this.tabChart.Text = "信号曲线";
            this.tabChart.UseVisualStyleBackColor = true;
            // 
            // formsPlotRssi
            // 
            this.formsPlotRssi.Dock = System.Windows.Forms.DockStyle.Fill;
            this.formsPlotRssi.Location = new System.Drawing.Point(3, 3);
            this.formsPlotRssi.Margin = new System.Windows.Forms.Padding(0);
            this.formsPlotRssi.Name = "formsPlotRssi";
            this.formsPlotRssi.Size = new System.Drawing.Size(1086, 180);
            this.formsPlotRssi.TabIndex = 0;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 720);
            this.Controls.Add(this.splitMain);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.MinimumSize = new System.Drawing.Size(1120, 760);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Impinj R700 管理控制台";
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            this.splitMain.ResumeLayout(false);
            this.tableTop.ResumeLayout(false);
            this.tableHeader.ResumeLayout(false);
            this.groupConnection.ResumeLayout(false);
            this.groupConnection.PerformLayout();
            this.groupControl.ResumeLayout(false);
            this.groupControl.PerformLayout();
            this.groupExport.ResumeLayout(false);
            this.groupExport.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridTags)).EndInit();
            this.tabBottom.ResumeLayout(false);
            this.tabLog.ResumeLayout(false);
            this.tabLog.PerformLayout();
            this.tabStatistics.ResumeLayout(false);
            this.tableStats.ResumeLayout(false);
            this.groupEpcSelection.ResumeLayout(false);
            this.groupEpcSelection.PerformLayout();
            this.tabChart.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.TableLayoutPanel tableTop;
        private System.Windows.Forms.TableLayoutPanel tableHeader;
        private System.Windows.Forms.GroupBox groupConnection;
        private System.Windows.Forms.Label labelStatusValue;
        private System.Windows.Forms.Label labelStatusCaption;
        private System.Windows.Forms.Button buttonDisconnect;
        private System.Windows.Forms.Button buttonReaderInfo;
        private System.Windows.Forms.Button buttonConnect;
        private System.Windows.Forms.TextBox textReaderIp;
        private System.Windows.Forms.Label labelReaderIp;
        private System.Windows.Forms.GroupBox groupControl;
        private System.Windows.Forms.CheckBox checkPlotSelectionOnly;
        private System.Windows.Forms.Button buttonAntennaConfig;
        private System.Windows.Forms.CheckedListBox checkedListAntennas;
        private System.Windows.Forms.Label labelAntennaSelection;
        private System.Windows.Forms.CheckBox checkAutoReconnect;
        private System.Windows.Forms.Button buttonStop;
        private System.Windows.Forms.Button buttonStart;
        private System.Windows.Forms.GroupBox groupExport;
        private System.Windows.Forms.Button buttonClear;
        private System.Windows.Forms.Button buttonExportExcel;
        private System.Windows.Forms.Button buttonExportCsv;
        private System.Windows.Forms.Label labelRecordCountValue;
        private System.Windows.Forms.Label labelRecordCountCaption;
        private System.Windows.Forms.DataGridView gridTags;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnEpc;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnAntenna;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnRssi;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnPhase;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnReadCount;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnFirstSeen;
        private System.Windows.Forms.DataGridViewTextBoxColumn columnLastSeen;
        private System.Windows.Forms.TabControl tabBottom;
        private System.Windows.Forms.TabPage tabLog;
        private System.Windows.Forms.TextBox textLog;
        private System.Windows.Forms.TabPage tabStatistics;
        private System.Windows.Forms.TableLayoutPanel tableStats;
        private System.Windows.Forms.ListView listStatistics;
        private System.Windows.Forms.ColumnHeader columnStatName;
        private System.Windows.Forms.ColumnHeader columnStatValue;
        private System.Windows.Forms.GroupBox groupEpcSelection;
        private System.Windows.Forms.CheckedListBox checkedListEpcSelection;
        private System.Windows.Forms.TabPage tabChart;
        private ScottPlot.WinForms.FormsPlot formsPlotRssi;
    }
}
