using MaterialSkin;
using MaterialSkin.Controls;
using System.Drawing;
using System.Windows.Forms;
using ScottPlot.WinForms;

namespace GithubAccelerator;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        notifyIcon = new NotifyIcon(components);
        trayContextMenu = new ContextMenuStrip(components);
        trayMenuShow = new ToolStripMenuItem();
        trayMenuExit = new ToolStripMenuItem();
        materialTabControl = new TabControl();
        tabPageHome = new TabPage();
        cardStatus = new MaterialCard();
        lblStatusTitle = new MaterialLabel();
        picStatus = new PictureBox();
        lblStatus = new MaterialLabel();
        cardActions = new MaterialCard();
        lblActionsTitle = new MaterialLabel();
        lblLatency = new MaterialLabel();
        materialButtonApply = new MaterialButton();
        materialButtonRestore = new MaterialButton();
        materialButtonRefresh = new MaterialButton();
        materialButtonTestConnection = new MaterialButton();
        cardSettings = new MaterialCard();
        lblSettingsTitle = new MaterialLabel();
        lblDarkMode = new MaterialLabel();
        materialSwitchDarkMode = new MaterialSwitch();
        lblStartup = new MaterialLabel();
        materialSwitchStartup = new MaterialSwitch();
        formsPlotLatency = new FormsPlot();
        txtTestResults = new TextBox();
        lblVersion = new MaterialLabel();
        tabPageHosts = new TabPage();
        lblHostsTitle = new MaterialLabel();
        lblUpdateTimeTitle = new MaterialLabel();
        lblUpdateTime = new MaterialLabel();
        txtHostsPreview = new TextBox();
        materialButtonRefreshHosts = new MaterialButton();
        materialButtonOpenNotepad = new MaterialButton();
        tabPageMonitor = new TabPage();
        lblMonitorTitle = new MaterialLabel();
        lblBestSource = new MaterialLabel();
        materialButtonTestSources = new MaterialButton();
        dgvSources = new DataGridView();
        trayContextMenu.SuspendLayout();
        materialTabControl.SuspendLayout();
        tabPageHome.SuspendLayout();
        cardStatus.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)picStatus).BeginInit();
        cardActions.SuspendLayout();
        cardSettings.SuspendLayout();
        tabPageHosts.SuspendLayout();
        tabPageMonitor.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvSources).BeginInit();
        SuspendLayout();
        // 
        // notifyIcon
        // 
        notifyIcon.ContextMenuStrip = trayContextMenu;
        notifyIcon.Icon = (Icon)resources.GetObject("notifyIcon.Icon");
        notifyIcon.Text = "GitHub 加速器";
        notifyIcon.DoubleClick += notifyIcon_DoubleClick;
        // 
        // trayContextMenu
        // 
        trayContextMenu.Items.AddRange(new ToolStripItem[] { trayMenuShow, trayMenuExit });
        trayContextMenu.Name = "trayContextMenu";
        trayContextMenu.Size = new Size(125, 48);
        // 
        // trayMenuShow
        // 
        trayMenuShow.Name = "trayMenuShow";
        trayMenuShow.Size = new Size(124, 22);
        trayMenuShow.Text = "显示窗口";
        trayMenuShow.Click += trayMenuShow_Click;
        // 
        // trayMenuExit
        // 
        trayMenuExit.Name = "trayMenuExit";
        trayMenuExit.Size = new Size(124, 22);
        trayMenuExit.Text = "退出";
        trayMenuExit.Click += trayMenuExit_Click;
        // 
        // materialTabControl
        // 
        materialTabControl.Controls.Add(tabPageHome);
        materialTabControl.Controls.Add(tabPageHosts);
        materialTabControl.Controls.Add(tabPageMonitor);
        materialTabControl.Dock = DockStyle.Fill;
        materialTabControl.Location = new Point(3, 64);
        materialTabControl.Name = "materialTabControl";
        materialTabControl.SelectedIndex = 0;
        materialTabControl.Size = new Size(981, 550);
        materialTabControl.TabIndex = 0;
        materialTabControl.SelectedIndexChanged += materialTabControl_SelectedIndexChanged;
        // 
        // tabPageHome
        // 
        tabPageHome.BackColor = Color.FromArgb(250, 250, 250);
        tabPageHome.Controls.Add(cardStatus);
        tabPageHome.Controls.Add(cardActions);
        tabPageHome.Controls.Add(cardSettings);
        tabPageHome.Controls.Add(formsPlotLatency);
        tabPageHome.Controls.Add(txtTestResults);
        tabPageHome.Controls.Add(lblVersion);
        tabPageHome.Location = new Point(4, 26);
        tabPageHome.Name = "tabPageHome";
        tabPageHome.Size = new Size(973, 520);
        tabPageHome.TabIndex = 0;
        tabPageHome.Text = "首页";
        // 
        // cardStatus
        // 
        cardStatus.BackColor = Color.FromArgb(255, 255, 255);
        cardStatus.Controls.Add(lblStatusTitle);
        cardStatus.Controls.Add(picStatus);
        cardStatus.Controls.Add(lblStatus);
        cardStatus.Depth = 0;
        cardStatus.ForeColor = Color.FromArgb(222, 0, 0, 0);
        cardStatus.Location = new Point(20, 15);
        cardStatus.Margin = new Padding(14);
        cardStatus.MouseState = MouseState.HOVER;
        cardStatus.Name = "cardStatus";
        cardStatus.Padding = new Padding(14);
        cardStatus.Size = new Size(280, 120);
        cardStatus.TabIndex = 0;
        // 
        // lblStatusTitle
        // 
        lblStatusTitle.AutoSize = true;
        lblStatusTitle.Depth = 0;
        lblStatusTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblStatusTitle.Location = new Point(16, 14);
        lblStatusTitle.MouseState = MouseState.HOVER;
        lblStatusTitle.Name = "lblStatusTitle";
        lblStatusTitle.Size = new Size(65, 19);
        lblStatusTitle.TabIndex = 0;
        lblStatusTitle.Text = "加速状态";
        // 
        // picStatus
        // 
        picStatus.BackColor = Color.Transparent;
        picStatus.Location = new Point(16, 44);
        picStatus.Name = "picStatus";
        picStatus.Size = new Size(48, 48);
        picStatus.TabIndex = 1;
        picStatus.TabStop = false;
        picStatus.Paint += picStatus_Paint;
        // 
        // lblStatus
        // 
        lblStatus.AutoSize = true;
        lblStatus.Depth = 0;
        lblStatus.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblStatus.Location = new Point(74, 58);
        lblStatus.MouseState = MouseState.HOVER;
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(77, 19);
        lblStatus.TabIndex = 2;
        lblStatus.Text = "正在检查...";
        // 
        // cardActions
        // 
        cardActions.BackColor = Color.FromArgb(255, 255, 255);
        cardActions.Controls.Add(lblActionsTitle);
        cardActions.Controls.Add(lblLatency);
        cardActions.Controls.Add(materialButtonApply);
        cardActions.Controls.Add(materialButtonRestore);
        cardActions.Controls.Add(materialButtonRefresh);
        cardActions.Controls.Add(materialButtonTestConnection);
        cardActions.Depth = 0;
        cardActions.ForeColor = Color.FromArgb(222, 0, 0, 0);
        cardActions.Location = new Point(320, 15);
        cardActions.Margin = new Padding(14);
        cardActions.MouseState = MouseState.HOVER;
        cardActions.Name = "cardActions";
        cardActions.Padding = new Padding(14);
        cardActions.Size = new Size(340, 120);
        cardActions.TabIndex = 1;
        // 
        // lblActionsTitle
        // 
        lblActionsTitle.AutoSize = true;
        lblActionsTitle.Depth = 0;
        lblActionsTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblActionsTitle.Location = new Point(16, 14);
        lblActionsTitle.MouseState = MouseState.HOVER;
        lblActionsTitle.Name = "lblActionsTitle";
        lblActionsTitle.Size = new Size(65, 19);
        lblActionsTitle.TabIndex = 0;
        lblActionsTitle.Text = "快捷操作";
        // 
        // lblLatency
        // 
        lblLatency.AutoSize = true;
        lblLatency.Depth = 0;
        lblLatency.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblLatency.Location = new Point(157, 16);
        lblLatency.MouseState = MouseState.HOVER;
        lblLatency.Name = "lblLatency";
        lblLatency.Size = new Size(107, 19);
        lblLatency.TabIndex = 4;
        lblLatency.Text = "平均延迟: -- ms";
        // 
        // materialButtonApply
        // 
        materialButtonApply.AutoSize = false;
        materialButtonApply.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonApply.BackColor = Color.FromArgb(0, 123, 255);
        materialButtonApply.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonApply.Depth = 0;
        materialButtonApply.HighEmphasis = true;
        materialButtonApply.Icon = null;
        materialButtonApply.Location = new Point(16, 48);
        materialButtonApply.Margin = new Padding(4, 6, 4, 6);
        materialButtonApply.MouseState = MouseState.HOVER;
        materialButtonApply.Name = "materialButtonApply";
        materialButtonApply.NoAccentTextColor = Color.Empty;
        materialButtonApply.Size = new Size(100, 32);
        materialButtonApply.TabIndex = 0;
        materialButtonApply.Text = "启用加速";
        materialButtonApply.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonApply.UseAccentColor = false;
        materialButtonApply.UseVisualStyleBackColor = false;
        materialButtonApply.Click += materialButtonApply_Click;
        // 
        // materialButtonRestore
        // 
        materialButtonRestore.AutoSize = false;
        materialButtonRestore.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonRestore.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonRestore.Depth = 0;
        materialButtonRestore.HighEmphasis = true;
        materialButtonRestore.Icon = null;
        materialButtonRestore.Location = new Point(159, 48);
        materialButtonRestore.Margin = new Padding(4, 6, 4, 6);
        materialButtonRestore.MouseState = MouseState.HOVER;
        materialButtonRestore.Name = "materialButtonRestore";
        materialButtonRestore.NoAccentTextColor = Color.Empty;
        materialButtonRestore.Size = new Size(100, 32);
        materialButtonRestore.TabIndex = 1;
        materialButtonRestore.Text = "恢复原状";
        materialButtonRestore.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonRestore.UseAccentColor = false;
        materialButtonRestore.UseVisualStyleBackColor = true;
        materialButtonRestore.Click += materialButtonRestore_Click;
        // 
        // materialButtonRefresh
        // 
        materialButtonRefresh.AutoSize = false;
        materialButtonRefresh.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonRefresh.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonRefresh.Depth = 0;
        materialButtonRefresh.HighEmphasis = true;
        materialButtonRefresh.Icon = null;
        materialButtonRefresh.Location = new Point(16, 86);
        materialButtonRefresh.Margin = new Padding(4, 6, 4, 6);
        materialButtonRefresh.MouseState = MouseState.HOVER;
        materialButtonRefresh.Name = "materialButtonRefresh";
        materialButtonRefresh.NoAccentTextColor = Color.Empty;
        materialButtonRefresh.Size = new Size(100, 32);
        materialButtonRefresh.TabIndex = 2;
        materialButtonRefresh.Text = "刷新 Hosts";
        materialButtonRefresh.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonRefresh.UseAccentColor = false;
        materialButtonRefresh.UseVisualStyleBackColor = true;
        materialButtonRefresh.Click += materialButtonRefresh_Click;
        // 
        // materialButtonTestConnection
        // 
        materialButtonTestConnection.AutoSize = false;
        materialButtonTestConnection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonTestConnection.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonTestConnection.Depth = 0;
        materialButtonTestConnection.HighEmphasis = true;
        materialButtonTestConnection.Icon = null;
        materialButtonTestConnection.Location = new Point(159, 86);
        materialButtonTestConnection.Margin = new Padding(4, 6, 4, 6);
        materialButtonTestConnection.MouseState = MouseState.HOVER;
        materialButtonTestConnection.Name = "materialButtonTestConnection";
        materialButtonTestConnection.NoAccentTextColor = Color.Empty;
        materialButtonTestConnection.Size = new Size(100, 32);
        materialButtonTestConnection.TabIndex = 3;
        materialButtonTestConnection.Text = "测试连接";
        materialButtonTestConnection.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonTestConnection.UseAccentColor = false;
        materialButtonTestConnection.UseVisualStyleBackColor = true;
        materialButtonTestConnection.Click += materialButtonTestConnection_Click;
        // 
        // cardSettings
        // 
        cardSettings.BackColor = Color.FromArgb(255, 255, 255);
        cardSettings.Controls.Add(lblSettingsTitle);
        cardSettings.Controls.Add(lblDarkMode);
        cardSettings.Controls.Add(materialSwitchDarkMode);
        cardSettings.Controls.Add(lblStartup);
        cardSettings.Controls.Add(materialSwitchStartup);
        cardSettings.Depth = 0;
        cardSettings.ForeColor = Color.FromArgb(222, 0, 0, 0);
        cardSettings.Location = new Point(680, 15);
        cardSettings.Margin = new Padding(14);
        cardSettings.MouseState = MouseState.HOVER;
        cardSettings.Name = "cardSettings";
        cardSettings.Padding = new Padding(14);
        cardSettings.Size = new Size(256, 120);
        cardSettings.TabIndex = 2;
        // 
        // lblSettingsTitle
        // 
        lblSettingsTitle.AutoSize = true;
        lblSettingsTitle.Depth = 0;
        lblSettingsTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblSettingsTitle.Location = new Point(16, 14);
        lblSettingsTitle.MouseState = MouseState.HOVER;
        lblSettingsTitle.Name = "lblSettingsTitle";
        lblSettingsTitle.Size = new Size(33, 19);
        lblSettingsTitle.TabIndex = 3;
        lblSettingsTitle.Text = "设置";
        // 
        // lblDarkMode
        // 
        lblDarkMode.AutoSize = true;
        lblDarkMode.Depth = 0;
        lblDarkMode.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblDarkMode.Location = new Point(16, 50);
        lblDarkMode.MouseState = MouseState.HOVER;
        lblDarkMode.Name = "lblDarkMode";
        lblDarkMode.Size = new Size(65, 19);
        lblDarkMode.TabIndex = 5;
        lblDarkMode.Text = "深色模式";
        // 
        // materialSwitchDarkMode
        // 
        materialSwitchDarkMode.AutoSize = true;
        materialSwitchDarkMode.BackColor = Color.FromArgb(0, 123, 255);
        materialSwitchDarkMode.Depth = 0;
        materialSwitchDarkMode.Location = new Point(180, 46);
        materialSwitchDarkMode.Margin = new Padding(0);
        materialSwitchDarkMode.MouseLocation = new Point(-1, -1);
        materialSwitchDarkMode.MouseState = MouseState.HOVER;
        materialSwitchDarkMode.Name = "materialSwitchDarkMode";
        materialSwitchDarkMode.Ripple = true;
        materialSwitchDarkMode.Size = new Size(58, 37);
        materialSwitchDarkMode.TabIndex = 4;
        materialSwitchDarkMode.UseVisualStyleBackColor = true;
        materialSwitchDarkMode.CheckedChanged += materialSwitchDarkMode_CheckedChanged;
        // 
        // lblStartup
        // 
        lblStartup.AutoSize = true;
        lblStartup.Depth = 0;
        lblStartup.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblStartup.Location = new Point(16, 86);
        lblStartup.MouseState = MouseState.HOVER;
        lblStartup.Name = "lblStartup";
        lblStartup.Size = new Size(65, 19);
        lblStartup.TabIndex = 7;
        lblStartup.Text = "开机自启";
        // 
        // materialSwitchStartup
        // 
        materialSwitchStartup.AutoSize = true;
        materialSwitchStartup.BackColor = Color.FromArgb(0, 123, 255);
        materialSwitchStartup.Depth = 0;
        materialSwitchStartup.Location = new Point(180, 82);
        materialSwitchStartup.Margin = new Padding(0);
        materialSwitchStartup.MouseLocation = new Point(-1, -1);
        materialSwitchStartup.MouseState = MouseState.HOVER;
        materialSwitchStartup.Name = "materialSwitchStartup";
        materialSwitchStartup.Ripple = true;
        materialSwitchStartup.Size = new Size(58, 37);
        materialSwitchStartup.TabIndex = 6;
        materialSwitchStartup.UseVisualStyleBackColor = true;
        materialSwitchStartup.CheckedChanged += materialSwitchStartup_CheckedChanged;
        // 
        // formsPlotLatency
        // 
        formsPlotLatency.BackColor = Color.FromArgb(250, 250, 250);
        formsPlotLatency.Location = new Point(20, 148);
        formsPlotLatency.Name = "formsPlotLatency";
        formsPlotLatency.Size = new Size(916, 180);
        formsPlotLatency.TabIndex = 5;
        // 
        // txtTestResults
        // 
        txtTestResults.BackColor = Color.White;
        txtTestResults.Font = new Font("Consolas", 9F);
        txtTestResults.Location = new Point(20, 340);
        txtTestResults.Multiline = true;
        txtTestResults.Name = "txtTestResults";
        txtTestResults.ReadOnly = true;
        txtTestResults.ScrollBars = ScrollBars.Vertical;
        txtTestResults.Size = new Size(916, 148);
        txtTestResults.TabIndex = 6;
        // 
        // lblVersion
        // 
        lblVersion.AutoSize = true;
        lblVersion.Depth = 0;
        lblVersion.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblVersion.ForeColor = Color.Purple;
        lblVersion.Location = new Point(598, 496);
        lblVersion.MouseState = MouseState.HOVER;
        lblVersion.Name = "lblVersion";
        lblVersion.Size = new Size(336, 19);
        lblVersion.TabIndex = 4;
        lblVersion.Text = "v1.0.0 | 科控物联提供技术支持.QQ:2492123056";
        // 
        // tabPageHosts
        // 
        tabPageHosts.BackColor = Color.FromArgb(250, 250, 250);
        tabPageHosts.Controls.Add(lblHostsTitle);
        tabPageHosts.Controls.Add(lblUpdateTimeTitle);
        tabPageHosts.Controls.Add(lblUpdateTime);
        tabPageHosts.Controls.Add(txtHostsPreview);
        tabPageHosts.Controls.Add(materialButtonRefreshHosts);
        tabPageHosts.Controls.Add(materialButtonOpenNotepad);
        tabPageHosts.Location = new Point(4, 26);
        tabPageHosts.Name = "tabPageHosts";
        tabPageHosts.Size = new Size(957, 522);
        tabPageHosts.TabIndex = 1;
        tabPageHosts.Text = "Hosts 管理";
        // 
        // lblHostsTitle
        // 
        lblHostsTitle.AutoSize = true;
        lblHostsTitle.Depth = 0;
        lblHostsTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblHostsTitle.Location = new Point(20, 16);
        lblHostsTitle.MouseState = MouseState.HOVER;
        lblHostsTitle.Name = "lblHostsTitle";
        lblHostsTitle.Size = new Size(110, 19);
        lblHostsTitle.TabIndex = 0;
        lblHostsTitle.Text = "Hosts 文件预览";
        // 
        // lblUpdateTimeTitle
        // 
        lblUpdateTimeTitle.AutoSize = true;
        lblUpdateTimeTitle.Depth = 0;
        lblUpdateTimeTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblUpdateTimeTitle.Location = new Point(157, 16);
        lblUpdateTimeTitle.MouseState = MouseState.HOVER;
        lblUpdateTimeTitle.Name = "lblUpdateTimeTitle";
        lblUpdateTimeTitle.Size = new Size(69, 19);
        lblUpdateTimeTitle.TabIndex = 2;
        lblUpdateTimeTitle.Text = "最后更新:";
        // 
        // lblUpdateTime
        // 
        lblUpdateTime.AutoSize = true;
        lblUpdateTime.Depth = 0;
        lblUpdateTime.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblUpdateTime.Location = new Point(232, 16);
        lblUpdateTime.MouseState = MouseState.HOVER;
        lblUpdateTime.Name = "lblUpdateTime";
        lblUpdateTime.Size = new Size(65, 19);
        lblUpdateTime.TabIndex = 3;
        lblUpdateTime.Text = "从未更新";
        // 
        // txtHostsPreview
        // 
        txtHostsPreview.BackColor = Color.White;
        txtHostsPreview.Font = new Font("Consolas", 9F);
        txtHostsPreview.Location = new Point(20, 48);
        txtHostsPreview.Multiline = true;
        txtHostsPreview.Name = "txtHostsPreview";
        txtHostsPreview.ReadOnly = true;
        txtHostsPreview.ScrollBars = ScrollBars.Vertical;
        txtHostsPreview.Size = new Size(916, 456);
        txtHostsPreview.TabIndex = 1;
        // 
        // materialButtonRefreshHosts
        // 
        materialButtonRefreshHosts.AutoSize = false;
        materialButtonRefreshHosts.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonRefreshHosts.BackColor = Color.FromArgb(0, 123, 255);
        materialButtonRefreshHosts.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonRefreshHosts.Depth = 0;
        materialButtonRefreshHosts.HighEmphasis = true;
        materialButtonRefreshHosts.Icon = null;
        materialButtonRefreshHosts.Location = new Point(700, 12);
        materialButtonRefreshHosts.Margin = new Padding(4, 6, 4, 6);
        materialButtonRefreshHosts.MouseState = MouseState.HOVER;
        materialButtonRefreshHosts.Name = "materialButtonRefreshHosts";
        materialButtonRefreshHosts.NoAccentTextColor = Color.Empty;
        materialButtonRefreshHosts.Size = new Size(100, 32);
        materialButtonRefreshHosts.TabIndex = 4;
        materialButtonRefreshHosts.Text = "刷新预览";
        materialButtonRefreshHosts.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonRefreshHosts.UseAccentColor = false;
        materialButtonRefreshHosts.UseVisualStyleBackColor = true;
        materialButtonRefreshHosts.Click += materialButtonRefreshHosts_Click;
        // 
        // materialButtonOpenNotepad
        // 
        materialButtonOpenNotepad.AutoSize = false;
        materialButtonOpenNotepad.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonOpenNotepad.BackColor = Color.FromArgb(0, 123, 255);
        materialButtonOpenNotepad.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonOpenNotepad.Depth = 0;
        materialButtonOpenNotepad.HighEmphasis = true;
        materialButtonOpenNotepad.Icon = null;
        materialButtonOpenNotepad.Location = new Point(810, 12);
        materialButtonOpenNotepad.Margin = new Padding(4, 6, 4, 6);
        materialButtonOpenNotepad.MouseState = MouseState.HOVER;
        materialButtonOpenNotepad.Name = "materialButtonOpenNotepad";
        materialButtonOpenNotepad.NoAccentTextColor = Color.Empty;
        materialButtonOpenNotepad.Size = new Size(120, 32);
        materialButtonOpenNotepad.TabIndex = 5;
        materialButtonOpenNotepad.Text = "用记事本打开";
        materialButtonOpenNotepad.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonOpenNotepad.UseAccentColor = false;
        materialButtonOpenNotepad.UseVisualStyleBackColor = true;
        materialButtonOpenNotepad.Click += materialButtonOpenNotepad_Click;
        // 
        // tabPageMonitor
        // 
        tabPageMonitor.BackColor = Color.FromArgb(250, 250, 250);
        tabPageMonitor.Controls.Add(lblMonitorTitle);
        tabPageMonitor.Controls.Add(lblBestSource);
        tabPageMonitor.Controls.Add(materialButtonTestSources);
        tabPageMonitor.Controls.Add(dgvSources);
        tabPageMonitor.Location = new Point(4, 26);
        tabPageMonitor.Name = "tabPageMonitor";
        tabPageMonitor.Size = new Size(957, 522);
        tabPageMonitor.TabIndex = 2;
        tabPageMonitor.Text = "数据源监控";
        // 
        // lblMonitorTitle
        // 
        lblMonitorTitle.AutoSize = true;
        lblMonitorTitle.Depth = 0;
        lblMonitorTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblMonitorTitle.Location = new Point(20, 16);
        lblMonitorTitle.MouseState = MouseState.HOVER;
        lblMonitorTitle.Name = "lblMonitorTitle";
        lblMonitorTitle.Size = new Size(113, 19);
        lblMonitorTitle.TabIndex = 0;
        lblMonitorTitle.Text = "数据源性能监控";
        // 
        // lblBestSource
        // 
        lblBestSource.AutoSize = true;
        lblBestSource.Depth = 0;
        lblBestSource.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblBestSource.Location = new Point(160, 16);
        lblBestSource.MouseState = MouseState.HOVER;
        lblBestSource.Name = "lblBestSource";
        lblBestSource.Size = new Size(133, 19);
        lblBestSource.TabIndex = 1;
        lblBestSource.Text = "推荐源: 等待测试...";
        // 
        // materialButtonTestSources
        // 
        materialButtonTestSources.AutoSize = false;
        materialButtonTestSources.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonTestSources.BackColor = Color.FromArgb(0, 123, 255);
        materialButtonTestSources.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonTestSources.Depth = 0;
        materialButtonTestSources.HighEmphasis = true;
        materialButtonTestSources.Icon = null;
        materialButtonTestSources.Location = new Point(810, 10);
        materialButtonTestSources.Margin = new Padding(4, 6, 4, 6);
        materialButtonTestSources.MouseState = MouseState.HOVER;
        materialButtonTestSources.Name = "materialButtonTestSources";
        materialButtonTestSources.NoAccentTextColor = Color.Empty;
        materialButtonTestSources.Size = new Size(120, 32);
        materialButtonTestSources.TabIndex = 2;
        materialButtonTestSources.Text = "测试所有源";
        materialButtonTestSources.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonTestSources.UseAccentColor = false;
        materialButtonTestSources.UseVisualStyleBackColor = true;
        materialButtonTestSources.Click += materialButtonTestSources_Click;
        // 
        // dgvSources
        // 
        dgvSources.AllowUserToAddRows = false;
        dgvSources.AllowUserToDeleteRows = false;
        dgvSources.AllowUserToResizeRows = false;
        dgvSources.BackgroundColor = Color.White;
        dgvSources.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvSources.Location = new Point(20, 50);
        dgvSources.MultiSelect = false;
        dgvSources.Name = "dgvSources";
        dgvSources.ReadOnly = true;
        dgvSources.RowHeadersVisible = false;
        dgvSources.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvSources.Size = new Size(916, 440);
        dgvSources.TabIndex = 3;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(987, 617);
        Controls.Add(materialTabControl);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        Icon = (Icon)resources.GetObject("$this.Icon");
        MaximizeBox = false;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "GitHub 加速器 v1.0.0";
        FormClosing += MainForm_FormClosing;
        trayContextMenu.ResumeLayout(false);
        materialTabControl.ResumeLayout(false);
        tabPageHome.ResumeLayout(false);
        tabPageHome.PerformLayout();
        cardStatus.ResumeLayout(false);
        cardStatus.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)picStatus).EndInit();
        cardActions.ResumeLayout(false);
        cardActions.PerformLayout();
        cardSettings.ResumeLayout(false);
        cardSettings.PerformLayout();
        tabPageHosts.ResumeLayout(false);
        tabPageHosts.PerformLayout();
        tabPageMonitor.ResumeLayout(false);
        tabPageMonitor.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvSources).EndInit();
        ResumeLayout(false);
    }

    #endregion
    private NotifyIcon notifyIcon;
    private ContextMenuStrip trayContextMenu;
    private ToolStripMenuItem trayMenuShow;
    private ToolStripMenuItem trayMenuExit;
    private TabControl materialTabControl;
    private TabPage tabPageHome;
    private TabPage tabPageHosts;
    private MaterialCard cardStatus;
    private MaterialLabel lblStatusTitle;
    private PictureBox picStatus;
    private MaterialLabel lblStatus;
    private MaterialCard cardActions;
    private MaterialLabel lblActionsTitle;
    private MaterialLabel lblLatency;
    private MaterialButton materialButtonApply;
    private MaterialButton materialButtonRestore;
    private MaterialButton materialButtonRefresh;
    private MaterialButton materialButtonTestConnection;
    private MaterialCard cardSettings;
    private MaterialLabel lblSettingsTitle;
    private MaterialLabel lblDarkMode;
    private MaterialSwitch materialSwitchDarkMode;
    private MaterialLabel lblStartup;
    private MaterialSwitch materialSwitchStartup;
    private MaterialLabel lblVersion;
    private FormsPlot formsPlotLatency;
    private TextBox txtTestResults;
    private MaterialLabel lblHostsTitle;
    private MaterialLabel lblUpdateTimeTitle;
    private MaterialLabel lblUpdateTime;
    private TextBox txtHostsPreview;
    private MaterialButton materialButtonRefreshHosts;
    private MaterialButton materialButtonOpenNotepad;
    private TabPage tabPageMonitor;
    private DataGridView dgvSources;
    private MaterialButton materialButtonTestSources;
    private MaterialLabel lblMonitorTitle;
    private MaterialLabel lblBestSource;
}
