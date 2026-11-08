# GitHub 加速器 Pro

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6.svg)](https://www.microsoft.com/windows)
[![Version](https://img.shields.io/badge/version-1.6.0-brightgreen.svg)](CHANGELOG.md)

🚀 一款智能的 GitHub 访问加速工具，通过优化 Hosts 文件实现快速稳定的 GitHub 访问。

## ✨ 功能特性

- 🔥 **一键加速** - 自动获取并应用最优 Hosts 记录
- 🌐 **多源 DNS 探测** - DoH 并行查询 AliDNS/TencentDNS/Google/Cloudflare
- 📊 **性能监控** - 实时监控多个数据源的性能指标
- 🔄 **智能切换** - 根据健康评分自动选择最优数据源
- 🩺 **子域健康检查** - 并行探测 GitHub 关键子域可达性
- ⚡ **下载加速** - GitHub URL 镜像转换（ghproxy、jsDelivr 等）
- 📈 **可视化图表** - 直观展示延迟数据和趋势
- 🌙 **主题切换** - 支持深色/浅色主题
- 💾 **数据备份** - 配置和数据安全备份
- 🔔 **系统托盘** - 最小化到托盘，后台运行
- ⚡ **开机自启** - Windows 开机自动启动

## 📸 截图

![主界面](docs/images/ui-main-interface.png)
![性能图表](docs/images/performance-curve.png)
![延迟监控](docs/images/latency-curve.png)

## 🚀 快速开始

### 系统要求

- Windows 10 1803+ 或 Windows 11
- .NET 10 Runtime

### 安装

1. 从 [Releases](https://github.com/jingshui127/GithubAccelerator/releases) 下载最新版本
2. 解压到任意目录
3. 正常双击即可运行，**不需要**默认以管理员身份启动；只有判定通过、确实要写 Hosts 时才会按需请求一次管理员授权

### 使用

1. 启动应用程序（可直接双击，不必先提权）
2. 应用会自动探测加速线路；主界面「① 能不能用」会直接给出当前是原始直连还是已加速
3. 点「🚀 开启网页加速」：先拿将要写入的 `github.com` 候选与直连交错配对，候选没有超过直连 20% 时不写入 Hosts，并明确提示「当前网络换 IP 改善不了」，全程不弹 UAC；达标后才请求管理员权限写入。判定分快判/慢判：先 2 组初判（通常几秒），明显不达标提前结束，接近临界才继续完整配对，进度可见、可取消。主按钮、来源卡片、分组应用、CLI 与自动切换都走同一道判定
4. 到「检查与修复」页确认依赖站点与官网诊断结论
5. 如需下载加速，打开「下载加速器」页生成镜像链接

## 📖 文档

| 文档 | 说明 |
|------|------|
| [变更日志](CHANGELOG.md) | 版本更新历史 |
| [用户手册](docs/USER_MANUAL.md) | Markdown 格式的安装和使用指南 |
| [项目计划](docs/PROJECT_PLAN.md) | 项目背景、目标、进度安排 |
| [需求规格说明书](docs/SRS.md) | 功能需求和非功能需求 |
| [设计文档](docs/SDD.md) | 架构设计和模块设计 |

## 🏗️ 项目结构

```
GithubAccelerator/
├── GithubAccelerator.Core/       # 核心业务层（服务、模型）
├── GithubAccelerator.UI/         # Avalonia UI 界面（主项目）
├── GithubAccelerator.Cli/        # CLI 命令行工具
├── GithubAccelerator.Tests/      # 单元测试项目
├── docs/                         # 项目文档
└── CHANGELOG.md                  # 变更日志
```

## 🛠️ 技术栈

- **语言**: C# 13
- **框架**: .NET 10
- **UI 框架**: Avalonia 12.1 + Semi.Avalonia
- **MVVM**: CommunityToolkit.Mvvm 8.4
- **日志**: Serilog + NewLife.Core
- **测试**: xUnit

## 🔧 开发

### 环境准备

```bash
# 安装 .NET SDK
winget install Microsoft.DotNet.SDK.10

# 克隆仓库
git clone https://github.com/jingshui127/GithubAccelerator.git
cd GithubAccelerator

# 还原依赖
dotnet restore

# 构建项目
dotnet build

# 运行测试
dotnet test
```

### 运行

```bash
# 运行 UI 版本（推荐）
dotnet run --project GithubAccelerator.UI

# 运行 CLI 版本
dotnet run --project GithubAccelerator.Cli
```

## 📝 变更日志

查看 [CHANGELOG.md](CHANGELOG.md) 了解版本更新历史（当前版本 v1.6.7）。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'feat: add some feature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

## 📧 联系方式

- QQ: 2492123056
- 微信公众号：科控物联

## 📄 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件。

## 🙏 致谢

- [FastGit](https://fastgit.org/) - 数据源支持
- [GitHub520](https://github.com/521xueweihan/GitHub520) - 数据源支持
- [Avalonia](https://avaloniaui.net/) - UI 框架
- [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) - 主题组件

---

**⭐ 如果这个项目对你有帮助，请给一个 Star！**
