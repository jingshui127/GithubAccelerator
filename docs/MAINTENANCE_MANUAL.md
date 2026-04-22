# GitHub 加速器 Pro - 维护手册 (MM)

**文档编号**: MM-2026-001  
**版本**: v1.0.0  
**创建日期**: 2026-04-22  
**最后更新**: 2026-04-22  
**适用版本**: v1.1.0 及以上  

---

## 目录

1. [维护概述](#1-维护概述)
2. [系统架构](#2-系统架构)
3. [配置管理](#3-配置管理)
4. [日志管理](#4-日志管理)
5. [故障诊断](#5-故障诊断)
6. [性能调优](#6-性能调优)
7. [备份恢复](#7-备份恢复)
8. [升级指南](#8-升级指南)
9. [安全维护](#9-安全维护)

---

## 1. 维护概述

### 1.1 维护目标

- 确保应用程序稳定运行
- 快速定位和解决问题
- 优化系统性能
- 保护数据安全

### 1.2 维护类型

| 类型 | 频率 | 内容 |
|------|------|------|
| 日常维护 | 每日 | 检查日志、监控状态 |
| 定期维护 | 每周 | 清理日志、备份数据 |
| 版本升级 | 按需 | 更新版本、迁移数据 |
| 故障处理 | 按需 | 问题诊断、修复 |

### 1.3 维护工具

| 工具 | 用途 |
|------|------|
| 事件查看器 | 查看系统事件 |
| 任务管理器 | 监控资源使用 |
| Process Monitor | 进程监控 |
| Wireshark | 网络抓包分析 |
| dotnet-dump | 内存分析 |

---

## 2. 系统架构

### 2.1 组件架构

```
┌─────────────────────────────────────────────────────────────┐
│                    GitHub 加速器 Pro                         │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────┐   │
│  │                  UI Layer (Avalonia)                 │   │
│  │  Views │ ViewModels │ Converters │ Controls         │   │
│  └───────────────────────────┬─────────────────────────┘   │
│                              │                              │
│  ┌───────────────────────────▼─────────────────────────┐   │
│  │                  Service Layer                       │   │
│  │  GithubHostsService │ PerformanceMonitor │ ...      │   │
│  └───────────────────────────┬─────────────────────────┘   │
│                              │                              │
│  ┌───────────────────────────▼─────────────────────────┐   │
│  │                  Data Layer                          │   │
│  │  JSON Persistence │ Configuration │ Cache           │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 数据流

```
用户操作 → ViewModel → Service → 数据存储
                ↓
           网络请求 → 数据源 API
                ↓
           性能监控 → 持久化
```

### 2.3 关键进程

| 进程 | 说明 | 资源占用 |
|------|------|----------|
| GithubAccelerator.UI.exe | 主程序 | 50-100MB |
| dotnet.exe | .NET 运行时 | 20-50MB |

---

## 3. 配置管理

### 3.1 配置文件位置

```
%AppData%\GithubAccelerator\
├── config.json              # 主配置文件
├── sources.json             # 数据源配置
├── data\
│   ├── performance.json     # 性能数据
│   └── history.json         # 历史记录
└── logs\
    ├── app-{date}.log       # 应用日志
    └── error-{date}.log     # 错误日志
```

### 3.2 主配置文件 (config.json)

```json
{
  "version": "1.1.0",
  "autoStart": true,
  "startMinimized": false,
  "autoMonitor": true,
  "refreshInterval": 300,
  "timeout": 5000,
  "isDarkMode": true,
  "language": "zh-CN",
  "advanced": {
    "maxConcurrentTests": 10,
    "historyRetentionDays": 30,
    "logLevel": "Information",
    "enableTelemetry": false
  }
}
```

### 3.3 配置项说明

| 配置项 | 类型 | 说明 | 默认值 |
|--------|------|------|--------|
| version | string | 配置版本号 | "1.1.0" |
| autoStart | bool | 开机自启 | false |
| startMinimized | bool | 启动最小化 | false |
| autoMonitor | bool | 自动监控 | true |
| refreshInterval | int | 刷新间隔(秒) | 300 |
| timeout | int | 请求超时(ms) | 5000 |
| isDarkMode | bool | 深色主题 | true |
| language | string | 界面语言 | "zh-CN" |

### 3.4 高级配置

```json
{
  "advanced": {
    "maxConcurrentTests": 10,      // 最大并发测速数
    "historyRetentionDays": 30,     // 历史数据保留天数
    "logLevel": "Information",      // 日志级别
    "enableTelemetry": false,       // 是否启用遥测
    "customSources": [              // 自定义数据源
      {
        "name": "Custom Source",
        "url": "https://example.com/hosts",
        "enabled": true
      }
    ]
  }
}
```

### 3.5 配置修改方法

#### 方法一：通过界面修改

1. 打开应用程序
2. 点击"设置"页面
3. 修改相应选项
4. 点击"保存"

#### 方法二：手动编辑文件

1. 关闭应用程序
2. 用文本编辑器打开 `config.json`
3. 修改配置项
4. 保存文件
5. 重新启动应用程序

### 3.6 配置验证

应用程序启动时会验证配置文件，如发现错误会：
1. 记录错误日志
2. 使用默认配置
3. 提示用户配置异常

---

## 4. 日志管理

### 4.1 日志位置

```
%AppData%\GithubAccelerator\logs\
├── app-20260422.log         # 应用日志
├── error-20260422.log       # 错误日志
└── perf-20260422.log        # 性能日志
```

### 4.2 日志级别

| 级别 | 说明 | 示例 |
|------|------|------|
| Trace | 详细跟踪 | 方法进入/退出 |
| Debug | 调试信息 | 变量值、状态 |
| Information | 一般信息 | 操作记录 |
| Warning | 警告信息 | 可恢复的异常 |
| Error | 错误信息 | 需要关注的错误 |
| Critical | 严重错误 | 系统崩溃 |

### 4.3 日志格式

```
[时间] [级别] [类名] 消息内容
异常堆栈（如有）

示例:
[2026-04-22 12:00:00] [INF] [GithubHostsService] 开始获取 Hosts 数据
[2026-04-22 12:00:01] [WRN] [GithubHostsService] 数据源响应超时: https://source1.com
[2026-04-22 12:00:02] [ERR] [GithubHostsService] 获取 Hosts 失败
System.Net.Http.HttpRequestException: 网络连接失败
   at GithubAccelerator.Core.Services.GithubHostsService.FetchAsync()
```

### 4.4 日志配置

在 `appsettings.json` 中配置日志：

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "System": "Warning",
        "Microsoft": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "%AppData%\\GithubAccelerator\\logs\\app-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ]
  }
}
```

### 4.5 日志分析

#### 常见错误模式

| 错误模式 | 可能原因 | 解决方案 |
|----------|----------|----------|
| `HttpRequestException` | 网络问题 | 检查网络连接 |
| `UnauthorizedAccessException` | 权限不足 | 以管理员运行 |
| `TimeoutException` | 请求超时 | 增加超时时间 |
| `JsonException` | 数据格式错误 | 检查数据源 |

#### 日志查询命令

```powershell
# 查找错误日志
Select-String -Path "logs\app-*.log" -Pattern "\[ERR\]"

# 查找特定时间段的日志
Select-String -Path "logs\app-*.log" -Pattern "2026-04-22 12:"

# 统计错误数量
(Select-String -Path "logs\app-*.log" -Pattern "\[ERR\]").Count
```

### 4.6 日志清理

日志文件默认保留 7 天，可通过配置修改：

```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

手动清理：

```powershell
# 删除 30 天前的日志
Get-ChildItem -Path "logs" -Filter "*.log" | 
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | 
    Remove-Item
```

---

## 5. 故障诊断

### 5.1 故障分类

| 类别 | 现象 | 优先级 |
|------|------|--------|
| 启动失败 | 应用无法启动 | P0 |
| 功能异常 | 功能不工作 | P1 |
| 性能问题 | 响应缓慢 | P2 |
| 界面问题 | 显示异常 | P3 |

### 5.2 诊断流程

```
故障发生
    │
    ▼
检查日志 ──无错误──▶ 检查配置 ──正确──▶ 检查环境
    │                    │                │
    │有错误              │错误            │异常
    ▼                    ▼                ▼
分析错误日志         修复配置         修复环境
    │                    │                │
    └────────────────────┴────────────────┘
                         │
                         ▼
                     问题解决？
                    /        \
                  是          否
                  │            │
                  ▼            ▼
               恢复服务    联系支持
```

### 5.3 常见故障处理

#### 5.3.1 应用无法启动

**症状**: 双击图标无反应或闪退

**诊断步骤**:
1. 检查 .NET Runtime 是否安装
2. 检查事件查看器中的错误
3. 尝试命令行启动查看错误

**解决方案**:
```powershell
# 检查 .NET 版本
dotnet --list-runtimes

# 命令行启动
cd "C:\Program Files\GithubAccelerator"
.\GithubAccelerator.UI.exe
```

#### 5.3.2 Hosts 应用失败

**症状**: 点击应用后提示失败

**诊断步骤**:
1. 检查是否以管理员身份运行
2. 检查 Hosts 文件是否被占用
3. 检查磁盘空间

**解决方案**:
```powershell
# 检查 Hosts 文件权限
icacls C:\Windows\System32\drivers\etc\hosts

# 检查文件是否被锁定
openfiles /query /v | findstr hosts

# 手动测试写入
echo "127.0.0.1 test.local" >> C:\Windows\System32\drivers\etc\hosts
```

#### 5.3.3 数据源全部超时

**症状**: 所有数据源显示超时

**诊断步骤**:
1. 检查网络连接
2. 检查防火墙设置
3. 检查代理设置

**解决方案**:
```powershell
# 测试网络连接
Test-NetConnection github.com -Port 443

# 检查代理设置
netsh winhttp show proxy

# 重置网络
netsh winsock reset
netsh int ip reset
```

#### 5.3.4 内存占用过高

**症状**: 内存占用超过 200MB

**诊断步骤**:
1. 检查历史数据大小
2. 检查是否有内存泄漏
3. 使用 dotnet-dump 分析

**解决方案**:
```powershell
# 使用 dotnet-dump 分析
dotnet tool install -g dotnet-dump
dotnet-dump collect -p <PID>
dotnet-dump analyze dump.dmp

# 查看内存对象
> dumpheap -stat
> gcroot <object_address>
```

### 5.4 诊断工具使用

#### 5.4.1 Process Monitor

1. 下载并运行 Process Monitor
2. 设置过滤器：`Process Name is GithubAccelerator.UI.exe`
3. 观察文件、注册表、网络操作

#### 5.4.2 Wireshark

1. 启动 Wireshark 抓包
2. 设置过滤器：`http.host contains "github"`
3. 分析请求响应

#### 5.4.3 dotnet-counters

```powershell
# 安装工具
dotnet tool install -g dotnet-counters

# 监控性能计数器
dotnet-counters monitor -p <PID>

# 导出数据
dotnet-counters collect -p <PID> -o counters.csv
```

---

## 6. 性能调优

### 6.1 性能指标

| 指标 | 目标值 | 警告值 | 危险值 |
|------|--------|--------|--------|
| 启动时间 | < 3s | 3-5s | > 5s |
| 内存占用 | < 100MB | 100-200MB | > 200MB |
| CPU 占用(空闲) | < 1% | 1-5% | > 5% |
| UI 响应 | < 100ms | 100-500ms | > 500ms |

### 6.2 性能优化建议

#### 6.2.1 启动优化

```json
{
  "advanced": {
    "lazyLoadServices": true,    // 延迟加载服务
    "preloadData": false,        // 不预加载数据
    "disableAnimations": false   // 保留动画
  }
}
```

#### 6.2.2 内存优化

```json
{
  "advanced": {
    "maxHistoryItems": 500,      // 限制历史数据
    "cacheExpiration": 300,      // 缓存过期时间
    "gcMode": "workstation"      // GC 模式
  }
}
```

#### 6.2.3 网络优化

```json
{
  "advanced": {
    "connectionPoolSize": 10,    // 连接池大小
    "maxConcurrentRequests": 5,  // 最大并发请求
    "enableCompression": true    // 启用压缩
  }
}
```

### 6.3 性能监控脚本

```powershell
# 性能监控脚本
param(
    [int]$Interval = 60,
    [int]$Duration = 3600
)

$process = Get-Process -Name "GithubAccelerator.UI" -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Error "进程未运行"
    exit
}

$iterations = $Duration / $Interval
$results = @()

for ($i = 0; $i -lt $iterations; $i++) {
    $process = Get-Process -Name "GithubAccelerator.UI"
    $cpu = $process.CPU
    $memory = $process.WorkingSet64 / 1MB
    
    $results += [PSCustomObject]@{
        Time = Get-Date -Format "HH:mm:ss"
        CPU = "{0:N2}%" -f $cpu
        Memory = "{0:N2} MB" -f $memory
    }
    
    Start-Sleep -Seconds $Interval
}

$results | Format-Table -AutoSize
```

---

## 7. 备份恢复

### 7.1 备份策略

| 备份类型 | 频率 | 内容 | 保留期限 |
|----------|------|------|----------|
| 配置备份 | 每次修改 | config.json | 10 份 |
| Hosts 备份 | 每次应用 | hosts 文件 | 10 份 |
| 数据备份 | 每日 | 性能数据 | 30 天 |
| 完整备份 | 每周 | 全部数据 | 90 天 |

### 7.2 手动备份

```powershell
# 备份脚本
$backupPath = "D:\Backups\GithubAccelerator"
$dataPath = "$env:APPDATA\GithubAccelerator"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

# 创建备份目录
New-Item -ItemType Directory -Path $backupPath -Force

# 备份配置和数据
Compress-Archive -Path "$dataPath\*" -DestinationPath "$backupPath\backup_$timestamp.zip"

Write-Host "备份完成: $backupPath\backup_$timestamp.zip"
```

### 7.3 恢复操作

```powershell
# 恢复脚本
param(
    [string]$BackupFile
)

$dataPath = "$env:APPDATA\GithubAccelerator"

# 解压备份
Expand-Archive -Path $BackupFile -DestinationPath $dataPath -Force

Write-Host "恢复完成"
```

### 7.4 自动备份配置

在任务计划程序中创建自动备份任务：

```xml
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2">
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>2026-04-22T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
      <ScheduleByWeek>
        <DaysOfWeek>
          <Sunday />
        </DaysOfWeek>
        <WeeksInterval>1</WeeksInterval>
      </ScheduleByWeek>
    </CalendarTrigger>
  </Triggers>
  <Actions Context="Author">
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-File "D:\Scripts\Backup-GithubAccelerator.ps1"</Arguments>
    </Exec>
  </Actions>
</Task>
```

---

## 8. 升级指南

### 8.1 版本兼容性

| 当前版本 | 目标版本 | 兼容性 | 迁移操作 |
|----------|----------|--------|----------|
| 1.0.x | 1.1.x | 兼容 | 无需迁移 |
| 1.x.x | 2.0.x | 部分兼容 | 需迁移配置 |

### 8.2 升级前准备

1. **备份数据**
   ```powershell
   # 备份当前配置和数据
   Compress-Archive -Path "$env:APPDATA\GithubAccelerator\*" `
       -DestinationPath "D:\Backups\pre-upgrade.zip"
   ```

2. **记录当前版本**
   ```powershell
   # 查看当前版本
   Get-Content "$env:APPDATA\GithubAccelerator\config.json" | 
       Select-String "version"
   ```

3. **检查系统要求**
   - 确认 .NET 版本满足要求
   - 确认磁盘空间充足

### 8.3 升级步骤

#### 安装版升级

1. 下载新版本安装包
2. 运行安装程序（会自动覆盖）
3. 启动应用程序
4. 验证功能正常

#### 便携版升级

1. 备份当前目录
2. 下载新版本并解压
3. 复制 `config.json` 和 `data` 目录到新目录
4. 运行新版本

### 8.4 配置迁移

如需手动迁移配置：

```powershell
# 迁移脚本
$oldConfig = Get-Content "old_config.json" | ConvertFrom-Json
$newConfig = @{
    version = "1.1.0"
    autoStart = $oldConfig.autoStart
    startMinimized = $oldConfig.startMinimized
    # ... 其他配置项
}

$newConfig | ConvertTo-Json | Set-Content "config.json"
```

### 8.5 回滚操作

如升级后出现问题：

1. 卸载新版本
2. 安装旧版本
3. 恢复备份的配置和数据

```powershell
# 回滚脚本
param(
    [string]$BackupFile
)

# 停止应用
Stop-Process -Name "GithubAccelerator.UI" -Force

# 恢复备份
Expand-Archive -Path $BackupFile -DestinationPath "$env:APPDATA\GithubAccelerator" -Force

Write-Host "回滚完成"
```

---

## 9. 安全维护

### 9.1 安全检查清单

| 检查项 | 频率 | 说明 |
|--------|------|------|
| 权限配置 | 每月 | 确认文件权限正确 |
| 日志审计 | 每周 | 检查异常访问 |
| 配置验证 | 每次修改 | 确认无敏感信息泄露 |
| 依赖更新 | 每月 | 更新安全补丁 |

### 9.2 权限配置

```powershell
# 检查配置文件权限
icacls "$env:APPDATA\GithubAccelerator\config.json"

# 设置正确权限
icacls "$env:APPDATA\GithubAccelerator\config.json" /inheritance:r
icacls "$env:APPDATA\GithubAccelerator\config.json" /grant:r "$env:USERNAME:(R,W)"
```

### 9.3 敏感信息检查

```powershell
# 检查配置文件中的敏感信息
Select-String -Path "$env:APPDATA\GithubAccelerator\*.json" `
    -Pattern "password|token|secret|key" -CaseSensitive
```

### 9.4 安全加固

1. **禁用遥测**（如不需要）
   ```json
   {
     "advanced": {
       "enableTelemetry": false
     }
   }
   ```

2. **限制日志级别**
   ```json
   {
     "Serilog": {
       "MinimumLevel": "Warning"
     }
   }
   ```

3. **定期清理日志**
   ```json
   {
     "Serilog": {
       "WriteTo": [{
         "Args": {
           "retainedFileCountLimit": 7
         }
       }]
     }
   }
   ```

---

## 附录

### A. 命令行参数

| 参数 | 说明 |
|------|------|
| `--minimized` | 启动时最小化 |
| `--no-monitor` | 不自动开始监控 |
| `--config <path>` | 指定配置文件路径 |
| `--log-level <level>` | 设置日志级别 |
| `--version` | 显示版本信息 |
| `--help` | 显示帮助信息 |

### B. 环境变量

| 变量 | 说明 |
|------|------|
| `GITHUB_ACCELERATOR_CONFIG` | 配置文件路径 |
| `GITHUB_ACCELERATOR_LOG` | 日志目录 |
| `GITHUB_ACCELERATOR_DATA` | 数据目录 |

### C. 注册表项

```
HKEY_CURRENT_USER\Software\GithubAccelerator
├── InstallPath        # 安装路径
├── Version            # 版本号
└── AutoStart          # 开机自启

HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
└── GithubAccelerator  # 自启动项
```

### D. 维护检查脚本

```powershell
# 完整维护检查脚本
function Invoke-MaintenanceCheck {
    $results = @()
    
    # 检查进程状态
    $process = Get-Process -Name "GithubAccelerator.UI" -ErrorAction SilentlyContinue
    $results += [PSCustomObject]@{
        Check = "进程状态"
        Status = if ($process) { "运行中" } else { "未运行" }
    }
    
    # 检查配置文件
    $configPath = "$env:APPDATA\GithubAccelerator\config.json"
    $results += [PSCustomObject]@{
        Check = "配置文件"
        Status = if (Test-Path $configPath) { "存在" } else { "缺失" }
    }
    
    # 检查日志大小
    $logSize = (Get-ChildItem "$env:APPDATA\GithubAccelerator\logs" -Recurse | 
                Measure-Object -Property Length -Sum).Sum / 1MB
    $results += [PSCustomObject]@{
        Check = "日志大小"
        Status = "{0:N2} MB" -f $logSize
    }
    
    # 检查磁盘空间
    $disk = Get-PSDrive C
    $results += [PSCustomObject]@{
        Check = "磁盘空间"
        Status = "{0:N2} GB" -f ($disk.Free / 1GB)
    }
    
    return $results | Format-Table -AutoSize
}

Invoke-MaintenanceCheck
```

---

**文档结束**
