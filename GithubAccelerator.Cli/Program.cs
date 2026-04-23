using Spectre.Console;
using GithubAccelerator.Services;

namespace GithubAccelerator.Cli;

public static class Program
{
    private static readonly GithubHostsService _hostsService = new();
    private static readonly IHostsFileService _fileService;
    private static readonly IDnsFlusher _dnsFlusher;
    private static readonly IStartupManager _startupManager;
    private static readonly GithubConnectionService _connectionService = new();
    private static readonly IpSpeedTestService _speedTestService = new();
    private static readonly GithubProxyService _proxyService = new();
    private static CancellationTokenSource _statusCts = new();
    private static List<(string ip, string domain)> _currentBestIps = new();
    private static bool _isTesting = false;

    static Program()
    {
        _fileService = PlatformServiceFactory.CreateHostsFileService(PlatformServiceFactory.CreatePlatformService());
        _dnsFlusher = PlatformServiceFactory.CreateDnsFlusher();
        _startupManager = PlatformServiceFactory.CreateStartupManager();
    }

    public static async Task<int> Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("GitHub Accelerator").Color(Color.Blue));
        
        // 检查命令行参数
        if (args.Length > 0)
        {
            return await HandleCommandLineArgs(args);
        }
        
        // 一键启动模式
        AnsiConsole.MarkupLine("[yellow]正在启动一键加速模式...[/]");
        AnsiConsole.WriteLine();
        
        var result = await QuickStartAsync();
        
        if (result == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✓ 一键加速完成！[/]");
            AnsiConsole.MarkupLine("[dim]按任意键进入交互模式，或按 Ctrl+C 退出...[/]");
            Console.ReadKey(true);
        }
        
        // 进入交互模式
        PrintHelp();

        _statusCts = new CancellationTokenSource();
        _ = Task.Run(() => AutoStatusLoop(_statusCts.Token));

        AnsiConsole.WriteLine();
        await CheckStatusAsync();

        while (true)
        {
            AnsiConsole.Markup("\n[bold]Command[/] (?=help, q=quit): ");
            var input = Console.ReadLine()?.Trim().ToLower();

            if (input == "q" || input == "quit" || input == "exit")
                break;

            if (string.IsNullOrEmpty(input))
                continue;

            int cmdResult;
            switch (input)
            {
                case "1":
                case "s":
                case "status":
                    cmdResult = await CheckStatusAsync();
                    break;
                case "2":
                case "a":
                case "apply":
                    cmdResult = await ApplyHostsAsync();
                    break;
                case "3":
                case "r":
                case "restore":
                    cmdResult = await RestoreHostsAsync();
                    break;
                case "4":
                case "f":
                case "refresh":
                    cmdResult = await RefreshHostsAsync();
                    break;
                case "5":
                case "t":
                case "test":
                    cmdResult = await TestConnectionAsync();
                    break;
                case "6":
                case "p":
                case "preview":
                    cmdResult = await ShowHostsPreviewAsync();
                    break;
                case "7":
                case "x":
                case "proxy":
                    cmdResult = await ToggleProxyAsync();
                    break;
                case "8":
                case "g":
                case "git":
                    cmdResult = await ConfigureGitProxyAsync();
                    break;
                case "0":
                case "9":
                case "?":
                case "？":
                case "h":
                case "help":
                    PrintHelp();
                    cmdResult = 0;
                    break;
                case "quick":
                case "start":
                case "quickstart":
                    cmdResult = await QuickStartAsync();
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown: {input}[/]");
                    cmdResult = 1;
                    break;
            }
        }

        _statusCts.Cancel();
        return 0;
    }
    
    private static async Task<int> HandleCommandLineArgs(string[] args)
    {
        var command = args[0].ToLower();
        
        switch (command)
        {
            case "--help":
            case "-h":
                PrintHelp();
                return 0;
            case "--status":
            case "-s":
                return await CheckStatusAsync();
            case "--apply":
            case "-a":
                return await ApplyHostsAsync();
            case "--restore":
            case "-r":
                return await RestoreHostsAsync();
            case "--test":
            case "-t":
                return await TestConnectionAsync();
            case "--proxy":
            case "-x":
                return await ToggleProxyAsync();
            case "--git":
            case "-g":
                return await ConfigureGitProxyAsync();
            case "--quick":
            case "-q":
                return await QuickStartAsync();
            default:
                AnsiConsole.MarkupLine($"[red]Unknown command: {command}[/]");
                PrintHelp();
                return 1;
        }
    }
    
    private static async Task<int> QuickStartAsync()
    {
        try
        {
            AnsiConsole.MarkupLine("[dim]1. 正在检查当前状态...[/]");
            var hostsContent = await _fileService.ReadHostsFileAsync();
            var isApplied = _fileService.IsGithubHostsApplied(hostsContent);
            
            if (isApplied)
            {
                AnsiConsole.MarkupLine("[yellow]加速已启用，正在刷新 Hosts...[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]2. 正在获取最新 Hosts...[/]");
            }
            
            var newHosts = await _hostsService.FetchHostsAsync();
            var parsedHosts = _hostsService.ParseHosts(newHosts);
            
            AnsiConsole.MarkupLine("[dim]3. 正在进行 IP 测速优选...[/]");
            var speedResults = await _speedTestService.TestIpListAsync(parsedHosts);
            var bestIps = _speedTestService.SelectBestIps(speedResults, maxPerDomain: 3);
            var optimizedContent = _speedTestService.GenerateOptimizedHostsContent(bestIps, newHosts);
            
            AnsiConsole.MarkupLine("[dim]4. 正在应用 Hosts 配置...[/]");
            await _fileService.BackupHostsFileAsync();
            await _fileService.ApplyGithubHostsAsync(optimizedContent);
            await _dnsFlusher.FlushDnsCacheAsync();
            
            _currentBestIps = bestIps;
            
            AnsiConsole.MarkupLine("[dim]5. 正在启动代理服务...[/]");
            var domainIpMap = _currentBestIps.ToDictionary(x => x.domain, x => x.ip);
            _proxyService.SetDomainIpMap(domainIpMap);
            await _proxyService.StartAsync();
            
            AnsiConsole.MarkupLine("[dim]6. 正在配置 Git 代理...[/]");
            await _proxyService.ConfigureGitProxyAsync();
            
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✓ 一键加速完成！[/]");
            AnsiConsole.MarkupLine($"[dim]✓ 已优选 {bestIps.Count} 个最优 IP[/]");
            AnsiConsole.MarkupLine("[dim]✓ DNS 缓存已刷新[/]");
            AnsiConsole.MarkupLine($"[dim]✓ 代理服务已启动 (端口: {_proxyService.ProxyPort})[/]");
            AnsiConsole.MarkupLine("[dim]✓ Git 代理已配置[/]");
            
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine("\n[red]✗ 需要管理员权限！请以管理员身份运行此程序。[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ 错误: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task AutoStatusLoop(CancellationToken token)
    {
        var lastRefresh = DateTime.Now;
        var refreshInterval = TimeSpan.FromHours(2);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (token.IsCancellationRequested) break;
                if (_isTesting)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine("=== 自动状态 ===");
                var hostsContent = await _fileService.ReadHostsFileAsync();
                var isApplied = _fileService.IsGithubHostsApplied(hostsContent);

                if (isApplied)
                {
                    var block = _fileService.GetCurrentGithubHostsBlock(hostsContent);
                    var parsedHosts = _hostsService.ParseHosts(block);
                    AnsiConsole.MarkupLine($"[green]状态: 已启用 ({parsedHosts.Count} 条记录)[/]");

                    var updateTimeMatch = System.Text.RegularExpressions.Regex.Match(block, @"Update[ -]time:\s*(.+)");
                    if (updateTimeMatch.Success)
                    {
                        var updateTimeStr = updateTimeMatch.Groups[1].Value.Trim();
                        AnsiConsole.MarkupLine($"[dim]更新时间：{updateTimeStr}[/]");

                        if (DateTime.TryParse(updateTimeStr, out var updateTime))
                        {
                            var nextRefreshTime = updateTime.AddHours(2);
                            var remainingTime = nextRefreshTime - DateTime.Now;
                            if (remainingTime.TotalSeconds > 0)
                                AnsiConsole.MarkupLine($"[dim]下次刷新：{remainingTime.Hours}小时{remainingTime.Minutes}分{remainingTime.Seconds}秒[/]");
                            else
                                AnsiConsole.MarkupLine("[yellow]Hosts 已过期，建议手动刷新[/]");
                        }
                    }

                    if (DateTime.Now - lastRefresh > refreshInterval)
                    {
                        AnsiConsole.MarkupLine("[yellow]Hosts 已过期，正在自动刷新...[/]");
                        try
                        {
                            var newHosts = await _hostsService.FetchHostsAsync();
                            var newParsed = _hostsService.ParseHosts(newHosts);
                            var newSpeedResults = await _speedTestService.TestIpListAsync(newParsed, cancellationToken: token);
                            var newBestIps = _speedTestService.SelectBestIps(newSpeedResults, maxPerDomain: 3);
                            var newOptimized = _speedTestService.GenerateOptimizedHostsContent(newBestIps, newHosts);

                            await _fileService.ApplyGithubHostsAsync(newOptimized);
                            await _dnsFlusher.FlushDnsCacheAsync();
                            lastRefresh = DateTime.Now;
                            _currentBestIps = newBestIps;
                            AnsiConsole.MarkupLine("[green]✓ 自动刷新成功[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[yellow]⚠ 自动刷新失败: {ex.Message}[/]");
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]状态: 未启用[/]");
                }

                try
                {
                    using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using var response = await client.GetAsync("https://api.github.com", System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
                    sw.Stop();
                    var status = response.IsSuccessStatusCode ? "OK" : $"{(int)response.StatusCode}";
                    AnsiConsole.MarkupLine($"[dim]延迟: {sw.ElapsedMilliseconds}ms | API: {status}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[dim]延迟: N/A | API: 失败 ({ex.Message})[/]");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private static void PrintHelp()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("可用命令：");
        var table = new Table();
        table.AddColumn(new TableColumn("按键").Centered());
        table.AddColumn(new TableColumn("功能").LeftAligned());
        table.AddRow("1 / s", "检查当前状态");
        table.AddRow("2 / a", "启用加速");
        table.AddRow("3 / r", "恢复原状");
        table.AddRow("4 / f", "刷新 Hosts");
        table.AddRow("5 / t", "测试连接");
        table.AddRow("6 / p", "查看 Hosts 预览");
        table.AddRow("7 / x", "启动/停止代理");
        table.AddRow("8 / g", "配置 Git 代理");
        table.AddRow("quick", "一键加速（自动配置所有功能）");
        table.AddRow("0 / ?", "显示帮助");
        table.AddRow("q", "退出程序");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine("自动状态：每 30 秒刷新一次");
        AnsiConsole.WriteLine("命令行参数：--help, --status, --apply, --restore, --test, --proxy, --git, --quick");
    }

    private static async Task<int> CheckStatusAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== 检查当前状态 ===");
        AnsiConsole.WriteLine();

        try
        {
            var hostsContent = await _fileService.ReadHostsFileAsync();
            var isApplied = _fileService.IsGithubHostsApplied(hostsContent);

            if (isApplied)
            {
                var block = _fileService.GetCurrentGithubHostsBlock(hostsContent);
                var parsedHosts = _hostsService.ParseHosts(block);

                var table = new Table();
                table.AddColumn(new TableColumn("状态").Centered());
                table.AddColumn(new TableColumn("详情").LeftAligned());
                table.AddRow(new Markup("[green]已启用[/]"), new Markup($"{parsedHosts.Count} 条 Hosts 记录"));

                var updateTimeMatch = System.Text.RegularExpressions.Regex.Match(block, @"Update[ -]time:\s*(.+)");
                if (updateTimeMatch.Success)
                    table.AddRow("更新时间", updateTimeMatch.Groups[1].Value.Trim());

                AnsiConsole.Write(table);
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]GitHub 加速未启用[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> ApplyHostsAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== 启用加速 ===");
        AnsiConsole.WriteLine();

        try
        {
            AnsiConsole.MarkupLine("[dim]正在获取最新 Hosts...[/]");
            var hostsContent = await _hostsService.FetchHostsAsync();
            var parsedHosts = _hostsService.ParseHosts(hostsContent);

            AnsiConsole.MarkupLine("[dim]正在进行 IP 测速优选...[/]");
            var speedResults = await _speedTestService.TestIpListAsync(parsedHosts);
            var bestIps = _speedTestService.SelectBestIps(speedResults, maxPerDomain: 3);

            var summaryTable = new Table();
            summaryTable.AddColumn(new TableColumn("域名").LeftAligned());
            summaryTable.AddColumn(new TableColumn("最优 IP").LeftAligned());
            summaryTable.AddColumn(new TableColumn("延迟").LeftAligned());
            summaryTable.AddColumn(new TableColumn("状态").LeftAligned());

            var groupedByDomain = speedResults
                .Where(r => r.IsReachable)
                .GroupBy(r => r.Domain)
                .OrderBy(g => g.Min(r => r.LatencyMs));

            foreach (var group in groupedByDomain.Take(10))
            {
                var best = group.First();
                summaryTable.AddRow(
                    group.Key,
                    best.Ip,
                    $"{best.LatencyMs}ms",
                    "[green]✓[/]"
                );
            }
            if (groupedByDomain.Count() > 10)
            {
                summaryTable.AddRow($"... 还有 {groupedByDomain.Count() - 10} 个域名", "", "", "");
            }
            AnsiConsole.Write(summaryTable);

            var optimizedContent = _speedTestService.GenerateOptimizedHostsContent(bestIps, hostsContent);

            _currentBestIps = bestIps;

            await _fileService.BackupHostsFileAsync();
            await _fileService.ApplyGithubHostsAsync(optimizedContent);
            await _dnsFlusher.FlushDnsCacheAsync();

            AnsiConsole.MarkupLine($"\n[green]✓ Hosts 配置已应用成功！[/]");
            AnsiConsole.MarkupLine($"[dim]✓ 已优选 {bestIps.Count} 个最优 IP[/]");
            AnsiConsole.MarkupLine("[dim]✓ DNS 缓存已刷新[/]");
            AnsiConsole.MarkupLine("[dim]✓ 启动代理: 输入 'x' 命令[/]");
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine("\n[red]✗ 需要管理员权限！请以管理员身份运行此程序。[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ 错误: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> RestoreHostsAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== 恢复原状 ===");
        AnsiConsole.WriteLine();

        try
        {
            await _fileService.RestoreOriginalHostsAsync();
            AnsiConsole.MarkupLine("\n[green]✓ Hosts 已恢复原始配置[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]错误：{ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> RefreshHostsAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== Refresh Hosts ===");
        AnsiConsole.WriteLine();

        try
        {
            AnsiConsole.MarkupLine("[dim]Fetching latest Hosts...[/]");
            var hostsContent = await _hostsService.FetchHostsAsync();
            var parsedHosts = _hostsService.ParseHosts(hostsContent);

            var table = new Table();
            table.AddColumn(new TableColumn("Domain").LeftAligned());
            table.AddColumn(new TableColumn("IP").LeftAligned());
            foreach (var host in parsedHosts.Take(10))
            {
                table.AddRow(host.domain, host.ip);
            }
            if (parsedHosts.Count > 10)
            {
                table.AddRow($"... and {parsedHosts.Count - 10} more", "");
            }
            AnsiConsole.Write(table);

            var updateTime = _hostsService.GetUpdateTime(hostsContent);
            if (updateTime.HasValue)
            {
                AnsiConsole.MarkupLine($"\n[dim]Update Time: {updateTime.Value:yyyy-MM-dd HH:mm:ss}[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> TestConnectionAsync()
    {
        _isTesting = true;
        
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== GitHub 连接测试 ===");
        AnsiConsole.WriteLine();

        var results = new List<ConnectionTestResult>();

        _connectionService.OnTestComplete += result =>
        {
            results.Add(result);
            var status = result.IsSuccess ? "[green]OK[/]" : "[red]FAIL[/]";
            var host = GetShortEndpoint(result.Endpoint);
            AnsiConsole.MarkupLine($"{status} {host,-40} {result.LatencyMs,5}ms");
        };

        _connectionService.OnLog += message =>
        {
            AnsiConsole.WriteLine(message);
        };

        _connectionService.OnStatusUpdate += (status, _) =>
        {
            AnsiConsole.WriteLine($">> {status}");
        };

        try
        {
            var testResults = await _connectionService.TestAllConnectionsAsync();
            var (latency, ip, success) = await _connectionService.TestGitHubLatencyAsync();

            var summaryTable = new Table();
            summaryTable.AddColumn(new TableColumn("Metric").Centered());
            summaryTable.AddColumn(new TableColumn("Value").Centered());
            summaryTable.AddRow("GitHub.com Latency", $"{latency}ms");
            summaryTable.AddRow("DNS Resolved IP", ip);
            summaryTable.AddRow("API Connection", success ? "[green]OK[/]" : "[red]Failed[/]");

            var successCount = results.Count(r => r.IsSuccess);
            var failCount = results.Count(r => !r.IsSuccess);
            summaryTable.AddRow("Test Result", $"{successCount} passed, {failCount} failed");

            AnsiConsole.WriteLine();
            AnsiConsole.Write(summaryTable);

            if (failCount == 0 && success)
            {
                AnsiConsole.MarkupLine("\n[bold green]All connection tests passed![/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"\n[bold yellow]{failCount} connection(s) failed[/]");
                
                if (successCount == 0)
                {
                    AnsiConsole.MarkupLine("\n[bold red]⚠️ 警告: 无法连接到 GitHub！[/]");
                    AnsiConsole.MarkupLine("[red]可能原因:[/]");
                    AnsiConsole.MarkupLine("  1. TCP 443 端口被网络运营商封锁");
                    AnsiConsole.MarkupLine("  2. DNS 解析正常但 HTTPS 请求被阻断");
                    AnsiConsole.MarkupLine("\n[cyan]解决方案:[/]");
                    AnsiConsole.MarkupLine("  - 使用代理软件/VPN");
                    AnsiConsole.MarkupLine("  - 尝试刷新获取新的 IP 段");
                    AnsiConsole.MarkupLine("  - 等待一段时间后重试");
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
        finally
        {
            _isTesting = false;
        }
    }

    private static async Task<int> ShowHostsPreviewAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== Hosts 预览 ===");
        AnsiConsole.WriteLine();

        try
        {
            var hostsContent = await _fileService.ReadHostsFileAsync();
            var isApplied = _fileService.IsGithubHostsApplied(hostsContent);

            if (isApplied)
            {
                var block = _fileService.GetCurrentGithubHostsBlock(hostsContent);
                var parsedHosts = _hostsService.ParseHosts(block);

                var panel = new Panel(string.Join("\n", parsedHosts.Select(h => $"{h.ip,-20} {h.domain}")));
                panel.Header = new PanelHeader("GitHub Hosts Configuration");
                panel.Border = BoxBorder.Rounded;
                AnsiConsole.Write(panel);
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Hosts configuration not applied[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> ToggleProxyAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== 代理服务器 ===");
        AnsiConsole.WriteLine();

        try
        {
            if (_proxyService.IsRunning)
            {
                await _proxyService.StopAsync();
                AnsiConsole.MarkupLine("[yellow]✓ 代理服务器已停止[/]");
                AnsiConsole.MarkupLine($"[dim]端口: {_proxyService.ProxyPort}[/]");
                AnsiConsole.MarkupLine($"[dim]处理请求: {_proxyService.RequestsHandled}[/]");
                AnsiConsole.MarkupLine($"[dim]传输数据: {FormatBytes(_proxyService.TotalBytesTransferred)}[/]");
            }
            else
            {
                var domainIpMap = _currentBestIps.ToDictionary(x => x.domain, x => x.ip);
                _proxyService.SetDomainIpMap(domainIpMap);

                _proxyService.OnLog += msg => AnsiConsole.MarkupLine($"[dim]{msg}[/]");

                await _proxyService.StartAsync();
                AnsiConsole.MarkupLine("[green]✓ 代理服务器已启动[/]");
                AnsiConsole.MarkupLine($"[dim]端口: {_proxyService.ProxyPort}[/]");
                AnsiConsole.MarkupLine("[dim]配置 Git 代理: 输入 'g' 命令[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ 错误: {ex.Message}[/]");
            return 1;
        }
    }

    private static async Task<int> ConfigureGitProxyAsync()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("=== Git 代理配置 ===");
        AnsiConsole.WriteLine();

        try
        {
            if (!_proxyService.IsRunning)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ 代理服务器未运行，正在启动...[/]");
                var domainIpMap = _currentBestIps.ToDictionary(x => x.domain, x => x.ip);
                _proxyService.SetDomainIpMap(domainIpMap);
                await _proxyService.StartAsync();
            }

            var isConfigured = _proxyService.IsGitProxyConfigured();

            if (isConfigured)
            {
                AnsiConsole.MarkupLine("[yellow]当前 Git 代理已配置[/]");
                AnsiConsole.MarkupLine("[dim]正在移除 Git 代理配置...[/]");
                await _proxyService.RemoveGitProxyAsync();
                AnsiConsole.MarkupLine("[green]✓ Git 代理已移除[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]正在配置 Git 代理...[/]");
                var success = await _proxyService.ConfigureGitProxyAsync();
                if (success)
                {
                    AnsiConsole.MarkupLine("[green]✓ Git 代理已配置[/]");
                    AnsiConsole.MarkupLine($"[dim]代理地址: http://localhost:{_proxyService.ProxyPort}[/]");
                    AnsiConsole.MarkupLine("[dim]现在可以使用 git clone/pull/push 加速了[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]✗ Git 代理配置失败[/]");
                    return 1;
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ 错误: {ex.Message}[/]");
            return 1;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string GetShortEndpoint(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }
}
