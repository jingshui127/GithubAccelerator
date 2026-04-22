# GitHub 加速器 Pro - 软件设计文档 (SDD)

**文档编号**: SDD-2026-001  
**版本**: v1.0.0  
**创建日期**: 2026-04-22  
**最后更新**: 2026-04-22  
**文档状态**: 正式发布  

---

## 目录

1. [设计概述](#1-设计概述)
2. [架构设计](#2-架构设计)
3. [模块详细设计](#3-模块详细设计)
4. [数据设计](#4-数据设计)
5. [接口设计](#5-接口设计)
6. [安全设计](#6-安全设计)
7. [性能设计](#7-性能设计)

---

## 1. 设计概述

### 1.1 设计目标

- **可维护性**: 模块化设计，低耦合高内聚
- **可扩展性**: 支持新功能扩展，支持跨平台
- **可靠性**: 异常处理完善，自动恢复机制
- **性能**: 响应快速，资源占用低

### 1.2 设计原则

| 原则 | 说明 |
|------|------|
| 单一职责 | 每个类只负责一个功能 |
| 开闭原则 | 对扩展开放，对修改关闭 |
| 依赖倒置 | 依赖抽象而非具体实现 |
| 接口隔离 | 接口最小化，专用化 |
| MVVM 模式 | 界面与业务逻辑分离 |

### 1.3 技术选型

| 层次 | 技术选型 | 版本 | 说明 |
|------|----------|------|------|
| 语言 | C# | 10+ | 现代化语言特性 |
| 框架 | .NET | 10 | 长期支持版本 |
| UI 框架 | Avalonia | 12 | 跨平台 UI 框架 |
| MVVM | CommunityToolkit.Mvvm | 8.x | MVVM 工具库 |
| 日志 | Serilog | 4.x | 结构化日志 |
| 测试 | xUnit | 2.x | 单元测试框架 |

---

## 2. 架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           表现层 (Presentation)                          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐        │
│  │  Avalonia UI    │  │  Windows Forms  │  │      CLI        │        │
│  │   (现代界面)     │  │   (传统界面)     │  │   (命令行)      │        │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘        │
│           │                    │                    │                  │
│           └────────────────────┼────────────────────┘                  │
│                                │                                       │
│  ┌─────────────────────────────▼─────────────────────────────────────┐ │
│  │                      ViewModels (视图模型层)                        │ │
│  │  MainWindowViewModel, DashboardViewModel, SettingsViewModel...    │ │
│  └─────────────────────────────┬─────────────────────────────────────┘ │
└────────────────────────────────┼────────────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────────────┐
│                                │         业务层                         │
│  ┌─────────────────────────────▼─────────────────────────────────────┐ │
│  │                        Services (服务层)                            │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐              │ │
│  │  │GithubHosts   │ │SourcePerf    │ │SmartSource   │              │ │
│  │  │Service       │ │Monitor       │ │Selector      │              │ │
│  │  └──────────────┘ └──────────────┘ └──────────────┘              │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐              │ │
│  │  │SourceStats   │ │HostsBackup   │ │LogService    │              │ │
│  │  │Service       │ │Service       │ │              │              │ │
│  │  └──────────────┘ └──────────────┘ └──────────────┘              │ │
│  └─────────────────────────────┬─────────────────────────────────────┘ │
└────────────────────────────────┼────────────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────────────┐
│                                │         数据层                         │
│  ┌─────────────────────────────▼─────────────────────────────────────┐ │
│  │                     Persistence (持久化层)                          │ │
│  │  SourcePerformancePersistence, Configuration, HostsBackup         │ │
│  └─────────────────────────────┬─────────────────────────────────────┘ │
└────────────────────────────────┼────────────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────────────┐
│                                │       基础设施层                       │
│  ┌─────────────────────────────▼─────────────────────────────────────┐ │
│  │                    Infrastructure (基础设施)                        │ │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐              │ │
│  │  │WindowsHosts  │ │HttpClient    │ │Serilog       │              │ │
│  │  │FileService   │ │Factory       │ │Logger        │              │ │
│  │  └──────────────┘ └──────────────┘ └──────────────┘              │ │
│  └───────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 项目结构

```
GithubAccelerator/
├── GithubAccelerator.Core/           # 核心业务层
│   ├── Services/
│   │   ├── GithubHostsService.cs     # Hosts 数据获取
│   │   ├── SourcePerformanceMonitor.cs # 性能监控
│   │   ├── SmartSourceSelector.cs    # 智能选择
│   │   ├── SourcePerformancePersistence.cs # 持久化
│   │   └── WindowsHostsFileService.cs # Hosts 文件操作
│   ├── Models/
│   │   ├── HostRecord.cs             # Hosts 记录模型
│   │   └── SourcePerformanceModels.cs # 性能数据模型
│   └── GithubAccelerator.Core.csproj
│
├── GithubAccelerator.UI/             # Avalonia UI 层
│   ├── Views/                        # 视图
│   │   ├── MainWindow.axaml
│   │   ├── DashboardView.axaml
│   │   ├── SettingsView.axaml
│   │   └── ...
│   ├── ViewModels/                   # 视图模型
│   │   ├── MainWindowViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   └── ...
│   ├── Services/                     # UI 服务
│   │   ├── ThemeManager.cs
│   │   ├── NotificationService.cs
│   │   └── ...
│   ├── Controls/                     # 自定义控件
│   │   └── SimpleChart.cs
│   ├── Converters/                   # 值转换器
│   └── Helpers/                      # 辅助类
│
├── GithubAccelerator/                # Windows Forms 界面
│   ├── MainForm.cs
│   └── ...
│
├── GithubAccelerator.Tests/          # 测试项目
│   ├── UnitTests.cs
│   ├── IntegrationTests.cs
│   └── FunctionalTests.cs
│
└── docs/                             # 文档目录
    ├── PROJECT_PLAN.md
    ├── SRS.md
    ├── SDD.md
    └── ...
```

### 2.3 设计模式应用

| 模式 | 应用场景 | 实现位置 |
|------|----------|----------|
| MVVM | UI 架构 | ViewModels |
| 单例 | 配置管理 | SettingsViewModel |
| 观察者 | 状态通知 | INotifyPropertyChanged |
| 策略 | 数据源选择 | SmartSourceSelector |
| 工厂 | 服务创建 | ServiceCollection |
| 装饰器 | 日志增强 | Serilog Sinks |

---

## 3. 模块详细设计

### 3.1 GithubHostsService (Hosts 数据获取服务)

#### 3.1.1 类图

```
┌─────────────────────────────────────────────┐
│          GithubHostsService                  │
├─────────────────────────────────────────────┤
│ - _httpClient: HttpClient                    │
│ - _sources: List<DataSource>                 │
│ - _logger: ILogger                           │
├─────────────────────────────────────────────┤
│ + FetchHostsAsync(): Task<HostsResult>       │
│ + FetchFromSourceAsync(url): Task<string>    │
│ + ParseHosts(content): List<HostRecord>      │
│ + ValidateHosts(records): bool               │
│ + AddCustomSource(url): void                 │
│ + RemoveSource(url): void                    │
└─────────────────────────────────────────────┘
```

#### 3.1.2 核心方法设计

**FetchHostsAsync 方法**:

```csharp
public async Task<HostsResult> FetchHostsAsync(CancellationToken cancellationToken = default)
{
    var result = new HostsResult();
    
    foreach (var source in _sources.OrderBy(s => s.Priority))
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var content = await FetchFromSourceAsync(source.Url, cancellationToken);
            stopwatch.Stop();
            
            var records = ParseHosts(content);
            if (ValidateHosts(records))
            {
                result.Records = records;
                result.SourceUrl = source.Url;
                result.ResponseTime = stopwatch.ElapsedMilliseconds;
                result.Success = true;
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to fetch from {Source}", source.Url);
            result.Errors.Add(new SourceError(source.Url, ex.Message));
        }
    }
    
    return result;
}
```

#### 3.1.3 数据源配置

```json
{
  "sources": [
    {
      "name": "FastGit",
      "url": "https://hosts.fastgit.org/hosts",
      "priority": 1,
      "enabled": true
    },
    {
      "name": "GitHub Hosts",
      "url": "https://github.com/521xueweihan/GitHub520/main/hosts",
      "priority": 2,
      "enabled": true
    }
  ]
}
```

---

### 3.2 SourcePerformanceMonitor (性能监控服务)

#### 3.2.1 类图

```
┌─────────────────────────────────────────────┐
│       SourcePerformanceMonitor               │
├─────────────────────────────────────────────┤
│ - _performanceData: Dictionary<string,       │
│                      SourcePerformance>      │
│ - _persistence: SourcePerformancePersistence │
│ - _timer: Timer                              │
│ - _isMonitoring: bool                        │
├─────────────────────────────────────────────┤
│ + StartMonitoring(): void                    │
│ + StopMonitoring(): void                     │
│ + RecordRequest(sourceId, success, time)     │
│ + GetPerformance(sourceId): SourcePerf       │
│ + GetAllPerformance(): List<SourcePerf>      │
│ + CalculateHealthScore(sourceId): double     │
│ + GetBestSource(): string                    │
└─────────────────────────────────────────────┘
```

#### 3.2.2 健康评分算法

```csharp
public double CalculateHealthScore(string sourceId)
{
    var perf = GetPerformance(sourceId);
    if (perf == null) return 0;
    
    // 成功率得分 (权重 40%)
    var successRate = perf.TotalRequests > 0 
        ? (double)perf.SuccessCount / perf.TotalRequests 
        : 0;
    var successScore = successRate * 40;
    
    // 响应时间得分 (权重 30%)
    var avgTime = perf.AverageResponseTime;
    var timeScore = Math.Max(0, 30 - (avgTime / 100));
    
    // 稳定性得分 (权重 30%)
    var stabilityScore = Math.Max(0, 30 - (perf.ConsecutiveFailures * 5));
    
    return successScore + timeScore + stabilityScore;
}
```

#### 3.2.3 状态转换图

```
         ┌─────────────┐
         │   Stopped   │
         └──────┬──────┘
                │ StartMonitoring()
                ▼
         ┌─────────────┐
    ┌───▶│  Monitoring │◀───┐
    │    └──────┬──────┘    │
    │           │           │
    │ StopMonitoring()      │ Timer Tick
    │           │           │
    │           ▼           │
    │    ┌─────────────┐    │
    └────│   Paused    │────┘
         └─────────────┘
```

---

### 3.3 SmartSourceSelector (智能选择器)

#### 3.3.1 类图

```
┌─────────────────────────────────────────────┐
│          SmartSourceSelector                 │
├─────────────────────────────────────────────┤
│ - _performanceMonitor: SourcePerformanceMon  │
│ - _selectionStrategy: ISelectionStrategy     │
├─────────────────────────────────────────────┤
│ + SelectBestSource(): SourceSelection        │
│ + SelectByStrategy(strategy): SourceSelect   │
│ + GetRecommendedSources(count): List<Source> │
└─────────────────────────────────────────────┘
         │
         │ implements
         ▼
┌─────────────────────────────────────────────┐
│          ISelectionStrategy                  │
├─────────────────────────────────────────────┤
│ + Select(sources): SourceSelection           │
└─────────────────────────────────────────────┘
         △
         │
    ┌────┴────┬────────────┐
    │         │            │
┌───┴───┐ ┌───┴───┐ ┌──────┴─────┐
│Health │ │Speed  │ │RoundRobin  │
│Based  │ │Based  │ │            │
└───────┘ └───────┘ └────────────┘
```

#### 3.3.2 选择策略实现

```csharp
public class HealthBasedStrategy : ISelectionStrategy
{
    public SourceSelection Select(IEnumerable<SourcePerformance> sources)
    {
        var healthySources = sources
            .Where(s => s.HealthScore > 60)
            .Where(s => s.ConsecutiveFailures == 0)
            .OrderByDescending(s => s.HealthScore)
            .ThenBy(s => s.AverageResponseTime)
            .ToList();
        
        if (healthySources.Any())
        {
            return new SourceSelection
            {
                SourceId = healthySources.First().SourceId,
                Reason = "健康评分最高且无连续失败"
            };
        }
        
        // 降级策略：选择评分最高的
        var bestAvailable = sources
            .OrderByDescending(s => s.HealthScore)
            .FirstOrDefault();
        
        return new SourceSelection
        {
            SourceId = bestAvailable?.SourceId,
            Reason = "降级选择：评分最高"
        };
    }
}
```

---

### 3.4 WindowsHostsFileService (Hosts 文件服务)

#### 3.4.1 类图

```
┌─────────────────────────────────────────────┐
│       WindowsHostsFileService                │
├─────────────────────────────────────────────┤
│ - _hostsPath: string                         │
│ - _backupService: HostsBackupService         │
│ - _logger: ILogger                           │
├─────────────────────────────────────────────┤
│ + ApplyHostsAsync(records): Task<bool>       │
│ + ClearHostsAsync(): Task<bool>              │
│ + BackupHostsAsync(): Task<string>           │
│ + RestoreHostsAsync(backupPath): Task<bool>  │
│ + ReadHostsAsync(): Task<string>             │
│ + IsAdminPrivilege(): bool                   │
│ + FlushDnsCache(): void                      │
└─────────────────────────────────────────────┘
```

#### 3.4.2 应用 Hosts 流程

```
ApplyHostsAsync(records)
        │
        ▼
┌───────────────┐     No    ┌─────────────────┐
│ IsAdmin?      │──────────▶│ Throw Exception │
└───────┬───────┘           └─────────────────┘
        │ Yes
        ▼
┌───────────────┐
│ Backup Hosts  │
└───────┬───────┘
        │
        ▼
┌───────────────┐
│ Read Current  │
│ Hosts Content │
└───────┬───────┘
        │
        ▼
┌───────────────┐
│ Remove Old    │
│ Accelerator   │
│ Records       │
└───────┬───────┘
        │
        ▼
┌───────────────┐
│ Add New       │
│ Records       │
└───────┬───────┘
        │
        ▼
┌───────────────┐
│ Write Hosts   │
│ File          │
└───────┬───────┘
        │
        ▼
┌───────────────┐
│ Flush DNS     │
│ Cache         │
└───────┬───────┘
        │
        ▼
    Success
```

#### 3.4.3 Hosts 文件格式

```
# GitHub Accelerator Pro - Start
# Generated: 2026-04-22 12:00:00
# Source: https://hosts.fastgit.org/hosts

140.82.112.4 github.com
140.82.112.9 github.com
140.82.112.10 github.com
...
# GitHub Accelerator Pro - End
```

---

### 3.5 MainWindowViewModel (主窗口视图模型)

#### 3.5.1 类图

```
┌─────────────────────────────────────────────┐
│          MainWindowViewModel                 │
├─────────────────────────────────────────────┤
│ + Sources: ObservableCollection<SourceVM>    │
│ + StatusMessage: string                      │
│ + IsMonitoring: bool                         │
│ + IsDarkMode: bool                           │
│ + CurrentView: UserControl                   │
├─────────────────────────────────────────────┤
│ + RefreshSourcesCommand: RelayCommand        │
│ + ApplyHostsCommand: RelayCommand            │
│ + ToggleMonitoringCommand: RelayCommand      │
│ + ToggleThemeCommand: RelayCommand           │
│ + ShowSettingsCommand: RelayCommand          │
│ + ShowAboutCommand: RelayCommand             │
│ + ExportDataCommand: RelayCommand            │
│ + ImportDataCommand: RelayCommand            │
├─────────────────────────────────────────────┤
│ - InitializeServices(): void                 │
│ - LoadSources(): void                        │
│ - UpdateStatus(message): void                │
│ - OnMonitoringStateChanged(): void           │
└─────────────────────────────────────────────┘
```

#### 3.5.2 命令绑定

| 命令 | 快捷键 | 功能 |
|------|--------|------|
| RefreshSourcesCommand | Ctrl+R | 刷新数据源 |
| ApplyHostsCommand | Ctrl+S | 应用 Hosts |
| ViewHostsContentCommand | Ctrl+H | 查看 Hosts 内容 |
| ToggleMonitoringCommand | F5 | 切换监控状态 |

---

### 3.6 SimpleChart (自定义图表控件)

#### 3.6.1 类图

```
┌─────────────────────────────────────────────┐
│          SimpleLineChart : Control           │
├─────────────────────────────────────────────┤
│ + Values: IList<double>                      │
│ + Labels: IList<string>                      │
│ + LineColor: Color                           │
│ + FillColor: Color                           │
│ + ShowGrid: bool                             │
│ + ShowLabels: bool                           │
├─────────────────────────────────────────────┤
│ - Render(context): void                      │
│ - DrawGrid(context): void                    │
│ - DrawLine(context): void                    │
│ - DrawLabels(context): void                  │
│ - DrawPoints(context): void                  │
└─────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│          SimpleBarChart : Control            │
├─────────────────────────────────────────────┤
│ + Values: IList<double>                      │
│ + Labels: IList<string>                      │
│ + BarColor: Color                            │
│ + ShowValues: bool                           │
├─────────────────────────────────────────────┤
│ - Render(context): void                      │
│ - DrawBars(context): void                    │
│ - DrawLabels(context): void                  │
│ - DrawValues(context): void                  │
└─────────────────────────────────────────────┘
```

#### 3.6.2 渲染流程

```
Render(context)
    │
    ├──▶ CalculateBounds()
    │
    ├──▶ if (ShowGrid) DrawGrid()
    │
    ├──▶ DrawAxes()
    │
    ├──▶ DrawData()
    │        │
    │        ├── LineChart: DrawLine() + DrawPoints()
    │        └── BarChart: DrawBars()
    │
    └──▶ if (ShowLabels) DrawLabels()
```

---

## 4. 数据设计

### 4.1 数据模型

#### 4.1.1 HostRecord

```csharp
public class HostRecord
{
    public string IP { get; set; }
    public string Domain { get; set; }
    public string Comment { get; set; }
    public DateTime AddedAt { get; set; }
    public string Source { get; set; }
}
```

#### 4.1.2 SourcePerformance

```csharp
public class SourcePerformance
{
    public string SourceId { get; set; }
    public string SourceName { get; set; }
    public string SourceUrl { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double AverageResponseTime { get; set; }
    public double HealthScore { get; set; }
    public DateTime LastSuccessTime { get; set; }
    public DateTime LastFailureTime { get; set; }
    public int ConsecutiveFailures { get; set; }
    public List<ResponseTimeRecord> ResponseHistory { get; set; }
}

public class ResponseTimeRecord
{
    public DateTime Timestamp { get; set; }
    public double ResponseTime { get; set; }
    public bool Success { get; set; }
}
```

#### 4.1.3 AppConfiguration

```csharp
public class AppConfiguration
{
    public bool AutoStart { get; set; }
    public bool StartMinimized { get; set; }
    public bool AutoMonitor { get; set; }
    public int RefreshInterval { get; set; }
    public int Timeout { get; set; }
    public bool IsDarkMode { get; set; }
    public List<DataSourceConfig> DataSources { get; set; }
}

public class DataSourceConfig
{
    public string Name { get; set; }
    public string Url { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; }
}
```

### 4.2 数据存储

#### 4.2.1 存储位置

```
%AppData%/GithubAccelerator/
├── config.json              # 应用配置
├── data/
│   ├── performance.json     # 性能数据
│   └── history.json         # 历史记录
├── logs/
│   ├── app-20260422.log     # 应用日志
│   └── error-20260422.log   # 错误日志
└── backups/
    ├── hosts_20260422_120000.txt  # Hosts 备份
    └── config_20260422.json       # 配置备份
```

#### 4.2.2 JSON 序列化格式

**config.json**:
```json
{
  "version": "1.1.0",
  "autoStart": true,
  "startMinimized": false,
  "autoMonitor": true,
  "refreshInterval": 300,
  "timeout": 5000,
  "isDarkMode": true,
  "dataSources": [
    {
      "name": "FastGit",
      "url": "https://hosts.fastgit.org/hosts",
      "priority": 1,
      "enabled": true
    }
  ]
}
```

**performance.json**:
```json
{
  "version": "1.0.0",
  "lastUpdated": "2026-04-22T12:00:00Z",
  "sources": [
    {
      "sourceId": "fastgit",
      "sourceName": "FastGit",
      "totalRequests": 100,
      "successCount": 95,
      "failureCount": 5,
      "averageResponseTime": 150.5,
      "healthScore": 85.5,
      "consecutiveFailures": 0
    }
  ]
}
```

---

## 5. 接口设计

### 5.1 服务接口

#### 5.1.1 IHostsService

```csharp
public interface IHostsService
{
    Task<HostsResult> FetchHostsAsync(CancellationToken cancellationToken = default);
    Task<bool> ApplyHostsAsync(IEnumerable<HostRecord> records);
    Task<bool> ClearHostsAsync();
    event EventHandler<HostsFetchedEventArgs> HostsFetched;
}
```

#### 5.1.2 IPerformanceMonitor

```csharp
public interface IPerformanceMonitor
{
    bool IsMonitoring { get; }
    void StartMonitoring();
    void StopMonitoring();
    SourcePerformance GetPerformance(string sourceId);
    IEnumerable<SourcePerformance> GetAllPerformance();
    event EventHandler<PerformanceUpdatedEventArgs> PerformanceUpdated;
}
```

#### 5.1.3 INotificationService

```csharp
public interface INotificationService
{
    void ShowSuccess(string message);
    void ShowError(string message);
    void ShowWarning(string message);
    void ShowInfo(string message);
}
```

### 5.2 ViewModel 接口

#### 5.2.1 INavigable

```csharp
public interface INavigable
{
    void OnNavigatedTo();
    void OnNavigatedFrom();
}
```

#### 5.2.2 IRefreshable

```csharp
public interface IRefreshable
{
    Task RefreshAsync();
}
```

---

## 6. 安全设计

### 6.1 权限管理

| 操作 | 权限要求 | 处理方式 |
|------|----------|----------|
| 读取 Hosts | 普通用户 | 直接读取 |
| 修改 Hosts | 管理员 | 检测并提示 |
| 写入配置 | 普通用户 | AppData 目录 |
| 网络请求 | 普通用户 | HTTPS 加密 |

### 6.2 数据安全

| 安全措施 | 说明 |
|----------|------|
| 本地存储 | 所有数据存储在本地，不上传服务器 |
| HTTPS | 所有网络请求使用 HTTPS 加密 |
| 无日志上传 | 不收集用户操作日志 |
| 配置加密 | 敏感配置可加密存储（预留） |

### 6.3 异常处理

```csharp
public class GlobalExceptionHandler
{
    public static void Handle(Exception ex)
    {
        // 记录日志
        Logger.Error(ex, "Unhandled exception");
        
        // 显示友好错误信息
        ShowErrorDialog("发生错误", "应用程序遇到问题，请查看日志获取详情。");
        
        // 上报错误（可选，需用户同意）
        // await ErrorReporter.ReportAsync(ex);
    }
}
```

---

## 7. 性能设计

### 7.1 性能目标

| 指标 | 目标值 | 测量方法 |
|------|--------|----------|
| 启动时间 | ≤ 3s | Stopwatch |
| 内存占用 | ≤ 100MB | GC.GetTotalMemory |
| CPU 空闲 | ≤ 1% | PerformanceCounter |
| UI 响应 | ≤ 100ms | DispatcherTimer |

### 7.2 优化策略

#### 7.2.1 启动优化

```csharp
// 延迟加载非关键服务
protected override async void OnFrameworkInitializationCompleted()
{
    // 先显示界面
    base.OnFrameworkInitializationCompleted();
    
    // 后台加载服务
    _ = Task.Run(async () =>
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 延迟初始化
            InitializeServices();
        });
    });
}
```

#### 7.2.2 内存优化

```csharp
// 限制历史数据大小
public class ResponseHistoryBuffer
{
    private readonly int _maxSize = 1000;
    private readonly ConcurrentQueue<ResponseTimeRecord> _buffer = new();
    
    public void Add(ResponseTimeRecord record)
    {
        _buffer.Enqueue(record);
        
        while (_buffer.Count > _maxSize)
        {
            _buffer.TryDequeue(out _);
        }
    }
}
```

#### 7.2.3 UI 响应优化

```csharp
// 使用虚拟化列表
<ItemsControl ItemsSource="{Binding Sources}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>

// 异步加载
public async Task LoadDataAsync()
{
    IsLoading = true;
    
    var data = await Task.Run(() => _service.GetData());
    
    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        Sources.Clear();
        foreach (var item in data)
        {
            Sources.Add(item);
        }
    });
    
    IsLoading = false;
}
```

### 7.3 资源释放

```csharp
public class ResourceManager : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    
    public void Register(IDisposable resource)
    {
        _resources.Add(resource);
    }
    
    public void Dispose()
    {
        foreach (var resource in _resources)
        {
            resource.Dispose();
        }
        _resources.Clear();
    }
}
```

---

## 附录

### A. 类图总览

```
┌─────────────────────────────────────────────────────────────────┐
│                         Core Layer                               │
├─────────────────────────────────────────────────────────────────┤
│  GithubHostsService ◄── SourcePerformanceMonitor                │
│         │                       │                                │
│         │                       ▼                                │
│         │              SmartSourceSelector                       │
│         │                       │                                │
│         ▼                       ▼                                │
│  WindowsHostsFileService ◄── SourcePerformancePersistence       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                          UI Layer                                │
├─────────────────────────────────────────────────────────────────┤
│  MainWindowViewModel                                             │
│         │                                                        │
│         ├── DashboardViewModel                                   │
│         ├── SettingsViewModel                                    │
│         ├── PerformanceChartViewModel                            │
│         └── LogViewerViewModel                                   │
└─────────────────────────────────────────────────────────────────┘
```

### B. 设计决策记录

| ID | 决策 | 原因 | 日期 |
|----|------|------|------|
| D001 | 使用 Avalonia 而非 WPF | 跨平台支持 | 2026-04-01 |
| D002 | 自绘图表替代第三方库 | 兼容性问题 | 2026-04-20 |
| D003 | 使用 CommunityToolkit.Mvvm | 简化 MVVM 实现 | 2026-04-01 |
| D004 | JSON 存储而非数据库 | 简化部署 | 2026-04-01 |

---

**文档结束**
