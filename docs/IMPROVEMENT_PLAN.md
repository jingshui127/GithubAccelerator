# GitHub 加速器改进计划

## 一、项目概述

### 1.1 当前状态
- **技术栈**：.NET 10, Windows Forms, Spectre.Console CLI
- **核心功能**：Hosts 管理、IP 测速优选、多数据源备份、自动刷新
- **架构**：服务层 + UI 层分离

### 1.2 参考项目分析

| 项目名称 | Stars | 核心技术 | 可借鉴亮点 |
|---------|-------|---------|-----------|
| GitHub520 | 28.5k | GitHub Actions | 自动化 IP 更新 |
| Watt Toolkit | 30k+ | .NET + Avalonia + YARP | 反向代理、跨平台 UI、插件化 |
| dev-sidecar | 15k+ | Electron | DNS 优选、多站点加速、npm 加速 |
| FastGithub | 10k+ | .NET + 智能 DNS | Git 全流程加速 |
| CloudflareSpeedTest | 8k+ | Go | CDN IP 测速算法 |

---

## 二、改进目标

### 2.1 短期目标（2 周内）
1. 完善 IP 测速优选算法
2. 实现 Git 操作加速代理
3. 优化 CLI 交互体验

### 2.2 中期目标（1 个月内）
1. 实现 Windows 窗体 UI 全面优化
2. 实现系统托盘支持
3. 实现开机自启功能

### 2.3 长期目标（3 个月内）
1. 跨平台支持（macOS/Linux）
2. 插件化架构
3. 浏览器扩展支持

---

## 三、详细改进方案

### 3.1 核心功能增强

#### 3.1.1 IP 测速优选优化
**当前状态**：✅ 已实现基础功能

**优化方向**：
1. 增加 TCP 端口测速（443 端口连通性）
2. 增加下载速度测试
3. 增加历史延迟记录
4. 支持自定义测速参数

**实现方案**：
```csharp
// 新增 TCP 测速
public async Task<bool> TestTcpPortAsync(string ip, int port = 443, int timeout = 3000)
{
    using var client = new TcpClient();
    try
    {
        await client.ConnectAsync(ip, port);
        return client.Connected;
    }
    catch
    {
        return false;
    }
}

// 新增下载速度测试
public async Task<double> TestDownloadSpeedAsync(string ip, string domain)
{
    // 测试小文件下载速度
}
```

#### 3.1.2 多数据源扩展
**当前状态**：✅ 已有主备两个数据源

**优化方向**：
1. 增加更多数据源（至少 5 个）
2. 实现数据源质量评分
3. 自动选择最优数据源

**数据源列表**：
- `https://raw.hellogithub.com/hosts` (主)
- `https://raw.githubusercontent.com/521xueweihan/GitHub520/main/hosts` (备 1)
- `https://raw.gitmirror.com/521xueweihan/GitHub520/main/hosts` (备 2)
- `https://ghproxy.net/https://raw.githubusercontent.com/521xueweihan/GitHub520/main/hosts` (备 3)
- `https://mirror.ghproxy.com/https://raw.githubusercontent.com/521xueweihan/GitHub520/main/hosts` (备 4)

#### 3.1.3 自动刷新策略优化
**当前状态**：✅ 每 2 小时自动刷新

**优化方向**：
1. 支持自定义刷新间隔
2. 智能刷新（网络变化时触发）
3. 刷新失败退避策略

---

### 3.2 Git 操作加速代理

**目标**：参考 FastGithub 实现 Git 全流程加速

#### 3.2.1 本地 HTTP 代理
```csharp
public class GithubProxyService
{
    private const int ProxyPort = 8888;
    private HttpListener? _listener;

    public async Task StartProxyAsync(CancellationToken token)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{ProxyPort}/");
        _listener.Start();

        while (!token.IsCancellationRequested)
        {
            var context = await _listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context), token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        // 转发请求到 GitHub
        // 使用优选 IP
        // 返回响应
    }
}
```

#### 3.2.2 Git 配置自动设置
```csharp
public async Task ConfigureGitProxyAsync()
{
    // git config --global http.proxy http://localhost:8888
    // git config --global https.proxy http://localhost:8888
}
```

---

### 3.3 Windows 窗体 UI 优化

**目标**：参考 Watt Toolkit 现代化设计

#### 3.3.1 界面布局重构
1. 采用侧边栏导航
2. 主内容区卡片式布局
3. 状态栏实时显示

#### 3.3.2 视觉设计
1. 深色/浅色主题切换
2. 现代化图标和动画
3. 进度条和状态指示器

#### 3.3.3 功能模块
1. **首页**：加速状态、一键开关
2. **Hosts 管理**：预览、应用、恢复
3. **测速中心**：IP 列表、延迟图表
4. **设置**：数据源、刷新间隔、代理配置
5. **日志**：操作记录、错误信息

---

### 3.4 系统托盘支持

**实现方案**：
```csharp
public class TrayService
{
    private NotifyIcon? _trayIcon;

    public void Initialize()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "GitHub 加速器",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("启用加速", null, OnEnableClick);
        menu.Items.Add("禁用加速", null, OnDisableClick);
        menu.Items.Add("退出", null, OnExitClick);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += OnDoubleClick;
    }
}
```

---

### 3.5 开机自启功能

**实现方案**：
```csharp
public class AutoStartService
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GithubAccelerator";

    public void EnableAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        key?.SetValue(AppName, Application.ExecutablePath);
    }

    public void DisableAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        key?.DeleteValue(AppName, false);
    }

    public bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
        return key?.GetValue(AppName) != null;
    }
}
```

---

## 四、技术实现路径

### 4.1 架构设计

```
GithubAccelerator/
├── Core/                    # 核心服务层
│   ├── Services/
│   │   ├── GithubHostsService.cs
│   │   ├── HostsFileService.cs
│   │   ├── IpSpeedTestService.cs
│   │   ├── GithubConnectionService.cs
│   │   ├── GithubProxyService.cs          # 新增
│   │   ├── AutoStartService.cs            # 新增
│   │   └── TrayService.cs                 # 新增
│   └── Models/
│       ├── HostsEntry.cs
│       ├── IpSpeedTestResult.cs
│       └── ProxyConfig.cs                 # 新增
├── UI/
│   ├── WinForms/                           # Windows 窗体
│   │   ├── Forms/
│   │   │   ├── MainForm.cs
│   │   │   ├── SettingsForm.cs
│   │   │   └── LogForm.cs
│   │   └── Controls/
│   │       ├── StatusCard.cs
│   │       └── SpeedChart.cs
│   └── Cli/                                # 控制台
│       └── Program.cs
└── Tests/
    ├── UnitTests.cs
    └── IntegrationTests.cs
```

### 4.2 依赖项

```xml
<!-- 新增依赖 -->
<PackageReference Include="System.Net.Http" Version="9.0.0" />
<PackageReference Include="Microsoft.Win32.Registry" Version="9.0.0" />
<PackageReference Include="LiveChartsCore" Version="2.0.0" />  <!-- 图表 -->
<PackageReference Include="MaterialSkin.2" Version="2.3.1" />  <!-- Material Design -->
```

---

## 五、资源需求评估

### 5.1 开发资源
- **开发人员**：1-2 名 .NET 开发者
- **设计资源**：UI/UX 设计师（可选）
- **测试资源**：自动化测试 + 手动测试

### 5.2 技术资源
- **.NET 10 SDK**
- **Visual Studio 2022 / VS Code**
- **GitHub API 访问权限**

### 5.3 时间资源
| 阶段 | 时间 | 交付物 |
|-----|------|-------|
| 阶段一 | 2 周 | IP 测速优化、Git 代理 |
| 阶段二 | 2 周 | UI 优化、系统托盘 |
| 阶段三 | 2 周 | 开机自启、设置中心 |
| 阶段四 | 2 周 | 测试、文档、发布 |

---

## 六、进度跟踪机制

### 6.1 里程碑
1. **M1**（第 2 周）：核心功能增强完成
2. **M2**（第 4 周）：UI 优化完成
3. **M3**（第 6 周）：全部功能完成
4. **M4**（第 8 周）：测试发布

### 6.2 评估指标
- **功能完成率**：计划功能实现比例
- **代码覆盖率**：单元测试覆盖率 > 80%
- **用户满意度**：连接成功率 > 95%
- **性能指标**：启动时间 < 3 秒，内存占用 < 100MB

### 6.3 反馈机制
- 每周进度回顾
- 问题跟踪（GitHub Issues）
- 用户反馈收集

---

## 七、风险与应对

### 7.1 技术风险
| 风险 | 影响 | 应对措施 |
|-----|------|---------|
| 数据源失效 | 高 | 多数据源备份、本地缓存 |
| IP 被封禁 | 中 | 动态 IP 切换、测速优选 |
| 系统兼容性 | 中 | 多环境测试、降级方案 |

### 7.2 进度风险
| 风险 | 影响 | 应对措施 |
|-----|------|---------|
| 功能延期 | 中 | 优先级排序、分阶段发布 |
| 测试不足 | 高 | 自动化测试、CI/CD |

---

## 八、分阶段实施步骤

### 阶段一：核心功能增强（第 1-2 周）

**Week 1**:
- [x] 实现 IP 测速优选服务
- [x] 实现多数据源备份
- [x] 实现定期自动刷新
- [ ] 优化测速算法（TCP + 下载）
- [ ] 增加数据源质量评分

**Week 2**:
- [ ] 实现 Git HTTP 代理
- [ ] 实现 Git 配置自动设置
- [ ] CLI 交互体验优化
- [ ] 编写单元测试

### 阶段二：UI 优化（第 3-4 周）

**Week 3**:
- [ ] 设计新界面布局
- [ ] 实现侧边栏导航
- [ ] 实现状态卡片组件
- [ ] 实现深色/浅色主题

**Week 4**:
- [ ] 实现测速图表
- [ ] 实现设置页面
- [ ] 实现日志页面
- [ ] UI 测试和优化

### 阶段三：系统功能（第 5-6 周）

**Week 5**:
- [ ] 实现系统托盘
- [ ] 实现开机自启
- [ ] 实现后台服务运行

**Week 6**:
- [ ] 实现配置持久化
- [ ] 实现自动更新检查
- [ ] 集成测试

### 阶段四：测试发布（第 7-8 周）

**Week 7**:
- [ ] 全面功能测试
- [ ] 性能优化
- [ ] 编写用户文档

**Week 8**:
- [ ] 修复问题
- [ ] 打包发布
- [ ] 收集用户反馈

---

## 九、总结

本改进计划基于对 5 个优质开源项目的深入分析，结合当前 GitHub 加速器的实际情况，制定了从核心功能增强到 UI 优化、系统功能完善的全面改进方案。

通过分阶段实施，确保每个阶段都有明确的交付物和评估指标，同时建立进度跟踪和反馈机制，保证改进计划有序推进。

**核心优势**：
1. **IP 测速优选**：确保使用最优 IP
2. **多数据源备份**：高可用性保障
3. **自动刷新**：保持 Hosts 最新
4. **Git 代理加速**：全流程加速
5. **现代化 UI**：提升用户体验

**预期效果**：
- GitHub 访问成功率 > 95%
- 平均延迟降低 50%+
- 用户操作简化 70%+
