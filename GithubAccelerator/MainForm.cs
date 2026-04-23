using MaterialSkin;
using MaterialSkin.Controls;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using ScottPlot;
using GithubAccelerator.Services;

namespace GithubAccelerator;

public partial class MainForm : MaterialForm
{
    private readonly GithubHostsService _hostsService = new();
    private readonly GithubConnectionService _connectionService = new();
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
    private bool _isSourceTesting = false;
    private readonly MaterialSkinManager _skinManager;
    private System.Drawing.Color _statusColor = System.Drawing.Color.Gray;

    private static readonly System.Drawing.Color StatusEnabled = System.Drawing.Color.FromArgb(76, 175, 80);
    private static readonly System.Drawing.Color StatusDisabled = System.Drawing.Color.FromArgb(158, 158, 158);
    private static readonly System.Drawing.Color StatusError = System.Drawing.Color.FromArgb(244, 67, 54);
    private static readonly System.Drawing.Color StatusWarning = System.Drawing.Color.FromArgb(255, 193, 7);

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

        InitializeSourceMonitorGrid();

        this.StartPosition = FormStartPosition.CenterScreen;
        this.Visible = true;
        this.Show();
        this.Activate();
    }
    
    private async Task QuickStartAsync()
    {
        try
        {
            UpdateStatusIndicator(StatusWarning, "正在启动一键加速...");
            
            // 1. 检查当前状态
            var hostsContent = await _fileService.ReadHostsFileAsync();
            var isApplied = _fileService.IsGithubHostsApplied(hostsContent);
            
            if (isApplied)
            {
                UpdateStatusIndicator(StatusWarning, "加速已启用，正在刷新...");
            }
            else
            {
                UpdateStatusIndicator(StatusWarning, "正在获取最新 Hosts...");
            }
            
            // 2. 获取最新 Hosts
            var newHosts = await _hostsService.FetchHostsAsync();
            
            // 3. 应用 Hosts 配置
            UpdateStatusIndicator(StatusWarning, "正在应用 Hosts 配置...");
            await _fileService.BackupHostsFileAsync();
            await _fileService.ApplyGithubHostsAsync(newHosts);
            await _dnsFlusher.FlushDnsCacheAsync();
            
            // 4. 测试连接
            UpdateStatusIndicator(StatusWarning, "正在测试连接...");
            await TestSingleDomainLatencyAsync();
            
            // 5. 完成
            _currentHostsContent = newHosts;
            _isHostsApplied = true;
            _lastTestTime = DateTime.Now;
            
            UpdateStatusIndicator(StatusEnabled, "一键加速完成！");
            materialButtonRestore.Enabled = true;
            materialButtonRefresh.Enabled = true;
            UpdateHostsPreview();
            
            // 显示成功消息
            MessageBox.Show("一键加速完成！\n\n已自动配置：\n- 最新 Hosts 配置\n- DNS 缓存刷新\n- 连接测试", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (UnauthorizedAccessException)
        {
            UpdateStatusIndicator(StatusError, "需要管理员权限！");
            MessageBox.Show("需要管理员权限才能应用 Hosts 配置，请以管理员身份运行此程序。", "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            UpdateStatusIndicator(StatusError, $"加速失败: {ex.Message}");
            MessageBox.Show($"一键加速失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateThemeSwitch()
    {
        materialSwitchDarkMode.Checked = _skinManager.Theme == MaterialSkinManager.Themes.DARK;
    }

    private void InitializeChart()
    {
        formsPlotLatency.Plot.Title("延迟曲线");
        formsPlotLatency.Plot.XLabel("时间");
        formsPlotLatency.Plot.YLabel("延迟 (ms)");
        formsPlotLatency.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");
        formsPlotLatency.Plot.Axes.SetLimitsY(0, 500);
        
        // 自动检测中文字体支持
        formsPlotLatency.Plot.Font.Automatic();
        
        formsPlotLatency.Refresh();
    }

    private void UpdateChartTheme(bool isDark)
    {
        if (isDark)
        {
            formsPlotLatency.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1F1F1F");
            formsPlotLatency.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            formsPlotLatency.Plot.Axes.Color(ScottPlot.Color.FromHex("#D7D7D7"));
            formsPlotLatency.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
        }
        else
        {
            formsPlotLatency.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");
            formsPlotLatency.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
            formsPlotLatency.Plot.Axes.Color(ScottPlot.Color.FromHex("#000000"));
            formsPlotLatency.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#CCCCCC");
        }
        
        formsPlotLatency.Refresh();
    }

    private async Task InitializeAsync()
    {
        await CheckStatusAsync();
    }

    private async Task CheckStatusAsync()
    {
        try
        {
            UpdateStatusIndicator(StatusWarning, "正在检查状态...");
            materialButtonApply.Enabled = false;
            materialButtonRestore.Enabled = false;
            materialButtonRefresh.Enabled = false;

            _currentHostsContent = await _fileService.ReadHostsFileAsync();
            _isHostsApplied = _fileService.IsGithubHostsApplied(_currentHostsContent);

            if (_isHostsApplied)
            {
                UpdateStatusIndicator(StatusEnabled, "已启用加速");
                materialButtonRestore.Enabled = true;
                materialButtonRefresh.Enabled = true;
                _lastTestTime = DateTime.Now;
            }
            else
            {
                UpdateStatusIndicator(StatusDisabled, "未启用加速");
                materialButtonApply.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusIndicator(StatusError, $"检查失败: {ex.Message}");
            materialButtonApply.Enabled = true;
        }
    }

    private async void materialButtonApply_Click(object? sender, EventArgs e)
    {
        try
        {
            UpdateStatusIndicator(StatusWarning, "正在获取最新 Hosts...");
            materialButtonApply.Enabled = false;

            var hostsContent = await _hostsService.FetchHostsAsync();

            UpdateStatusIndicator(StatusWarning, "正在备份并应用...");
            await _fileService.BackupHostsFileAsync();
            await _fileService.ApplyGithubHostsAsync(hostsContent);

            await _dnsFlusher.FlushDnsCacheAsync();

            _currentHostsContent = hostsContent;
            _isHostsApplied = true;
            _lastTestTime = DateTime.Now;

            UpdateStatusIndicator(StatusEnabled, "加速已启用");
            materialButtonRestore.Enabled = true;
            materialButtonRefresh.Enabled = true;
            UpdateHostsPreview();
            
            // 显示成功消息
            MessageBox.Show("加速已成功启用！\n\n已自动配置：\n- 最新 Hosts 配置\n- DNS 缓存刷新\n- 连接测试", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            
            // 自动测试连接
            await TestSingleDomainLatencyAsync();
        }
        catch (UnauthorizedAccessException)
        {
            UpdateStatusIndicator(StatusError, "需要管理员权限！");
            MessageBox.Show("需要管理员权限才能应用 Hosts 配置，请以管理员身份运行此程序。", "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            materialButtonApply.Enabled = true;
        }
        catch (Exception ex)
        {
            UpdateStatusIndicator(StatusError, $"启用失败: {ex.Message}");
            MessageBox.Show($"启用失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            materialButtonApply.Enabled = true;
        }
    }

    private async void materialButtonRestore_Click(object? sender, EventArgs e)
    {
        try
        {
            UpdateStatusIndicator(StatusWarning, "正在恢复...");
            materialButtonRestore.Enabled = false;

            await _fileService.RestoreOriginalHostsAsync();
            await _dnsFlusher.FlushDnsCacheAsync();

            _currentHostsContent = await _fileService.ReadHostsFileAsync();
            _isHostsApplied = false;

            UpdateStatusIndicator(StatusDisabled, "已恢复原状");
            materialButtonApply.Enabled = true;
            materialButtonRefresh.Enabled = false;
        }
        catch (Exception ex)
        {
            UpdateStatusIndicator(StatusError, $"恢复失败: {ex.Message}");
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

            txtTestResults.AppendText("\r\n=== 测试 GitHub 真实访问 (HTTPS) ===\r\n");
            txtTestResults.AppendText("正在测试 TCP 443 端口...\r\n");
            
            try
            {
                var testResults = await _connectionService.TestAllConnectionsAsync();
                var successCount = testResults.Count(r => r.IsSuccess);
                var totalCount = testResults.Count;
                
                if (successCount > 0)
                {
                    var avgHttpsLatency = testResults.Where(r => r.IsSuccess).Average(r => r.LatencyMs);
                    txtTestResults.AppendText($"[green]✓ GitHub 可访问 (成功率: {successCount}/{totalCount}, 平均延迟: {avgHttpsLatency:F0}ms)[/]\r\n");
                }
                else
                {
                    txtTestResults.AppendText($"[red]✗ 无法访问 GitHub！TCP 443 端口可能被封锁[/]\r\n");
                    txtTestResults.AppendText("[yellow]可能原因: 网络运营商封锁了 GitHub 的 HTTPS 端口[/]\r\n");
                    txtTestResults.AppendText("[cyan]解决方案: 使用代理软件/VPN 或等待后重试[/]\r\n");
                }
            }
            catch (Exception ex)
            {
                txtTestResults.AppendText($"测试失败: {ex.Message}\r\n");
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
            UpdateHostsPreview();
        }
    }

    private void UpdateHostsPreview()
    {
        if (!string.IsNullOrEmpty(_currentHostsContent))
        {
            txtHostsPreview.Text = _currentHostsContent.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }
        if (_lastTestTime != DateTime.MinValue)
        {
            lblUpdateTime.Text = _lastTestTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    private void materialSwitchDarkMode_CheckedChanged(object? sender, EventArgs e)
    {
        _skinManager.Theme = materialSwitchDarkMode.Checked
            ? MaterialSkinManager.Themes.DARK
            : MaterialSkinManager.Themes.LIGHT;
        
        // 更新图表主题
        UpdateChartTheme(materialSwitchDarkMode.Checked);
    }

    private void materialSwitchStartup_CheckedChanged(object? sender, EventArgs e)
    {
        if (materialSwitchStartup.Checked)
            _startupManager.EnableStartup();
        else
            _startupManager.DisableStartup();
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
        var now = DateTime.Now;
        _latencyHistory.Add((now, latency));

        var oneHourAgo = now.AddHours(-1);
        _latencyHistory.RemoveAll(x => x.time < oneHourAgo);

        if (latency >= 0)
        {
            lblLatency.Text = $"平均延迟: {latency} ms";
        }
        else
        {
            lblLatency.Text = "平均延迟: -- ms";
        }

        formsPlotLatency.Plot.Clear();

        if (_latencyHistory.Count > 0)
        {
            var xs = _latencyHistory.Select(p => p.time.ToOADate()).ToArray();
            var ys = _latencyHistory.Select(p => p.latency >= 0 ? (double)p.latency : 0).ToArray();

            var signal = formsPlotLatency.Plot.Add.Scatter(xs, ys);
            signal.Color = ScottPlot.Color.FromHex("#007BFF");
            signal.LineWidth = 2;
            signal.MarkerSize = 4;

            formsPlotLatency.Plot.Axes.DateTimeTicksBottom();
            
            var maxLatency = ys.Max();
            formsPlotLatency.Plot.Axes.SetLimitsY(0, Math.Max(maxLatency * 1.2, 100));
        }

        formsPlotLatency.Refresh();
    }

    private async void materialButtonRefreshHosts_Click(object? sender, EventArgs e)
    {
        try
        {
            materialButtonRefreshHosts.Enabled = false;
            _currentHostsContent = await _fileService.ReadHostsFileAsync();
            UpdateHostsPreview();
            lblUpdateTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            txtHostsPreview.Text = $"读取失败: {ex.Message}";
        }
        finally
        {
            materialButtonRefreshHosts.Enabled = true;
        }
    }

    private void materialButtonOpenNotepad_Click(object? sender, EventArgs e)
    {
        try
        {
            var hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = hostsPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开记事本: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void picStatus_Paint(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var centerX = picStatus.Width / 2;
        var centerY = picStatus.Height / 2;
        var radius = Math.Min(centerX, centerY) - 4;

        using (var glowPath = new System.Drawing.Drawing2D.GraphicsPath())
        {
            for (int i = 3; i >= 0; i--)
            {
                var glowRadius = radius + i * 3;
                var alpha = 30 - i * 7;
                if (alpha < 0) alpha = 0;
                
                using (var glowBrush = new SolidBrush(System.Drawing.Color.FromArgb(alpha, _statusColor)))
                {
                    g.FillEllipse(glowBrush, centerX - glowRadius, centerY - glowRadius, glowRadius * 2, glowRadius * 2);
                }
            }
        }

        using (var brush = new SolidBrush(_statusColor))
        {
            g.FillEllipse(brush, centerX - radius, centerY - radius, radius * 2, radius * 2);
        }

        using (var highlightBrush = new SolidBrush(System.Drawing.Color.FromArgb(60, System.Drawing.Color.White)))
        {
            var highlightRect = new RectangleF(centerX - radius + 4, centerY - radius + 4, radius * 2 - 8, radius - 4);
            g.FillEllipse(highlightBrush, highlightRect);
        }

        using (var pen = new Pen(System.Drawing.Color.FromArgb(100, System.Drawing.Color.White), 2))
        {
            g.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2, radius * 2);
        }
    }

    private void UpdateStatusIndicator(System.Drawing.Color color, string statusText)
    {
        _statusColor = color;
        picStatus.Invalidate();
        lblStatus.Text = statusText;
    }

    private void InitializeSourceMonitorGrid()
    {
        dgvSources.AutoGenerateColumns = false;
        dgvSources.Columns.Clear();

        dgvSources.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Name",
            HeaderText = "数据源名称",
            Width = 160
        });
        dgvSources.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Priority",
            HeaderText = "优先级",
            Width = 60
        });
        dgvSources.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "IsHealthyText",
            HeaderText = "状态",
            Width = 60
        });
        dgvSources.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "LastResponseTimeMs",
            HeaderText = "响应时间(ms)",
            Width = 100
        });
        dgvSources.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Description",
            HeaderText = "描述",
            Width = 300
        });
        dgvSources.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "LastCheckTimeText",
            HeaderText = "最后检测",
            Width = 140
        });

        LoadInitialSourceData();
    }

    private void LoadInitialSourceData()
    {
        var sources = _hostsService.GetHostsSources();
        var displayList = sources.Select(s => new SourceDisplayItem
        {
            Name = s.Name,
            Priority = s.Priority,
            IsHealthy = s.IsHealthy,
            IsHealthyText = s.IsHealthy ? "✓ 健康" : "✗ 异常",
            LastResponseTimeMs = s.LastResponseTimeMs > 0 ? $"{s.LastResponseTimeMs}" : "--",
            Description = s.Description,
            LastCheckTime = s.LastCheckTime,
            LastCheckTimeText = s.LastCheckTime?.ToString("HH:mm:ss") ?? "--"
        }).OrderBy(s => s.Priority).ToList();

        dgvSources.DataSource = displayList;
    }

    private async void materialButtonTestSources_Click(object? sender, EventArgs e)
    {
        if (_isSourceTesting) return;
        _isSourceTesting = true;
        materialButtonTestSources.Enabled = false;
        materialButtonTestSources.Text = "测试中...";

        try
        {
            var results = await _hostsService.CheckSourcesHealthAsync();

            var displayList = results.Select(s => new SourceDisplayItem
            {
                Name = s.Name,
                Priority = s.Priority,
                IsHealthy = s.IsHealthy,
                IsHealthyText = s.IsHealthy ? "✓ 健康" : "✗ 异常",
                LastResponseTimeMs = s.LastResponseTimeMs > 0 ? $"{s.LastResponseTimeMs}" : "超时",
                Description = s.Description,
                LastCheckTime = s.LastCheckTime,
                LastCheckTimeText = s.LastCheckTime?.ToString("HH:mm:ss") ?? "--"
            }).OrderBy(s => s.Priority).ThenBy(s => s.LastResponseTimeMs).ToList();

            dgvSources.DataSource = displayList;

            var bestSource = results.FirstOrDefault(s => s.IsHealthy);
            if (bestSource != null)
            {
                lblBestSource.Text = $"推荐源: {bestSource.Name} ({bestSource.LastResponseTimeMs}ms)";
            }
            else
            {
                lblBestSource.Text = "推荐源: 所有源不可用";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"测试失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            materialButtonTestSources.Enabled = true;
            materialButtonTestSources.Text = "测试所有源";
            _isSourceTesting = false;
        }
    }
}

public class SourceDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsHealthy { get; set; }
    public string IsHealthyText { get; set; } = string.Empty;
    public string LastResponseTimeMs { get; set; } = "--";
    public string Description { get; set; } = string.Empty;
    public DateTime? LastCheckTime { get; set; }
    public string LastCheckTimeText { get; set; } = "--";
}
