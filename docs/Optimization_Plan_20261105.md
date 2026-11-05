# GitHub 加速器 Pro — 易用性与功能增强方案（2026-11-05）

> 本文只做评审与方案，不含代码改动、未提交、未推送。所有结论基于实读代码 + 一次 Release 构建 + 一次非管理员启动实测。
> 编号（P0-x / P1-x / P2-x）用于逐条确认或驳回；确认后可按编号分批实施、每批单独提交。

---

## 一、总体判断

1. **产品能力不弱，但"拿到就能用"这条链断在 4 个地方**：下载渠道（发布在 GitHub Releases，而这正是被墙的站点）、管理员权限（要求用户右键以管理员身份运行，无自动提权）、首次使用（4 步文字指南而不是引导）、失败处理（错误是技术术语，没有可执行下一步）。
2. **当前"加速"成功路径的交互成本是 8~10 次操作 + 1 次 UAC**，且其中"智能优选"要等待不可预期的探测时间（多域名 × 多候选 IP × 3 次探针，串行受 12 并发限制），期间没有进度和取消。
3. **界面上的功能多于实际能力**：`CloseToTray`、`StartMinimized`、`ShowTrayNotifications` 三个开关会被持久化/导出，但代码中从未被消费，属于"假开关"，会直接损害信任。
4. **有 3 处会真实影响用户网络/数据**：备份恢复语义错误（恢复的内容会被包进加速标记，之后"清除"会连带删掉用户原内容）、清除 Hosts 非原子写、卸载后 Hosts 块与 Git 全局代理残留无提示。
5. **Git 加速当前是"看起来有、实际会坏"**：把 `https.proxy` 设成 `https://ghproxy.net`（一个 HTTP 反向代理地址），Git 会把它当 HTTP 代理发 CONNECT，正常 clone/pull 大概率失败。这是功能缺口里优先级最高的一条。
6. **加速范围只覆盖了浏览器访问 GitHub**：`ghcr.io`、`pkg.github.com`、`codeload`、`gist` 等容器/代码拉取场景要么完全没覆盖，要么只在出问题时才靠"子域健康检查"暴露。
7. **遥测默认开启、对用户不可见**：硬编码连 `http://47.113.219.65:6600`，每 60 秒上报进程 CPU/内存/线程/句柄，且没有任何设置项与首次告知。
8. **构建可复现性风险**：仓库无 `global.json`，本机默认用了 SDK `11.0.100-rc.3` 构建 net10 项目；`NewLife.Core` 指定的版本在还原时被降级（NU1603）。

### 实测记录（本机，2026-11-05）

| 项目 | 结果 |
|---|---|
| 构建 | `dotnet build GithubAccelerator.UI -c Release`：**0 error / 23 warning**。警告集中在可空引用、CA1416（`Microsoft.Win32.Registry` 跨平台提示）与 Avalonia `Watermark` 属性过时三类，均有具体文件行号见构建日志，未阻断构建 |
| SDK 解析 | 默认使用 `11.0.100-rc.3`（无 global.json），构建日志提示 `NETSDK1057` 预览版 |
| 包还原 | `NU1603`：`NewLife.Core >= 11.0.2026.717-beta2` 不存在，实际解析到 `11.1.2024.1205-beta0429` |
| 启动 | 非管理员双击可正常启动，窗口标题「GitHub 加速器 Pro」；日志 15:25:00 启动 → 15:25:04 ViewModel 初始化 → 15:25:04 完成 |
| 权限现状 | 日志明确记录「应用程序未以管理员身份运行，部分功能可能受限」，但**没有任何提权入口** |
| 首次引导 | 本次未弹出（本机 `settings.json` 里 `ShowQuickGuideOnStartup=false`，来自历史运行）；指南内容按 [QuickGuideWindow.axaml](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/QuickGuideWindow.axaml#L38-L120) 的 4 张卡片 + "不再显示"复选框（[#L173](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/QuickGuideWindow.axaml#L173)）核算 |
| 用户数据位置 | `%AppData%\GithubAccelerator\`：`settings.json`、`operation_history.json`、`Logs\log-YYYYMMDD.txt` |
| 发布形态 | `publish/GithubAccelerator.UI.exe` 单文件约 **146 MB**（自包含），无安装包、无签名、无更新检查 |

**未验证（本文明确标注）**：真实写入 Hosts（需要管理员，按纪律未执行）、UAC 自提权行为、Git 代理设置后的 clone 实际表现、各镜像站当前连通性、星尘服务器可达性、`dotnet test` 全量结果。

---

## 二、优先级表

| 编号 | 等级 | 问题（一句话） | 证据 | 影响面 | 改默认行为 | 工作量 |
|---|---|---|---|---|---|---|
| P0-1 | P0 | 必须手动"右键以管理员身份运行"，无自动/按需提权 | [app.manifest](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/app.manifest#L1-L17)、[CheckAdminPrivileges](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L416-L429) | 启动、所有写 Hosts 路径 | 是（见 §三-1） | 1 人日 |
| P0-2 | P0 | 没有"开始/停止加速"单一开关，停止入口是垃圾桶图标 | [DashboardView#L169-L172](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/DashboardView.axaml#L169-L172)、[#L207-L209](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/DashboardView.axaml#L207-L209) | 首页、状态机 | 是（首启自动加速） | 2–3 人日 |
| P0-3 | P0 | 首次使用是"读 4 步说明书"，不是引导浮层 | [QuickGuideWindow.axaml#L38-L120](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/QuickGuideWindow.axaml#L38-L120) | 首启、引导 | 否 | 2 人日 |
| P0-4 | P0 | 回滚/还原语义不安全：备份恢复会被包进加速标记；清除非原子；卸载无还原 | [HostsBackupService#L109-L117](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/HostsBackupService.cs#L109-L117)、[ClearAppliedHosts#L981-L1002](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L981-L1002) | Hosts 写入、备份、卸载 | 否 | 1.5 人日 |
| P0-5 | P0 | 分发不可达：发布在 GitHub Releases、146MB、无签名 | `publish/` 实测、[README#L37-L41](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L37-L41) | 发布/运营 | 否 | 2 人日（含渠道） |
| P0-6 | P0 | 遥测默认开启、无告知、无开关 | [StardustMonitorService#L29-L40](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/StardustMonitorService.cs#L29-L40)、[App.axaml.cs#L23](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/App.axaml.cs#L23) | 隐私、合规、信任 | 见 §三-6 | 0.5 人日 |
| P1-1 | P1 | Git 加速实现错误，会把 `http.proxy` 指向反向代理站 | [SettingsViewModel#L393-L400](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L393-L400)、[GitProxyService#L205-L235](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GitProxyService.cs#L205-L235) | Git 场景 | 是 | 2 人日 |
| P1-2 | P1 | 加速范围缺容器/包拉取：`ghcr.io`、`pkg.github.com` 未覆盖 | [ChinazDnsService#L74-L89](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/ChinazDnsService.cs#L74-L89)、[HostsOptimizerService#L34-L47](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L34-L47) | 域名清单、Hosts | 否 | 0.5 人日 |
| P1-3 | P1 | 失败后没有可执行自救路径，错误是技术术语 | [MainWindowViewModel#L1379](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1379) | 全失败分支 | 否 | 1.5 人日 |
| P1-4 | P1 | 智能优选耗时不可预期、无进度、不可取消 | [HostsOptimizerService#L216-L260](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L216-L260) | 首页主流程 | 否 | 1 人日 |
| P1-5 | P1 | 三个设置开关是假开关（持久化但无实现） | [SettingsViewModel#L53-L55](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L53-L55)、[MainWindow.axaml.cs#L79-L85](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L79-L85) | 设置、托盘 | 否 | 0.5–1 人日 |
| P1-6 | P1 | 无单实例保护，两个进程可同时改写 Hosts | [Program.cs#L13-L20](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Program.cs#L13-L20) | 启动、写入 | 否 | 0.5 人日 |
| P1-7 | P1 | Git 代理设置被错误地要求管理员权限 | [SettingsViewModel#L324](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L324) | 设置 | 否 | 0.2 人日 |
| P1-8 | P1 | 镜像池含已停服站点，无运行时健康检查 | [DownloadAccelerationService#L18-L24](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/DownloadAccelerationService.cs#L18-L24)、[MirrorUrlService#L13-L30](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorUrlService.cs#L13-L30) | 下载加速 | 否 | 0.5 人日 |
| P1-9 | P1 | 无版本检查，用户不知道有新版本 | 全仓无更新代码；[README#L116](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L116) 靠手写版本号 | 发布/支持 | 否 | 1 人日 |
| P1-10 | P1 | 所有源都失败时无兜底，应用等于不可用 | [GithubHostsService#L171](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L171) | 极端网络 | 否 | 1 人日 |
| P2-1 | P2 | 托盘只有显示/退出，无加速开关与状态 | [TrayIconManager 调用](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L53-L60) | 托盘 | 否 | 1 人日 |
| P2-2 | P2 | 下载加速只生成链接，需要用户手动复制到浏览器 | [MirrorUrlService#L108-L160](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorUrlService.cs#L108-L160) | 下载页 | 否 | 2–3 人日 |
| P2-3 | P2 | SSH（22 端口）场景完全未覆盖 | 全仓无 22/SSH 相关实现 | Git 场景 | 否 | 1 人日 |
| P2-4 | P2 | 拉取到的 Hosts 内容未校验，被污染的源会写死错误 IP | [GithubHostsService#L304-L307](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L304-L307) | 安全 | 否 | 1 人日 |
| P2-5 | P2 | 解决方案里还有 WinForms 旧 UI，发布物不明确 | [GithubAccelerator.slnx#L1-L6](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.slnx#L1-L6) | 维护/发布 | 否 | 1 人日 |
| P2-6 | P2 | 无 `global.json`，同事机器会用 SDK 11 RC 构建 | 构建实测 `NETSDK1057`、`dotnet --list-sdks` | 构建一致性 | 否 | 0.2 人日 |

> 分布：P0 = 6 项，P1 = 10 项，P2 = 6 项，合计 22 项。

### 功能覆盖矩阵（"用户到底想加速什么"）

| 用户场景 | 当前能力 | 结论 |
|---|---|---|
| 浏览器打开 github.com / 网页 | Hosts 加速 | ✅ 覆盖（主路径） |
| raw 文件、README 图片 | Hosts + 镜像链接 | 🟡 可用，但镜像列表含失效站点（P1-8） |
| Release / archive 下载 | 生成 ghproxy 等链接，需手动复制 | 🟡 可用但多一步（P2-2） |
| `git clone / pull / push` | 只有"设置 Git 代理"，实现方式错误 | ❌ 实际会破坏 Git（P1-1） |
| `docker pull ghcr.io/...` | 域名清单无 `ghcr.io`/`pkg.github.com` | ❌ 未覆盖（P1-2） |
| GitHub Pages（`*.github.io`） | 在域名清单内 | ✅ 覆盖 |
| gist / codeload / media | gist 在优化器清单，codeload 部分覆盖 | 🟡 部分 |
| SSH clone（22 端口） | 无 | ❌ 未覆盖（P2-3） |
| npm / pip / maven 源 | 无 | ⛔ 明确不做（见 §五） |

---

## 三、分项详述

### P0-1 提权自动化：让"双击就能用"

**现状**：manifest 没有 `requestedExecutionLevel`，默认 asInvoker（[app.manifest](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/app.manifest#L1-L17)）；程序检测到非管理员只写一行日志（[MainWindowViewModel#L416-L429](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L416-L429)），把"请右键以管理员身份运行"写进 README 和指南（[README#L41](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L41)、[QuickGuideWindow#L50-L58](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/QuickGuideWindow.axaml#L50-L58)）。全仓无 `runas`/自提权代码（已全局检索确认）。

**做法（推荐 A，简单可控）**
- A1：保持 asInvoker 启动，主界面顶部出现一条"当前没有管理员权限，无法加速"的可点击横幅，点击后 `Process.Start(new ProcessStartInfo(exePath){ Verb="runas", UseShellExecute=true })` 自我重启并退出当前实例。
- A2：若已有单实例保护（P1-6），提权重启时把命令行参数透传（例如 `--elevated`），避免二次启动冲突。
- A3：不要把管理员状态只做成侧边栏灰色小徽标（[MainWindow.axaml#L237-L243](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml#L237-L243)），要给出"为什么 + 点这里解决"。
- B 方案（不推荐但可作为选项）：manifest 直接 `requireAdministrator`，双击即 UAC。代价是每次启动都弹 UAC，且托盘/开机自启体验变差。

**不做会怎样**：绝大多数普通用户只会双击，双击后"智能优选"不可用或写入失败，他们不会去看 README；这是当前第一道流失。

**影响面**：`app.manifest`（仅 B 方案）、`MainWindowViewModel`、`MainWindow.axaml`、`Program.cs`（参数透传）。
**改变默认行为**：A 方案不改双击行为（仍不弹 UAC），只在需要时弹一次；B 方案每次弹 UAC。
**回滚**：A 方案删除横幅与提权按钮即可；B 方案把 manifest 改回 asInvoker（单独 commit）。

---

### P0-2 一键"开始/停止加速"状态机

**现状**：首页主按钮是"🚀 智能优选"（[DashboardView#L207-L209](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/DashboardView.axaml#L207-L209)），停止入口是只在已应用时才出现的"🗑️"图标按钮（[DashboardView#L169-L172](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/DashboardView.axaml#L169-L172)），用户还要先理解"数据源/评分/预览/应用"这套概念（整页还含数据源复选、搜索、排序、全选/取消等多组交互）。

**做法**
- 顶部放一个明确的开关卡片：`未加速` / `加速中（来源、应用时间、已运行时长）` / `加速中但检测异常`。
- 打开开关 = 现有 `ApplyHosts`（多源合并+优化）；关闭 = 现有清除逻辑（但要走 P0-4 修正后的实现），并提示"已恢复原始 Hosts"。
- **专家区下沉**：数据源列表、评分、预览折叠进"高级"区域；首屏只保留"状态 + 开关 + 一句人话解释"。
- 不做多套概念并存（"智能优选"和"应用选中源"两条路径保留在高级区）。

**不做会怎样**：用户不知道点哪个、也不知道怎么撤销；有用户会点"🗑️"以为是清理缓存，直接断掉已生效的加速。

**影响面**：`DashboardView.axaml`、`MainWindowViewModel`（命令与状态）、`MainView` 侧边栏导航。
**改变默认行为**：是——建议首启时若 GitHub 不可达则自动打开开关（见 §三-决策点 2）。
**回滚**：新状态机与旧按钮并存一个版本（feature flag 常量），确认后删除旧入口。

---

### P0-3 首次启动引导重构

**现状**：3 秒后弹一个 560×580 的模态窗口，内容 4 张卡片 + 小提示 + 反馈，本质是"说明书"；其中第 1 步是叫用户关掉程序去右键以管理员身份运行（[QuickGuideWindow#L38-L60](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/QuickGuideWindow.axaml#L38-L60)），默认每次启动都弹（`ShowQuickGuideOnStartup` 默认 true，[SettingsViewModel#L57](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L57)）。

**做法**：首启只做三件事，都带"完成/失败"结果，而不是叫用户去读：
1. 权限：非管理员 → 直接给"以管理员身份重启"按钮（P0-1）。
2. 网络：自动测 GitHub 三端点（已有 [TestAllConnectionsAsync](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubConnectionService.cs#L40-L65)），结果用"能通/不能通"两句话呈现。
3. 动作：能通 → "无需加速，可直接使用"；不能通 → 一个大按钮"立即加速"，并在结束时给出"已恢复/已加速，访问 github.com 验证"。
- 保留"更多设置"入口，但默认不弹长篇指南；把现有 4 步内容移入"帮助/使用指南"菜单（已有入口 [MainWindow.axaml#L196-L201](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml#L196-L201)）。

**不做会怎样**：首启体验是"读文档 + 关掉弹窗 + 自己找按钮"，与"傻瓜化"目标直接矛盾；且每次启动都弹（用户已保存过设置才会停）。

**影响面**：`QuickGuideWindow`、首启流程、`SettingsViewModel` 默认值。
**改变默认行为**：仅"首启弹什么"的变化；`ShowQuickGuideOnStartup` 建议改为 true 只弹一次（首次完成引导后自动置 false）。
**回滚**：保留原 `QuickGuideWindow`，入口切换即可。

---

### P0-4 回滚与卸载：不能把用户网络改坏

**现状问题（按严重度）**
1. **备份恢复语义错误**：`RestoreBackupAsync` 把备份内容交给 `ApplyGithubHostsAsync`（[HostsBackupService#L109-L117](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/HostsBackupService.cs#L109-L117)），后者会在末尾追加 `# === GitHub Accelerator Start/End ===` 块（[WindowsHostsFileService#L72-L91](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/WindowsHostsFileService.cs#L72-L91)）。结果：用户"恢复备份"后，恢复出来的内容被当作"加速内容"；下次点清除会把它一起删掉 —— 数据丢失。
2. **清除走另一条非原子路径**：`ClearAppliedHosts` 直接 `File.WriteAllTextAsync`（[MainWindowViewModel#L1002](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1002)），同类直写还有 L1143、L1158、L1222，全部绕过了 `AtomicWriteAsync`（[WindowsHostsFileService#L19-L42](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/WindowsHostsFileService.cs#L19-L42)），断电/杀进程会留下半截 hosts。
3. **备份只有一份且被覆盖**：每次应用都覆盖 `hosts.backup`（[WindowsHostsFileService#L44-L58](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/WindowsHostsFileService.cs#L44-L58)）；`HostsBackupService` 另有 5 份轮转备份，两条备份体系并存、语义不同。
4. **清除后不刷 DNS**，状态提示为"已清除我们添加的 Hosts 内容"，用户无法确认网络已恢复。
5. **卸载无还原**：Hosts 块的唯一删除入口是运行本程序点清除；用户直接删目录/卸载后，块永久残留。Git 全局代理同样残留（`SetProxyAsync` 写 `--global`）。
6. **`RestoreOriginalHostsAsync` 语义可疑**（[#L93-L104](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/WindowsHostsFileService.cs#L93-L104)）：把"当前内容去掉我们的块"再拼上"备份内容去掉我们的块"，两条非 GitHub 记录可能重复/顺序错乱；且该方法在 UI 中未被使用（用 BackupManager 的恢复路径）。

**做法**
- 统一 Hosts 读写唯一入口：只允许 `IHostsFileService` 的原子写；删除 ViewModel 里的写文件代码。
- 修正备份恢复：恢复 = 把备份内容当作"用户原始内容"直接覆盖写入（先原子备份当前文件到 `%AppData%` 再写），不再套加速标记；把"恢复备份"和"清除加速"两个语义在 UI 上分开说明。
- 清除加速：走原子写 + 自动 `flushdns` + 校验写入前后 diff（只应删掉标记块）。
- 新增"恢复原始网络设置"向导：列出 ①Hosts 加速块 ②Git 全局 `http.proxy`/`https.proxy`（仅在由本程序设置时，用 `%AppData%` 里的设置快照判断） ③开机自启注册项（[WindowsStartupManager#L190-L237](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/WindowsHostsFileService.cs#L190-L237)）④应用数据目录，逐项可勾选还原。
- 卸载路径：在 README/发布页给出一段"卸载前请先点『恢复原始网络设置』"；程序在退出时检测"加速仍生效"给出一次提示（不要静默卸载式残留）。
- 启动时若发现 Hosts 里有标记块但"不是本机上次应用的记录"（无 `%AppData%` 记录），提示"检测到遗留加速记录，是否清除" —— 解决"换了用户/删了配置目录"的孤儿块。

**不做会怎样**：用户点"恢复备份"可能丢掉自己原有的 hosts；用户卸载后 GitHub 换 IP 就再也打不开，并且完全不知道是谁改的 —— 这会变成售后问题，且是"把用户网络搞坏"的真实风险。

**影响面**：`WindowsHostsFileService`、`HostsBackupService`、`BackupManagerViewModel`、`MainWindowViewModel`、`settings.json`（新增"由我设置过的 Git 代理值"快照字段）。
**改变默认行为**：无（不改变首启默认动作）；新增退出前提示（可用设置关闭）。
**回滚**：逻辑集中在服务层，单 commit 回滚；新增的 `git proxy` 快照字段向后兼容（缺省视为"未管理"）。

---

### P0-5 分发与信任：让用户"下载得到、敢运行"

**现状**：README 指向 GitHub Releases（[README#L39](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L39)）；发布物是 `publish/` 下 146 MB 单文件 exe，无安装包、无数字签名、无 hash 清单。对"上不了 GitHub 的人"来说，下载这个工具本身就需要加速 —— 死循环。

**做法（代码 + 运营各一半）**
- 增加国内可访问的发布渠道（任选：Gitee Releases / 自建 OSS / 网盘 + 固定短链），README 首位放置；GitHub Releases 作为备用。
- 提供 `.sha256` 与版本说明；发布包命名带版本号（现在叫 `GithubAccelerator_v1.5.0.zip`，与 `1.5.5` 不一致）。
- 体积：评估改为 framework-dependent（需 .NET 10 运行时）或裁剪（`PublishTrimmed`），把 146MB 降到可接受区间；若保留自包含，至少提供"精简版/完整版"两档。
- 签名：短期至少给 exe 加版本信息 + 提供校验值；中期评估代码签名证书（成本项，需用户决策）。

**不做会怎样**：目标用户根本拿不到安装包；即便拿到，SmartScreen/杀软拦截会让非技术用户直接放弃。

**影响面**：发布脚本/文档（非业务代码）；`csproj` 的发布参数。
**改变默认行为**：无。
**回滚**：发布参数改回原值。

---

### P0-6 遥测：先告知，再谈要不要

**现状**：`App.Initialize` 无条件初始化星尘（[App.axaml.cs#L20-L23](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/App.axaml.cs#L20-L23)）；服务里 `Enabled = true`、服务器硬编码 `http://47.113.219.65:6600`、每 60 秒上报 CPU/内存/线程/句柄（[StardustMonitorService#L29-L40](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/StardustMonitorService.cs#L29-L40)、[#L90-L107](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/StardustMonitorService.cs#L90-L107)）。设置页没有任何相关开关（[SettingsView.axaml](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/SettingsView.axaml#L13-L233)）。

**做法**
- `SettingsData` 新增 `TelemetryEnabled` 与 `TelemetryServer`（默认值由用户拍板，见 §六-4）。
- 设置页新增"诊断与改进"分组：开关 + 服务器地址（只读展示）+ "发送什么"一句话说明 + 一键关闭。
- 首启引导末尾一行"匿名运行数据帮助改进（可随时关闭）"，不打断流程。
- 上报内容去除可识别信息（现在是进程指标与版本，风险低，但要写清楚）。
- `Enabled=false` 时不创建 `StarFactory`（现在 `Initialize` 里已提前 return，[#L63-L67](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/StardustMonitorService.cs#L63-L67)），确认无其它链路仍在发。

**不做会怎样**：应用向一个用户不可见的固定 IP 定期上报数据，无告知无开关 —— 一旦被用户发现，信任成本远高于这点数据价值；也不符合国内应用的基本合规习惯。

**影响面**：`SettingsViewModel`、`SettingsView.axaml`、`StardustMonitorService`、首启引导。
**改变默认行为**：取决于决策点 4。
**回滚**：默认值改回 `true`，或整段功能移除（无外部依赖）。

---

### P1-1 Git 加速：现在的实现会让 Git 变慢/失败

**现状**：`UseRecommendedGitProxy` 把 `GitProxyUrl` 设为 `https://ghproxy.net`（[SettingsViewModel#L393-L400](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L393-L400)），`SetProxyAsync` 把它写进 `git config --global http.proxy / https.proxy`（[GitProxyService#L205-L235](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GitProxyService.cs#L205-L235)）。`ghproxy.net` 是"前缀式反向代理"，不是 HTTP CONNECT 代理；Git 会向它发 `CONNECT github.com:443`，大概率 4xx/超时。用户看到的是"设置了代理，clone 反而失败"。

**做法（按推荐顺序）**
1. **URL 改写（最稳）**：`git config --global url."https://ghproxy.net/https://github.com/".insteadOf "https://github.com/"`，同时提供一键撤销（`--unset-all`）。只影响 clone/fetch，不碰代理语义。
2. **本地 CONNECT 代理**：项目里已有 `GithubProxyService`（[GithubProxyService#L51-L83](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubProxyService.cs#L51-L83)），但它用 `HttpListener` 实现"URL 转发"，**不是 CONNECT 代理**，且当前 UI 未使用它 —— 要走这条路必须补 CONNECT 处理、端口占用与常驻生命周期，成本高、风险大。
3. **SSH over 443 引导**（归入 P2-3）：仅给配置说明，不自动改。
4. 无论哪条路线：设置前先检测"当前是否已有代理"，给出"将被覆盖的现值"并保存快照，供 P0-4 恢复。

**不做会怎样**：Git 场景是"用户实际想加速什么"里最高频的一种；现状下这个功能展示存在但结果可能是负向的，比没有更糟。

**影响面**：`GitProxyService`、`SettingsViewModel`、`SettingsView`。
**改变默认行为**：是（默认推荐从"写 proxy"改为"insteadOf"，仅对 GitHub 生效）。
**回滚**：保留两种模式并在设置页可切换；`insteadOf` 模式下"清除"要同时清理 `url.*.insteadOf`。

---

### P1-2 补齐加速覆盖：容器与包拉取

**现状**：DNS 域名清单（[ChinazDnsService#L74-L89](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/ChinazDnsService.cs#L74-L89)）含 `github.com`、`raw`、`objects`、`codeload`、`github.io` 等；优化器清单含 `gist.github.com`（[#L41](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L41)）。但**两份清单都没有** `ghcr.io`、`pkg.github.com`（全仓检索无匹配），健康检查的子域表（[HealthCheckItem.cs#L73-L90](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Models/HealthCheckItem.cs#L73-L90)）也没有；第三个域名表 `SourcePerformanceMonitor` 同样缺（[#L336-L339](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/SourcePerformanceMonitor.cs#L336-L339)）。

**做法**
- 在 DNS 清单与核心域名表补：`ghcr.io`、`pkg.github.com`；`gist.github.com` 目前只在优化器清单里，也要同步进 DNS 清单与健康检查表。
- 健康检查页、DNS 清单、优化器清单、性能监控表共用同一份"要加速的域名"常量，避免四处清单漂移（[ChinazDnsService#L74-L89](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/ChinazDnsService.cs#L74-L89)、[HostsOptimizerService#L34-L47](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L34-L47)、[HealthCheckItem.cs#L73-L90](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Models/HealthCheckItem.cs#L73-L90)、[SourcePerformanceMonitor#L336-L339](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/SourcePerformanceMonitor.cs#L336-L339)）。
- 注意：`ghcr.io` 走的是 OCI Registry（`/v2/`），TLS 正常即可，Hosts 方案有效。

**不做会怎样**：`docker pull ghcr.io/...`、GitHub Packages 用户完全没被加速，而这类用户往往就是"开发者"里最需要加速的群体。

**影响面**：域名清单、Hosts 生成、健康检查。
**改变默认行为**：写入 Hosts 的条目会变多（需要提示"共 N 条域名"）。
**回滚**：清单常量回退。

---

### P1-3 失败路径：从"报错"到"下一步"

**现状**：全源失败只给一句"所有数据源均获取失败，请检查网络"（[MainWindowViewModel#L1379](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1379)）；写入失败提示"请以管理员身份运行"（[#L1447-L1449](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1447-L1449)，同样文案还出现在 L1569、L1727），但用户此时没有"以管理员重启"的按钮（P0-1）。日志在 `%AppData%\...\Logs`（[LogService#L95-L110](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/LogService.cs#L95-L110)），用户不会去找。

**做法**
- 建立"失败原因 → 一句人话 + 1~2 个动作"的映射表：无管理员 / DNS 被篡改 / 源站不可达 / 本机代理拦截 / 写入被占锁 / 优化超时。
- 失败卡片提供：`重试`、`换一个数据源`、`复制诊断信息`、`打开日志目录`。
- "复制诊断信息"= 版本 + OS + 是否管理员 + 三端点测试结果 + 当前 Hosts 是否含标记块 + 最近 30 行日志（脱敏：不包含用户名以外的路径细节）。
- `StatusMessage` 只用于过程；结果一律走 toast/卡片，避免用户盯着底部一行小字。

**不做会怎样**：当前用户失败后唯一能做的是"自己猜"；对非技术用户等价于"这软件坏了"，且没有任何信息能反馈给你（QQ 是唯一渠道）。

**影响面**：`NotificationService`/`ToastControl`、`MainWindowViewModel`、新增诊断服务。
**改变默认行为**：无。
**回滚**：新增组件独立，可移除。

---

### P1-4 长任务：进度、预算、取消

**现状**：智能优选对每个域名 × 每个候选 IP × 3 次探针（[HostsOptimizerService#L216-L260](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L216-L260)），单探针超时 5s（[#L22](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L22)）、并发 12（[#L50](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L50)）。最坏情况是数十秒到分钟级；界面只有底部一行 `StatusMessage` 随日志翻滚，`ApplyHostsCommand` 期间禁用，**没有取消**。

**做法**
- 主按钮区显示确定性进度：`已探测 37/84 个 IP，预计还需 25s`，配进度条。
- 总预算：整体上限（如 60s）+ 单域名候选数上限（如 4 个），超出即用当前最优结果落地（现在是"必须全部探完"）。
- 提供"取消"：`CancellationToken` 已在签名里（[OptimizeAsync](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L73-L109)），UI 却没有传 token，接上即可。
- 首次使用可以走"快速模式"：只用 1 次探针 + 单源，先让用户"立刻可用"，再后台精修。

**不做会怎样**：用户点击主按钮后界面"卡住不动"，很容易判定软件无响应而强杀 —— 强杀恰好发生在写 Hosts 附近时会放大 P0-4 的风险。

**影响面**：`HostsOptimizerService`、`MainWindowViewModel`、Dashboard。
**改变默认行为**：是（引入探测预算与"快速模式"）。
**回滚**：预算参数放设置/常量，恢复默认即回旧行为。

---

### P1-5 假开关：要么实现，要么删掉

**现状**：`CloseToTray`（默认 true）、`StartMinimized`、`ShowTrayNotifications` 只存在于 `SettingsData`/导出导入（[SettingsViewModel#L53-L55](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L53-L55)），代码里没有任何消费点（已全局检索确认）。而关闭按钮的实际行为是直接退出（[MainWindow.axaml.cs#L79-L85](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L79-L85)），与"关闭到托盘=开"直接矛盾。

**做法**：三选一按用户意向确认——(a) 全部实现（关闭到托盘、启动即最小化、托盘气泡受开关控制）；(b) 删除三个开关 + 同步删除导出字段（老配置反序列化会自动忽略）；(c) 保留 `CloseToTray` 并实现，删掉另外两个。
> 建议 (a)+(b) 混合：`CloseToTray` 实现，`ShowTrayNotifications` 实现（气泡已存在），`StartMinimized` 在开机自启场景下才有意义 → 与 P0-2 的"后台常驻"一起做。

**不做会怎样**：用户改了开关发现没效果，会认为整页设置都不可信（连带怀疑"自动应用 Hosts""自动刷新 DNS"是否真的生效）。

**影响面**：`MainWindow.axaml.cs`、`SettingsViewModel`、`DataExportImportService`。
**改变默认行为**：关闭按钮行为会变（如果实现 CloseToTray）。
**回滚**：开关默认值/行为常量回退。

---

### P1-6 单实例与写入互斥

**现状**：`Program.Main` 没有任何单实例保护（[Program.cs#L13-L20](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Program.cs#L13-L20)）；两个实例都会跑 30 分钟定时刷新（[StartPeriodicHostsRefresh#L1775-L1796](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1775-L1796)），并发写 hosts。
另：`WindowsHostsFileService.RequiresAdminPrivileges()` 用 `FileShare.None` 试打开（[#L128-L139](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/WindowsHostsFileService.cs#L128-L139)），若恰好被记事本占用会误判为"需要管理员"，该方法当前也未被 UI 使用。

**做法**
- 命名 Mutex（`Global\GithubAccelerator`）单实例，二次启动激活已有窗口（通过 `--activate` 或窗口消息）；提权重启时改名（`Global\GithubAccelerator.Elevated`）避免自我阻塞。
- Hosts 写入加进程内互斥（服务层 `SemaphoreSlim`）+ 写前检查文件是否被占用，失败给"请关闭编辑器后重试"。

**不做会怎样**：用户开两次（常见于从托盘和桌面图标各点一次）后，一次"应用"和一次"定时刷新"交错写，可能出现 hosts 里两个标记块（`RemoveGithubHostsBlock` 能处理多块，但 `IsGithubHostsApplied` 只判断存在标记）——状态显示错乱。

**影响面**：`Program.cs`、`WindowsHostsFileService`。
**改变默认行为**：第二次启动不再开新窗口。
**回滚**：移除 Mutex 即可。

---

### P1-7 Git 代理设置的权限判断是错的

**现状**：`SetGitProxy`/`UnsetGitProxy` 开头 `if (!IsAdmin)` 直接拒绝（[SettingsViewModel#L324-L327](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L324-L327)、[#L363](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L363)），`SettingsView` 也按 `IsAdmin` 禁用按钮（[#L114-L146](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/SettingsView.axaml#L114-L146)）。`git config --global` 只写用户目录 `~/.gitconfig`，**不需要管理员**。

**做法**：去掉 `IsAdmin` 前置条件，改为检测 Git 是否可用（已有 `CheckGitAvailabilityAsync`）；权限不足的真实失败（如只读 `.gitconfig`）按 P1-3 的映射给出提示。

**不做会怎样**：非管理员用户（默认情况）根本用不了 Git 功能，且提示语误导用户去走 UAC。

**影响面**：`SettingsViewModel`、`SettingsView.axaml`。
**改变默认行为**：非管理员可用 Git 功能。
**回滚**：恢复前置判断。

---

### P1-8 镜像池健康化

**现状**：`DownloadAccelerationService` 镜像含 `raw.fastgit.org`（FastGit 已于 2022 年停服）、`raw.sevencdn.com`、`gh.api.99988866.xyz`（[#L18-L24](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/DownloadAccelerationService.cs#L18-L24)）；`MirrorUrlService` 另有一套 `gitclone.com`、`github.com.cnpmjs.org`（同样已停服/不稳定）（[#L13-L30](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorUrlService.cs#L13-L30)）。两套镜像配置并存、互不同步，且都没有运行时可用性检测。

**做法**
- 合并为一份镜像配置（一个 `MirrorRegistry`），内置 `LastSuccessTime`，生成链接时按"最近成功 + 历史成功率"排序。
- 点击"复制/打开"前做一次 HEAD 探测（2s 超时），失败自动换下一个并提示"已切换到 X"。
- 移除已停服站点，或保留但排到末位并标注"可能失效"。

**不做会怎样**：用户复制到的第一个链接很可能就是死的，会认为"加速功能没用"；且业界镜像站生命周期很短，硬编码列表必然腐化。

**影响面**：`DownloadAccelerationService`、`MirrorUrlService`、`DownloadAcceleratorViewModel`。
**改变默认行为**：可用镜像排序变化。
**回滚**：镜像列表常量回退。

---

### P1-9 版本检查与更新提示

**现状**：全仓无版本检查/更新逻辑；`publish/` 里没有版本标识文件，README 版本号靠手写（[README#L116](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L116)）。

**做法（分两档）**
- 必做：启动后异步读取一个"版本清单"（可放在国内渠道/Gitee Raw/对象存储，带签名或 hash），比较 `AssemblyVersion`（当前 1.5.5，[csproj#L9-L11](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/GithubAccelerator.UI.csproj#L9-L11)），有新版本时在首页出一条"有新版本 vX（更新说明）→ 打开下载页"。
- 可选（不建议先做）：自动下载替换 —— 需要签名与回滚机制，风险高。

**不做会怎样**：Hosts 类工具对"源/域名变化"高度敏感，版本落后会表现为"越来越不好用"，而用户没有升级路径。

**影响面**：新增 `UpdateCheckService`、首页、发布流程（生成版本清单）。
**改变默认行为**：启动多一次外网请求（可在设置里关）。
**回滚**：移除服务与卡片。

---

### P1-10 全部源失败时的兜底

**现状**：三个源（GitHub520 及 4 个镜像、ittuann、DNS 探测）全失败就抛异常（[GithubHostsService#L171](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L171)），应用没有任何内置数据。

**做法**：随包内置一份"最近一次成功生成"的 Hosts 快照（首次运行时写入 `%AppData%`，每次成功应用后更新），全源失败时提示"使用本地缓存（生成于 X，可能已过期）"，并允许用户继续。这样即使断网/源站全挂，用户仍能恢复到上次可用状态。

**不做会怎样**：弱网环境（正是目标用户）下，应用最需要它的时刻恰好完全不可用。

**影响面**：`GithubHostsService`/`MainWindowViewModel`、`%AppData%` 新增 `last-good-hosts.txt`。
**改变默认行为**：失败时会写入缓存内容（需要明确提示"缓存可能过期"）。
**回滚**：删除缓存分支。

---

### P2 级（可选，按收益排序）

- **P2-1 托盘快捷开关**：托盘当前只有"显示窗口/退出"（[MainWindow.axaml.cs#L53-L60](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L53-L60)），加"加速开/关 + 当前状态"更符合"装完就不管"的使用方式。不做则用户每次都要开窗口。
- **P2-2 应用内下载器**：现在只生成链接（[MirrorUrlService#L108-L160](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorUrlService.cs#L108-L160)）。做"粘贴链接→自动选最快镜像→下载到指定目录→失败自动换镜像"。工作量最大，收益对开发者明显，可后置。
- **P2-3 SSH over 443 引导**：给出 `~/.ssh/config` 片段（`Host github.com / Hostname ssh.github.com / Port 443`）与"复制"按钮，不自动改用户文件。不做则纯 SSH 用户无解。
- **P2-4 Hosts 内容校验**：`ContainsGithubHosts` 只做了两个字符串包含判断（[GithubHostsService#L304-L307](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L304-L307)）。可复用 `ChinazDnsService` 里的已知 GitHub IP 段（[#L113](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/ChinazDnsService.cs#L113)）过滤异常 IP，降低"坏源把域名指向任意 IP"的风险。
- **P2-5 UI 项目收敛**：解决方案里同时有 Avalonia UI、WinForms `GithubAccelerator`、CLI、Tests（[slnx#L1-L6](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.slnx#L1-L6)），README 说 Avalonia 是主项目（[README#L61-L72](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L61-L72)）。不走"代码洁癖"，理由只有一个：**发布时拿错 exe / 新人改错项目**。建议在 README 与 slnx 注释里明确"仅 UI 为发布物"，或把 WinForms 移到 `legacy/`。
- **P2-6 锁定 SDK**：加 `global.json`（`rollForward: latestFeature` 指向 10.0.4xx），避免同事默认用 SDK 11 RC 构建 net10 项目。

---

## 四、建议实施顺序（分批，每批可独立回滚）

| 批次 | 内容 | 目标 |
|---|---|---|
| 第 1 批 | P0-1、P1-6、P1-7 | 让普通用户能启动、能提权、能少踩权限坑；风险最低、见效最快 |
| 第 2 批 | P0-4、P0-2 | 先把"回滚正确"做好，再换 UI 状态机（顺序不能反） |
| 第 3 批 | P0-3、P1-3、P1-4 | 首启引导 + 失败自救 + 长任务反馈 |
| 第 4 批 | P1-1、P1-2、P1-10 | 补齐真实加速场景（Git/容器/兜底） |
| 第 5 批 | P0-5、P1-9、P0-6、P1-5、P1-8 | 分发/更新/隐私/假开关/镜像池 |
| 第 6 批 | P2 按需 | 托盘、下载器、SSH、校验、项目收敛 |

---

## 五、明确不做的清单（及理由）

| 不做的事 | 理由 |
|---|---|
| 自动设置/接管 Windows 系统代理 | 影响全局网络，失败恢复成本高；现有 Hosts 方案已覆盖主要场景。用户对"改系统代理"极敏感 |
| 安装根证书 / 本地 TLS 解密 / MITM | 安全与合规风险远超收益 |
| 安装驱动、TAP/WinDivert、系统服务常驻 | 提权 + 杀软冲突 + 卸载残留，是"把用户网络搞坏"的高危区 |
| npm/pip/maven/Docker Hub 等非 GitHub 源的全局加速 | 超出产品边界，会变成另一个工具；可作为"相关推荐"链接 |
| 引入 DI 容器 / 拆分 79KB 的 `MainWindowViewModel` | 纯代码洁癖，不解决用户问题；只在大改（P0-2）时顺带拆命令 |
| 把 `settings.json` 迁移到 NewLife `Config<T>` | 现有实现可用且有导出/导入；迁移只增加回归面 |
| 换 UI 框架 / 多语言 / 插件化 | 与本次目标无关 |
| 静默自动更新（下载并替换自身 exe） | 无签名前提下风险高，先做"提示 + 跳转下载" |
| 多套备份体系继续并存 | 不是"不做"，而是收敛（并入 P0-4），避免语义冲突 |

---

## 六、待拍板的关键判断点

1. **提权方式**：`按需提权重启`（推荐，不每次弹 UAC）还是 `manifest requireAdministrator`（更简单，但每次双击都弹 UAC）？
2. **首启是否默认自动加速**（`AutoApplyHosts` 默认从 false 改 true）：会改变默认行为，首次启动可能直接写 Hosts。是否接受？
3. **是否默认开机自启**（`AutoStart` 默认 false → true）：配合"常驻后台维持加速"，但会让用户觉得"装了个甩不掉的东西"。
4. **遥测默认值**：保留默认开启但首次明确告知（改动最小），还是默认关闭、用户主动开启（最保守，但基本收不到数据）？
5. **"停止加速"的语义**：只移除我们写入的块（现状），还是"恢复到我第一次运行前的 Hosts 完整快照"（更安全，但会覆盖用户在此期间手动加的内容）？
6. **卸载清理**：是否允许程序在卸载/退出时自动移除 Hosts 块与 Git 代理（自动）还是只做引导（提示用户点"恢复原始网络设置"，推荐）？
7. **Git 加速路线**：`insteadOf` URL 改写（推荐、低风险）/ 本地 CONNECT 代理（体验最好、成本最高）/ SSH 443 引导（只给说明）？
8. **分发渠道与签名**：能否提供国内下载渠道（Gitee/OSS/网盘）？是否愿意投入代码签名证书？
9. **是否保留 WinForms 旧 UI 与 CLI 作为对外发布物**（影响 P2-5 的处理方式）。

---

## 七、附：评审中顺带发现的仓库风险（不属功能项，但建议立即处理）

1. **凭据泄露**：`.git/config` 的 `origin` URL 里内嵌了明文 GitHub PAT。建议立刻吊销该 token、改用凭据管理器或 SSH，并检查历史提交是否也带过该 token。本文不复述该令牌内容。
2. **工作区状态混乱**：当前有大量未提交改动（含删除 `publish/` 下数百个 DLL、改名文档、新增未跟踪文件），`csproj` 已是 1.5.5 而最后提交是 v1.5.0 tag。实施前建议先固化一个可回滚基线，否则"每批单独提交、单独回滚"无法执行。
3. **构建警告中的候选真实项**（建议随批次验证，不单独立项）：构建的 23 条警告里有可空引用与 CA1416；其中可空类警告需逐条核对是否会在运行时抛 `NullReferenceException`（本次只做构建与启动实测，未逐条复现）。

---

*文档生成时间：2026-11-05；本次未修改任何业务代码、未改动系统代理/Hosts/防火墙/服务注册。*
