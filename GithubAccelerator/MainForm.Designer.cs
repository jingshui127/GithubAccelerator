using MaterialSkin;
using MaterialSkin.Controls;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

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
        tabPageHosts = new TabPage();
        lblHostsTitle = new MaterialLabel();
        txtHostsPreview = new TextBox();
        lblUpdateTimeTitle = new MaterialLabel();
        lblUpdateTime = new MaterialLabel();
        tabPageHome = new TabPage();
        chartLatency = new Chart();
        tabPageHome.Controls.Add(chartLatency);
        txtTestResults = new TextBox();
        cardStatus = new MaterialCard();
        lblStatusTitle = new MaterialLabel();
        picStatus = new PictureBox();
        lblStatus = new MaterialLabel();
        cardQuickActions = new MaterialCard();
        lblQuickActions = new MaterialLabel();
        lblLatency = new MaterialLabel();
        materialButtonApply = new MaterialButton();
        materialButtonRestore = new MaterialButton();
        materialButtonRefresh = new MaterialButton();
        materialButtonTestConnection = new MaterialButton();
        cardProxy = new MaterialCard();
        lblProxyTitle = new MaterialLabel();
        materialButtonToggleProxy = new MaterialButton();
        materialButtonProxyGit = new MaterialButton();
        lblProxyInfo = new MaterialLabel();
        materialTabControl = new TabControl();
        lblVersion = new MaterialLabel();
        materialCard1 = new MaterialCard();
        lblSettingsTitle = new MaterialLabel();
        lblDarkMode = new MaterialLabel();
        materialSwitchDarkMode = new MaterialSwitch();
        lblStartup = new MaterialLabel();
        materialSwitchStartup = new MaterialSwitch();
        trayContextMenu.SuspendLayout();
        tabPageHosts.SuspendLayout();
        tabPageHome.SuspendLayout();
        cardStatus.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)picStatus).BeginInit();
        cardQuickActions.SuspendLayout();
        cardProxy.SuspendLayout();
        materialTabControl.SuspendLayout();
        materialCard1.SuspendLayout();
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
        // tabPageHosts
        // 
        tabPageHosts.BackColor = Color.FromArgb(250, 250, 250);
        tabPageHosts.Controls.Add(lblHostsTitle);
        tabPageHosts.Controls.Add(txtHostsPreview);
        tabPageHosts.Controls.Add(lblUpdateTimeTitle);
        tabPageHosts.Controls.Add(lblUpdateTime);
        tabPageHosts.Location = new Point(4, 26);
        tabPageHosts.Name = "tabPageHosts";
        tabPageHosts.Size = new Size(1082, 737);
        tabPageHosts.TabIndex = 1;
        tabPageHosts.Text = "Hosts 管理";
        // 
        // lblHostsTitle
        // 
        lblHostsTitle.AutoSize = true;
        lblHostsTitle.Depth = 0;
        lblHostsTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblHostsTitle.Location = new Point(20, 20);
        lblHostsTitle.MouseState = MouseState.HOVER;
        lblHostsTitle.Name = "lblHostsTitle";
        lblHostsTitle.Size = new Size(110, 19);
        lblHostsTitle.TabIndex = 0;
        lblHostsTitle.Text = "Hosts 文件预览";
        // 
        // txtHostsPreview
        // 
        txtHostsPreview.BackColor = Color.White;
        txtHostsPreview.Font = new Font("Consolas", 9F);
        txtHostsPreview.Location = new Point(20, 60);
        txtHostsPreview.Multiline = true;
        txtHostsPreview.Name = "txtHostsPreview";
        txtHostsPreview.ReadOnly = true;
        txtHostsPreview.ScrollBars = ScrollBars.Vertical;
        txtHostsPreview.Size = new Size(960, 654);
        txtHostsPreview.TabIndex = 1;
        // 
        // lblUpdateTimeTitle
        // 
        lblUpdateTimeTitle.AutoSize = true;
        lblUpdateTimeTitle.Depth = 0;
        lblUpdateTimeTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblUpdateTimeTitle.Location = new Point(622, 20);
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
        lblUpdateTime.Location = new Point(697, 20);
        lblUpdateTime.MouseState = MouseState.HOVER;
        lblUpdateTime.Name = "lblUpdateTime";
        lblUpdateTime.Size = new Size(65, 19);
        lblUpdateTime.TabIndex = 3;
        lblUpdateTime.Text = "从未更新";
        // 
        // tabPageHome
        // 
        tabPageHome.BackColor = Color.FromArgb(250, 250, 250);
        tabPageHome.Controls.Add(materialCard1);
        tabPageHome.Controls.Add(lblVersion);
        tabPageHome.Controls.Add(txtTestResults);
        tabPageHome.Controls.Add(cardStatus);
        tabPageHome.Controls.Add(cardQuickActions);
        tabPageHome.Controls.Add(cardProxy);
        tabPageHome.Location = new Point(4, 26);
        tabPageHome.Name = "tabPageHome";
        tabPageHome.Size = new Size(1082, 737);
        tabPageHome.TabIndex = 0;
        tabPageHome.Text = "首页";
        // 
        // txtTestResults
        // 
        txtTestResults.BackColor = Color.White;
        txtTestResults.Font = new Font("Consolas", 9F);
        txtTestResults.Location = new Point(3, 565);
        txtTestResults.Multiline = true;
        txtTestResults.Name = "txtTestResults";
        txtTestResults.ReadOnly = true;
        txtTestResults.ScrollBars = ScrollBars.Vertical;
        txtTestResults.Size = new Size(960, 110);
        txtTestResults.TabIndex = 3;
        // 
        // cardStatus
        // 
        cardStatus.BackColor = Color.FromArgb(255, 255, 255);
        cardStatus.Controls.Add(lblStatusTitle);
        cardStatus.Controls.Add(picStatus);
        cardStatus.Controls.Add(lblStatus);
        cardStatus.Depth = 0;
        cardStatus.ForeColor = Color.FromArgb(222, 0, 0, 0);
        cardStatus.Location = new Point(20, 20);
        cardStatus.Margin = new Padding(14);
        cardStatus.MouseState = MouseState.HOVER;
        cardStatus.Name = "cardStatus";
        cardStatus.Padding = new Padding(14);
        cardStatus.Size = new Size(194, 150);
        cardStatus.TabIndex = 0;
        // 
        // lblStatusTitle
        // 
        lblStatusTitle.AutoSize = true;
        lblStatusTitle.Depth = 0;
        lblStatusTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblStatusTitle.Location = new Point(20, 20);
        lblStatusTitle.MouseState = MouseState.HOVER;
        lblStatusTitle.Name = "lblStatusTitle";
        lblStatusTitle.Size = new Size(65, 19);
        lblStatusTitle.TabIndex = 0;
        lblStatusTitle.Text = "加速状态";
        // 
        // picStatus
        // 
        picStatus.BackColor = Color.Gray;
        picStatus.Location = new Point(20, 60);
        picStatus.Name = "picStatus";
        picStatus.Size = new Size(40, 40);
        picStatus.SizeMode = PictureBoxSizeMode.StretchImage;
        picStatus.TabIndex = 1;
        picStatus.TabStop = false;
        // 
        // lblStatus
        // 
        lblStatus.AutoSize = true;
        lblStatus.Depth = 0;
        lblStatus.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblStatus.Location = new Point(70, 70);
        lblStatus.MouseState = MouseState.HOVER;
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(77, 19);
        lblStatus.TabIndex = 2;
        lblStatus.Text = "正在检查...";
        // 
        // cardQuickActions
        // 
        cardQuickActions.BackColor = Color.FromArgb(255, 255, 255);
        cardQuickActions.Controls.Add(lblQuickActions);
        cardQuickActions.Controls.Add(lblLatency);
        cardQuickActions.Controls.Add(materialButtonApply);
        cardQuickActions.Controls.Add(materialButtonRestore);
        cardQuickActions.Controls.Add(materialButtonRefresh);
        cardQuickActions.Controls.Add(materialButtonTestConnection);
        cardQuickActions.Depth = 0;
        cardQuickActions.ForeColor = Color.FromArgb(222, 0, 0, 0);
        cardQuickActions.Location = new Point(233, 20);
        cardQuickActions.Margin = new Padding(14);
        cardQuickActions.MouseState = MouseState.HOVER;
        cardQuickActions.Name = "cardQuickActions";
        cardQuickActions.Padding = new Padding(14);
        cardQuickActions.Size = new Size(301, 150);
        cardQuickActions.TabIndex = 1;
        // 
        // lblQuickActions
        // 
        lblQuickActions.AutoSize = true;
        lblQuickActions.Depth = 0;
        lblQuickActions.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblQuickActions.Location = new Point(20, 20);
        lblQuickActions.MouseState = MouseState.HOVER;
        lblQuickActions.Name = "lblQuickActions";
        lblQuickActions.Size = new Size(65, 19);
        lblQuickActions.TabIndex = 0;
        lblQuickActions.Text = "快捷操作";
        // 
        // lblLatency
        // 
        lblLatency.AutoSize = true;
        lblLatency.Depth = 0;
        lblLatency.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblLatency.Location = new Point(160, 20);
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
        materialButtonApply.Location = new Point(20, 60);
        materialButtonApply.Margin = new Padding(4, 6, 4, 6);
        materialButtonApply.MouseState = MouseState.HOVER;
        materialButtonApply.Name = "materialButtonApply";
        materialButtonApply.NoAccentTextColor = Color.Empty;
        materialButtonApply.Size = new Size(120, 36);
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
        materialButtonRestore.Location = new Point(160, 60);
        materialButtonRestore.Margin = new Padding(4, 6, 4, 6);
        materialButtonRestore.MouseState = MouseState.HOVER;
        materialButtonRestore.Name = "materialButtonRestore";
        materialButtonRestore.NoAccentTextColor = Color.Empty;
        materialButtonRestore.Size = new Size(120, 36);
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
        materialButtonRefresh.Location = new Point(20, 105);
        materialButtonRefresh.Margin = new Padding(4, 6, 4, 6);
        materialButtonRefresh.MouseState = MouseState.HOVER;
        materialButtonRefresh.Name = "materialButtonRefresh";
        materialButtonRefresh.NoAccentTextColor = Color.Empty;
        materialButtonRefresh.Size = new Size(120, 36);
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
        materialButtonTestConnection.Location = new Point(160, 105);
        materialButtonTestConnection.Margin = new Padding(4, 6, 4, 6);
        materialButtonTestConnection.MouseState = MouseState.HOVER;
        materialButtonTestConnection.Name = "materialButtonTestConnection";
        materialButtonTestConnection.NoAccentTextColor = Color.Empty;
        materialButtonTestConnection.Size = new Size(120, 36);
        materialButtonTestConnection.TabIndex = 3;
        materialButtonTestConnection.Text = "测试连接";
        materialButtonTestConnection.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonTestConnection.UseAccentColor = false;
        materialButtonTestConnection.UseVisualStyleBackColor = true;
        materialButtonTestConnection.Click += materialButtonTestConnection_Click;
        // 
        // cardProxy
        // 
        cardProxy.BackColor = Color.FromArgb(255, 255, 255);
        cardProxy.Controls.Add(lblProxyTitle);
        cardProxy.Controls.Add(materialButtonToggleProxy);
        cardProxy.Controls.Add(materialButtonProxyGit);
        cardProxy.Controls.Add(lblProxyInfo);
        cardProxy.Depth = 0;
        cardProxy.ForeColor = Color.FromArgb(222, 0, 0, 0);
        cardProxy.Location = new Point(555, 20);
        cardProxy.Margin = new Padding(14);
        cardProxy.MouseState = MouseState.HOVER;
        cardProxy.Name = "cardProxy";
        cardProxy.Padding = new Padding(14);
        cardProxy.Size = new Size(300, 150);
        cardProxy.TabIndex = 2;
        // 
        // lblProxyTitle
        // 
        lblProxyTitle.AutoSize = true;
        lblProxyTitle.Depth = 0;
        lblProxyTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblProxyTitle.Location = new Point(20, 20);
        lblProxyTitle.MouseState = MouseState.HOVER;
        lblProxyTitle.Name = "lblProxyTitle";
        lblProxyTitle.Size = new Size(65, 19);
        lblProxyTitle.TabIndex = 0;
        lblProxyTitle.Text = "代理服务";
        // 
        // materialButtonToggleProxy
        // 
        materialButtonToggleProxy.AutoSize = false;
        materialButtonToggleProxy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonToggleProxy.BackColor = Color.FromArgb(0, 123, 255);
        materialButtonToggleProxy.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonToggleProxy.Depth = 0;
        materialButtonToggleProxy.HighEmphasis = true;
        materialButtonToggleProxy.Icon = null;
        materialButtonToggleProxy.Location = new Point(20, 60);
        materialButtonToggleProxy.Margin = new Padding(4, 6, 4, 6);
        materialButtonToggleProxy.MouseState = MouseState.HOVER;
        materialButtonToggleProxy.Name = "materialButtonToggleProxy";
        materialButtonToggleProxy.NoAccentTextColor = Color.Empty;
        materialButtonToggleProxy.Size = new Size(120, 36);
        materialButtonToggleProxy.TabIndex = 0;
        materialButtonToggleProxy.Text = "启动代理";
        materialButtonToggleProxy.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonToggleProxy.UseAccentColor = false;
        materialButtonToggleProxy.UseVisualStyleBackColor = false;
        materialButtonToggleProxy.Click += materialButtonToggleProxy_Click;
        // 
        // materialButtonProxyGit
        // 
        materialButtonProxyGit.AutoSize = false;
        materialButtonProxyGit.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        materialButtonProxyGit.Density = MaterialButton.MaterialButtonDensity.Default;
        materialButtonProxyGit.Depth = 0;
        materialButtonProxyGit.HighEmphasis = true;
        materialButtonProxyGit.Icon = null;
        materialButtonProxyGit.Location = new Point(160, 60);
        materialButtonProxyGit.Margin = new Padding(4, 6, 4, 6);
        materialButtonProxyGit.MouseState = MouseState.HOVER;
        materialButtonProxyGit.Name = "materialButtonProxyGit";
        materialButtonProxyGit.NoAccentTextColor = Color.Empty;
        materialButtonProxyGit.Size = new Size(120, 36);
        materialButtonProxyGit.TabIndex = 1;
        materialButtonProxyGit.Text = "配置 Git";
        materialButtonProxyGit.Type = MaterialButton.MaterialButtonType.Contained;
        materialButtonProxyGit.UseAccentColor = false;
        materialButtonProxyGit.UseVisualStyleBackColor = true;
        materialButtonProxyGit.Click += materialButtonProxyGit_Click;
        // 
        // lblProxyInfo
        // 
        lblProxyInfo.AutoSize = true;
        lblProxyInfo.Depth = 0;
        lblProxyInfo.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblProxyInfo.Location = new Point(20, 110);
        lblProxyInfo.MouseState = MouseState.HOVER;
        lblProxyInfo.Name = "lblProxyInfo";
        lblProxyInfo.Size = new Size(179, 19);
        lblProxyInfo.TabIndex = 2;
        lblProxyInfo.Text = "代理地址: 127.0.0.1:7890";
        // 
        // materialTabControl
        // 
        materialTabControl.Controls.Add(tabPageHome);
        materialTabControl.Controls.Add(tabPageHosts);
        materialTabControl.Dock = DockStyle.Fill;
        materialTabControl.Location = new Point(3, 64);
        materialTabControl.Name = "materialTabControl";
        materialTabControl.SelectedIndex = 0;
        materialTabControl.Size = new Size(1090, 767);
        materialTabControl.TabIndex = 0;
        materialTabControl.SelectedIndexChanged += materialTabControl_SelectedIndexChanged;
        // 
        // lblVersion
        // 
        lblVersion.AutoSize = true;
        lblVersion.Depth = 0;
        lblVersion.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblVersion.Location = new Point(729, 709);
        lblVersion.MouseState = MouseState.HOVER;
        lblVersion.Name = "lblVersion";
        lblVersion.Size = new Size(336, 19);
        lblVersion.TabIndex = 4;
        lblVersion.Text = "v1.0.0 | 科控物联提供技术支持.QQ:2492123056";
        // 
        // materialCard1
        // 
        materialCard1.BackColor = Color.FromArgb(255, 255, 255);
        materialCard1.Controls.Add(lblSettingsTitle);
        materialCard1.Controls.Add(lblDarkMode);
        materialCard1.Controls.Add(materialSwitchDarkMode);
        materialCard1.Controls.Add(lblStartup);
        materialCard1.Controls.Add(materialSwitchStartup);
        materialCard1.Depth = 0;
        materialCard1.ForeColor = Color.FromArgb(222, 0, 0, 0);
        materialCard1.Location = new Point(880, 20);
        materialCard1.Margin = new Padding(14);
        materialCard1.MouseState = MouseState.HOVER;
        materialCard1.Name = "materialCard1";
        materialCard1.Padding = new Padding(14);
        materialCard1.Size = new Size(182, 150);
        materialCard1.TabIndex = 3;
        // 
        // lblSettingsTitle
        // 
        lblSettingsTitle.AutoSize = true;
        lblSettingsTitle.Depth = 0;
        lblSettingsTitle.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblSettingsTitle.Location = new Point(12, 12);
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
        lblDarkMode.Location = new Point(12, 62);
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
        materialSwitchDarkMode.Location = new Point(112, 62);
        materialSwitchDarkMode.Margin = new Padding(0);
        materialSwitchDarkMode.MouseLocation = new Point(-1, -1);
        materialSwitchDarkMode.MouseState = MouseState.HOVER;
        materialSwitchDarkMode.Name = "materialSwitchDarkMode";
        materialSwitchDarkMode.Ripple = true;
        materialSwitchDarkMode.Size = new Size(58, 37);
        materialSwitchDarkMode.TabIndex = 4;
        materialSwitchDarkMode.UseVisualStyleBackColor = true;
        // 
        // lblStartup
        // 
        lblStartup.AutoSize = true;
        lblStartup.Depth = 0;
        lblStartup.Font = new Font("Roboto", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
        lblStartup.Location = new Point(12, 102);
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
        materialSwitchStartup.Location = new Point(112, 102);
        materialSwitchStartup.Margin = new Padding(0);
        materialSwitchStartup.MouseLocation = new Point(-1, -1);
        materialSwitchStartup.MouseState = MouseState.HOVER;
        materialSwitchStartup.Name = "materialSwitchStartup";
        materialSwitchStartup.Ripple = true;
        materialSwitchStartup.Size = new Size(58, 37);
        materialSwitchStartup.TabIndex = 6;
        materialSwitchStartup.UseVisualStyleBackColor = true;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1096, 834);
        Controls.Add(materialTabControl);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "GitHub 加速器 v1.0.0";
        FormClosing += MainForm_FormClosing;
        trayContextMenu.ResumeLayout(false);
        tabPageHosts.ResumeLayout(false);
        tabPageHosts.PerformLayout();
        tabPageHome.ResumeLayout(false);
        tabPageHome.PerformLayout();
        cardStatus.ResumeLayout(false);
        cardStatus.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)picStatus).EndInit();
        cardQuickActions.ResumeLayout(false);
        cardQuickActions.PerformLayout();
        cardProxy.ResumeLayout(false);
        cardProxy.PerformLayout();
        materialTabControl.ResumeLayout(false);
        materialCard1.ResumeLayout(false);
        materialCard1.PerformLayout();
        ResumeLayout(false);
    }

    #endregion
    private Chart chartLatency;
    private NotifyIcon notifyIcon;
    private ContextMenuStrip trayContextMenu;
    private ToolStripMenuItem trayMenuShow;
    private ToolStripMenuItem trayMenuExit;
    private TabPage tabPageHosts;
    private MaterialLabel lblHostsTitle;
    private TextBox txtHostsPreview;
    private MaterialLabel lblUpdateTimeTitle;
    private MaterialLabel lblUpdateTime;
    private TabPage tabPageHome;
    private TextBox txtTestResults;
    private MaterialCard cardStatus;
    private MaterialLabel lblStatusTitle;
    private PictureBox picStatus;
    private MaterialLabel lblStatus;
    private MaterialCard cardQuickActions;
    private MaterialLabel lblQuickActions;
    private MaterialLabel lblLatency;
    private MaterialButton materialButtonApply;
    private MaterialButton materialButtonRestore;
    private MaterialButton materialButtonRefresh;
    private MaterialButton materialButtonTestConnection;
    private MaterialCard cardProxy;
    private MaterialLabel lblProxyTitle;
    private MaterialButton materialButtonToggleProxy;
    private MaterialButton materialButtonProxyGit;
    private MaterialLabel lblProxyInfo;
    private TabControl materialTabControl;
    private MaterialLabel lblVersion;
    private MaterialCard materialCard1;
    private MaterialLabel lblSettingsTitle;
    private MaterialLabel lblDarkMode;
    private MaterialSwitch materialSwitchDarkMode;
    private MaterialLabel lblStartup;
    private MaterialSwitch materialSwitchStartup;
}
