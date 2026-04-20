using MaterialSkin;
using MaterialSkin.Controls;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using GithubAccelerator.Services;

namespace GithubAccelerator;

public partial class MainForm : MaterialForm
{
    private readonly GithubHostsService _hostsService = new();
    private readonly IHostsFileService _fileService;
    private readonly IDnsFlusher _dnsFlusher;
    private readonly IStartupManager _startupManager;
    private string _currentHostsContent = string.Empty;
    private bool _isHostsApplied = false;
    private string _lastTestResults = string.Empty;
    private DateTime _lastTestTime = DateTime.MinValue;
    private readonly List<(DateTime time, int latency)> _latencyHistory = new();
    private System.Windows.Forms.Timer _autoTestTimer;
    private bool _isTesting = false;
    private readonly MaterialSkinManager _skinManager;

    public MainForm()
    {
        InitializeComponent();

        InitializeChart();

        _skinManager = MaterialSkinManager.Instance;
        _skinManager.AddFormToManage(this);
        _skinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);

        var platform = PlatformServiceFactory.CreatePlatformService();
        _fileService = PlatformServiceFactory.CreateHostsFileService(platform);
        _dnsFlusher = PlatformServiceFactory.CreateDnsFlusher();
        _startupManager = PlatformServiceFactory.CreateStartupManager();

        materialSwitchStartup.Checked = _startupManager.IsStartupEnabled;
        UpdateThemeSwitch();
        _autoTestTimer = new System.Windows.Forms.Timer();
        _autoTestTimer.Interval = 30000;
        _autoTestTimer.Tick += async (s, e) => await AutoTestLatencyAsync();
        _autoTestTimer.Start();
        _ = TestSingleDomainLatencyAsync();
        _ = InitializeAsync();

        this.StartPosition = FormStartPosition.CenterScreen;
        this.Visible = true;
        this.Show();
        this.Activate();
    }

    private void UpdateThemeSwitch()
    {
        materialSwitchDarkMode.Checked = _skinManager.Theme == MaterialSkinManager.Themes.DARK;
    }

    private void InitializeChart()
    {
        ChartArea chartArea = new ChartArea("LatencyArea");
        chartArea.AxisX.LabelStyle.Format = "HH:mm";
        chartArea.AxisX.IntervalType = DateTimeIntervalType.Minutes;
        chartArea.AxisX.Interval = 10;
        chartArea.AxisY.Title = "延迟 (ms)";
        chartArea.AxisY.Minimum = 0;
        chartLatency.ChartAreas.Add(chartArea);

        Series latencySeries = new Series("延迟");
        latencySeries.ChartType = SeriesChartType.Line;
        latencySeries.Color = Color.FromArgb(0, 123, 255);
        latencySeries.BorderWidth = 2;
        latencySeries.XValueType = ChartValueType.DateTime;
        chartLatency.Series.Add(latencySeries);

        chartLatency.Location = new Point(20, 190);
        chartLatency.Size = new Size(960, 200);
    }

    private async Task InitializeAsync()
    {
        await CheckStatusAsync();
    }

    private async Task CheckStatusAsync()
    {
        try
        {
            lblStatus.Text = "正在检查状态...";
            materialButtonApply.Enabled = false;
            materialButtonRestore.Enabled = false;
            materialButtonRefresh.Enabled = false;

            _currentHostsContent = await _fileService.ReadHostsFileAsync();
            _isHostsApplied = _fileService.IsGithubHostsApplied(_currentHostsContent);

            if (_isHostsApplied)
            {
                lblStatus.Text = "已启用加速";
                picStatus.BackColor = Color.FromArgb(76, 175, 80);
                materialButtonRestore.Enabled = true;
                materialButtonRefresh.Enabled = true;
                _lastTestTime = DateTime.Now;
            }
            else
            {
                lblStatus.Text = "未启用加速";
                picStatus.BackColor = Color.FromArgb(158, 158, 158);
                materialButtonApply.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"检查失败: {ex.Message}";
            picStatus.BackColor = Color.FromArgb(244, 67, 54);
            materialButtonApply.Enabled = true;
        }
    }

    private async void materialButtonApply_Click(object? sender, EventArgs e)
    {
        try
        {
            lblStatus.Text = "正在获取最新 Hosts...";
            materialButtonApply.Enabled = false;

            var hostsContent = await _hostsService.FetchHostsAsync();

            lblStatus.Text = "正在备份并应用...";
            await _fileService.BackupHostsFileAsync();
            await _fileService.ApplyGithubHostsAsync(hostsContent);

            await _dnsFlusher.FlushDnsCacheAsync();

            _currentHostsContent = hostsContent;
            _isHostsApplied = true;
            _lastTestTime = DateTime.Now;

            lblStatus.Text = "加速已启用";
            picStatus.BackColor = Color.FromArgb(76, 175, 80);
            materialButtonRestore.Enabled = true;
            materialButtonRefresh.Enabled = true;
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"启用失败: {ex.Message}";
            picStatus.BackColor = Color.FromArgb(244, 67, 54);
            materialButtonApply.Enabled = true;
        }
    }

    private async void materialButtonRestore_Click(object? sender, EventArgs e)
    {
        try
        {
            lblStatus.Text = "正在恢复...";
            materialButtonRestore.Enabled = false;

            await _fileService.RestoreOriginalHostsAsync();
            await _dnsFlusher.FlushDnsCacheAsync();

            _currentHostsContent = await _fileService.ReadHostsFileAsync();
            _isHostsApplied = false;

            lblStatus.Text = "已恢复原状";
            picStatus.BackColor = Color.FromArgb(158, 158, 158);
            materialButtonApply.Enabled = true;
            materialButtonRefresh.Enabled = false;
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"恢复失败: {ex.Message}";
            picStatus.BackColor = Color.FromArgb(244, 67, 54);
            materialButtonRestore.Enabled = true;
        }
    }

    private void materialButtonRefresh_Click(object? sender, EventArgs e)
    {
        materialButtonApply_Click(sender, e);
    }

    private async void materialButtonTestConnection_Click(object? sender, EventArgs e)
    {
        if (_isTesting) return;
        _isTesting = true;
        try
        {
            txtTestResults.Text = "正在测试 GitHub 各服务延迟...\r\n";
            txtTestResults.AppendText("=====================================\r\n");

            var domains = new[] { "github.com", "api.github.com", "raw.githubusercontent.com", "github.global.ssl.fastly.net" };
            var results = new List<(string domain, long latency)>();

            foreach (var domain in domains)
            {
                try
                {
                    txtTestResults.AppendText($"测试 {domain}... ");
                    var ping = new Ping();
                    var reply = await ping.SendPingAsync(domain, 3000);

                    if (reply.Status == IPStatus.Success && reply.RoundtripTime > 0)
                    {
                        results.Add((domain, reply.RoundtripTime));
                        txtTestResults.AppendText($"{reply.RoundtripTime} ms\r\n");
                    }
                    else
                    {
                        txtTestResults.AppendText("超时\r\n");
                        results.Add((domain, -1));
                    }
                }
                catch
                {
                    txtTestResults.AppendText("失败\r\n");
                    results.Add((domain, -1));
                }
            }

            if (!_isHostsApplied)
            {
                txtTestResults.AppendText("\r\n⚠️ 警告: 加速未启用，测试结果为直连速度\r\n");
            }

            var validResults = results.Where(r => r.latency > 0).ToList();
            if (validResults.Any())
            {
                var avgLatency = (int)validResults.Average(r => r.latency);
                lblLatency.Text = $"平均延迟: {avgLatency} ms";
                txtTestResults.AppendText("=====================================\r\n");
                txtTestResults.AppendText($"测试完成 | 平均延迟: {avgLatency} ms");
            }
            else
            {
                lblLatency.Text = "平均延迟: -- ms";
                txtTestResults.AppendText("=====================================\r\n");
                txtTestResults.AppendText("测试完成 | 所有节点均超时");
            }

            _lastTestResults = txtTestResults.Text;
            _lastTestTime = DateTime.Now;
            UpdateLatencyChart(validResults.Any() ? (int)validResults.Average(r => r.latency) : -1);
        }
        catch (Exception ex)
        {
            txtTestResults.Text = $"测试失败: {ex.Message}";
        }
        finally
        {
            _isTesting = false;
        }
    }

    private void materialTabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (materialTabControl.SelectedIndex == 1)
        {
            txtHostsPreview.Text = _currentHostsContent;
            if (_lastTestTime != DateTime.MinValue)
            {
                lblUpdateTime.Text = _lastTestTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
        else if (materialTabControl.SelectedIndex == 2)
        {
            if (!string.IsNullOrEmpty(_lastTestResults))
            {
                txtTestResults.Text = _lastTestResults;
            }
        }
    }

    private void materialSwitchDarkMode_CheckedChanged(object? sender, EventArgs e)
    {
        _skinManager.Theme = materialSwitchDarkMode.Checked
            ? MaterialSkinManager.Themes.DARK
            : MaterialSkinManager.Themes.LIGHT;
    }

    private void materialSwitchStartup_CheckedChanged(object? sender, EventArgs e)
    {
        if (materialSwitchStartup.Checked)
            _startupManager.EnableStartup();
        else
            _startupManager.DisableStartup();
    }

    private void materialButtonToggleProxy_Click(object? sender, EventArgs e)
    {
        if (materialButtonToggleProxy.Text == "启动代理")
        {
            materialButtonToggleProxy.Text = "停止代理";
            lblProxyInfo.Text = "代理状态: 已启动";
        }
        else
        {
            materialButtonToggleProxy.Text = "启动代理";
            lblProxyInfo.Text = "代理状态: 已停止";
        }
    }

    private void materialButtonProxyGit_Click(object? sender, EventArgs e)
    {
        MessageBox.Show("Git 代理配置功能开发中...", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            notifyIcon.Visible = true;
        }
    }

    private void notifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
        notifyIcon.Visible = false;
    }

    private void trayMenuShow_Click(object? sender, EventArgs e)
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
        notifyIcon.Visible = false;
    }

    private void trayMenuExit_Click(object? sender, EventArgs e)
    {
        _autoTestTimer?.Stop();
        _autoTestTimer?.Dispose();
        notifyIcon.Visible = false;
        Application.Exit();
    }

    private async Task AutoTestLatencyAsync()
    {
        if (_isTesting) return;
        _isTesting = true;
        try
        {
            await TestSingleDomainLatencyAsync();
        }
        finally
        {
            _isTesting = false;
        }
    }

    private async Task TestSingleDomainLatencyAsync()
    {
        try
        {
            var ping = new Ping();
            var reply = await ping.SendPingAsync("github.com", 3000);
            var latency = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
            UpdateLatencyChart(latency);
        }
        catch
        {
            UpdateLatencyChart(-1);
        }
    }

    private void UpdateLatencyChart(int latency)
    {
        if (chartLatency.Series.Count == 0 || chartLatency.ChartAreas.Count == 0)
            return;

        var now = DateTime.Now;
        _latencyHistory.Add((now, latency));

        var oneHourAgo = now.AddHours(-1);
        _latencyHistory.RemoveAll(x => x.time < oneHourAgo);

        var series = chartLatency.Series["延迟"];
        series.Points.Clear();

        foreach (var point in _latencyHistory)
        {
            series.Points.AddXY(point.time.ToOADate(), point.latency >= 0 ? point.latency : 0);
        }

        chartLatency.ChartAreas[0].RecalculateAxesScale();
    }
}
