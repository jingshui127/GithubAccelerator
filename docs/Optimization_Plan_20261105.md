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
| P0-5 | P0 | 分发不可达：发布在 GitHub Releases、146MB、无签名（第 5 批 70.03MB；第 6 批移除未使用的 Stardust.Extensions 后实测 59.14MB，附 SHA256SUMS 与一页安装说明，仍不买证书） | `publish/` 实测 146.06MB → `artifacts/` 实测 70.03MB、[INSTALL.md](file:///E:/CsharpCode/repos/GithubAccelerator/docs/INSTALL.md) | 发布/运营 | 否 | 2 人日（含渠道） |
| P0-6 | P0 | 遥测默认开启、无告知、无开关 | [StardustMonitorService#L29-L40](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/StardustMonitorService.cs#L29-L40)、[App.axaml.cs#L23](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/App.axaml.cs#L23) | 隐私、合规、信任 | 见 §三-6 | 0.5 人日 |
| P1-1 | P1 | Git 加速原方案把 `http.proxy` 指向反向代理站（第 4 批已改为 `url.insteadOf`，可逆、带来源记录与历史清理） | [GitUrlRewriteService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GitUrlRewriteService.cs)、[SettingsViewModel](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs) | Git 场景 | 是 | 2 人日 |
| P1-2 | P1 | 容器/包拉取：`ghcr.io` 只能文档指引；`pkg.github.com` 非下载端点，做不了（不加假开关） | [ChinazDnsService#L74-L89](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/ChinazDnsService.cs#L74-L89)、§P1-2 结论表 | 域名清单、文档 | 否 | 0.5 人日 |
| P1-3 | P1 | 失败后没有可执行自救路径，错误是技术术语（第 5 批已做：五类人话 + 按钮、诊断包预览/脱敏/不外发） | [FailureAdvisor.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/FailureAdvisor.cs)、[DiagnosticsBundleService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/DiagnosticsBundleService.cs) | 全失败分支 | 否 | 1.5 人日 |
| P1-4 | P1 | 智能优选耗时不可预期、无进度、不可取消 | [HostsOptimizerService#L216-L260](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/HostsOptimizerService.cs#L216-L260) | 首页主流程 | 否 | 1 人日 |
| P1-5 | P1 | 三个设置开关是假开关（持久化但无实现） | [SettingsViewModel#L53-L55](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L53-L55)、[MainWindow.axaml.cs#L79-L85](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L79-L85) | 设置、托盘 | 否 | 0.5–1 人日 |
| P1-6 | P1 | 无单实例保护，两个进程可同时改写 Hosts | [Program.cs#L13-L20](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Program.cs#L13-L20) | 启动、写入 | 否 | 0.5 人日 |
| P1-7 | P1 | Git 代理设置被错误地要求管理员权限 | [SettingsViewModel#L324](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L324) | 设置 | 否 | 0.2 人日 |
| P1-8 | P1 | 镜像池含已停服站点，无运行时健康检查（第 4 批已健康化并移除 DNS 死站） | [MirrorHealthService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorHealthService.cs)、§P1-8 实测记录 | 下载加速 | 否 | 0.5 人日 |
| P1-9 | P1 | 无版本检查，用户不知道有新版本（第 5 批已做：仅手动检查，私有仓库无公共通道，不假装自动更新） | [UpdateCheckService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/UpdateCheckService.cs)、[AboutWindow.axaml](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/AboutWindow.axaml) | 发布/支持 | 否 | 1 人日 |
| P1-10 | P1 | 所有源都失败时无兜底，应用等于不可用 | [GithubHostsService#L171](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L171) | 极端网络 | 否 | 1 人日 |
| P2-1 | P2 | 托盘只有显示/退出，无加速开关与状态 | [TrayIconManager 调用](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L53-L60) | 托盘 | 否 | 1 人日 |
| P2-2 | P2 | 下载加速只生成链接，需要用户手动复制到浏览器 | [MirrorUrlService#L108-L160](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorUrlService.cs#L108-L160) | 下载页 | 否 | 2–3 人日 |
| P2-3 | P2 | SSH（22 端口）场景完全未覆盖 | 全仓无 22/SSH 相关实现 | Git 场景 | 否 | 1 人日 |
| P2-4 | P2 | 拉取到的 Hosts 内容未校验，被污染的源会写死错误 IP | [GithubHostsService#L304-L307](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L304-L307) | 安全 | 否 | 1 人日 |
| P2-5 | P2 | 解决方案里还有 WinForms 旧 UI，发布物不明确（第 6 批审计：实为托盘 NotifyIcon 的 `UseWindowsForms`，无法安全移出，保持原状） | [GithubAccelerator.slnx#L1-L6](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.slnx#L1-L6) | 维护/发布 | 否 | 1 人日 |
| P2-6 | P2 | 无 `global.json`，同事机器会用 SDK 11 RC 构建 | 构建实测 `NETSDK1057`、`dotnet --list-sdks` | 构建一致性 | 否 | 0.2 人日 |

> 分布：P0 = 6 项，P1 = 10 项，P2 = 6 项，合计 22 项。

### 功能覆盖矩阵（"用户到底想加速什么"）

| 用户场景 | 当前能力 | 结论 |
|---|---|---|
| 浏览器打开 github.com / 网页 | Hosts 加速 | ✅ 覆盖（主路径） |
| raw 文件、README 图片 | Hosts + 镜像链接 | 🟢 可用，镜像池按实测健康度排序（P1-8 已做） |
| Release / archive 下载 | 生成镜像链接，需手动复制/打开 | 🟡 可用但多一步（P2-2 不做） |
| `git clone / pull / push` | HTTPS 远程改为 `url.insteadOf`；push 直连；SSH 未覆盖 | 🟢 HTTP 远程可用（P1-1 已做），SSH 见 P2-3 |
| `docker pull ghcr.io/...` | 无法由本程序自动加速 | ⛔ 仅文档指引（P1-2） |
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

### P0-5 分发与信任：让用户"下载得到、敢运行"（状态：已做，2026-11-05；不买代码签名证书）

**现状（改造前）**：README 指向 GitHub Releases（[README#L39](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L39)）；发布物是 `publish/` 下 146 MB 单文件 exe，无安装包、无数字签名、无 hash 清单。

**第 5 批实测（Windows x64，`Get-ChildItem | Measure-Object Length -Sum` 实得，非估算）**

| 项 | 改造前（`publish/`） | 改造后（`artifacts/v1.5.5/`） |
|---|---|---|
| 发布 exe | 146.06 MB | **70.03 MB**（73,429,145 B） |
| 发布 zip | 无 | **64.48 MB**（67,616,549 B，`GithubAccelerator_v1.5.5_win-x64.zip`：exe + `Assets/app-icon.ico` + `INSTALL.md` + `SHA256SUMS`） |
| 校验/说明 | 无 | `SHA256SUMS` + [INSTALL.md](file:///E:/CsharpCode/repos/GithubAccelerator/docs/INSTALL.md) 一页 |

体积构成（未压缩、无 PDB 的实测分类，合计 173.06 MB；PDB 100.09 MB 不随包发布）：

| 分类 | MB | 处理 |
|---|---|---|
| .NET 运行时 coreclr/host | 26.18 | 保留（自包含必须） |
| System 基础库 | 57.60 | 保留（无法安全裁剪） |
| WinForms 栈 | 23.16 | 保留（legacy 项目共用，删了会破坏构建） |
| ASP.NET Core 栈（NewLife.Stardust 引入） | 21.60 | 保留（诊断上报功能仍在，关开关不等于删依赖） |
| Avalonia + 托管 Skia | 11.88 | 保留 |
| 原生 Skia/HarfBuzz/GL | 17.97 | 保留（渲染必需，压进单文件后随 exe 压缩） |
| 卫星语言资源 | 8.57 | **Release 只保留 `zh-Hans;zh-Hant`**，其余语言不再随包 |
| 程序与业务依赖 | 2.93 | 保留 |
| 其他 | 3.17 | 保留 |

**做法（落地）**
- 瘦身顺序按约定执行：① 去掉未使用的语言资源（`SatelliteResourceLanguages`，Release 限定）→ ② 单文件压缩（`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + `EnableCompressionInSingleFile`）→ ③ framework-dependent **未采用**：实测约 61 MB（单文件 43.27 + 原生 17.97），只比现状少约 9 MB，却要求用户自装 .NET 10 桌面运行时，对"傻瓜化"是净负分，故放弃。
- 发布参数固化在 [FolderProfile.pubxml](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Properties/PublishProfiles/FolderProfile.pubxml)（`SelfContained=true`、`PublishSingleFile=true`、`PublishReadyToRun=false`、`DebugType=None`、`PublishDir=..\artifacts\staging\`）；`Release` 配置额外排除 `AvaloniaUI.DiagnosticsSupport`（仅 Debug 需要）。
- 可重复发布脚本 [Release.ps1](file:///E:/CsharpCode/repos/GithubAccelerator/scripts/Release.ps1)：清 staging → `dotnet publish -p:PublishProfile=FolderProfile` → 只留 exe → 复制托盘图标到 `dist/Assets/` → 生成 `SHA256SUMS` → 打 zip → 可选 `-Upload` 走 `gh release create/upload --clobber`。产物在 `artifacts/`，已在 [.gitignore](file:///E:/CsharpCode/repos/GithubAccelerator/.gitignore) 中忽略，不会被再次入库。
- 校验：`SHA256SUMS` 为"小写哈希 + 两空格 + 文件名"；安装说明给出可直接复制的 `Get-FileHash .\GithubAccelerator.UI.exe -Algorithm SHA256` 对比命令，并写明"对不上就别装"。
- 未签名如实说明：不写"数字签名可省略"，直接写明"未签名，首次运行 SmartScreen 会拦，这是正常现象"，给出放行路径（更多信息 → 仍要运行）。
- 安装说明一页：解压 → 可选校验 → 双击 + 点"开始加速"（与 P0-3 的两步对齐）→ 出问题走诊断包（先预览、不自动上传）。

**不做/未做**：国内镜像渠道（Gitee/OSS/网盘）仍属运营项，未在本批落地；代码签名证书按用户决策不购买。

**不做会怎样**：目标用户根本拿不到安装包；即便拿到，SmartScreen/杀软拦截会让非技术用户直接放弃。

**影响面**：发布脚本/文档（非业务代码）；`csproj` 的发布参数。
**改变默认行为**：无。
**回滚**：发布参数改回原值，删 `scripts/Release.ps1` 与 `docs/INSTALL.md`。

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

### P1-1 Git 加速：改用 url.insteadOf（状态：已做，2026-11-05）

**原方案（已废弃，勿再按此实现）**：旧实现把 `https://ghproxy.net` 这类"前缀式反向代理"地址写进 `git config --global http.proxy / https.proxy`。它并不是 HTTP CONNECT 代理，Git 会对它发 `CONNECT github.com:443`，大概率 4xx/超时；而且会污染用户全局配置，停止时无法准确还原。

**当前实现**（[GitUrlRewriteService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GitUrlRewriteService.cs)）：
- 只写 `url.<镜像前缀>.insteadOf = https://github.com/`，覆盖 clone/fetch/pull 的读取路径；不再写任何 `http.proxy` / `https.proxy`。
- 写入前记录来源（`%AppData%\GithubAccelerator\git-url-rewrite.json`）：key、值、镜像、写入前该 key 的既有值、是否本程序写入。停止时只删本程序写入且当前仍存在的精确值。
- 冲突保护：同名 key 已被用户占用（值不同）或已存在其它 `insteadOf` 规则匹配 `https://github.com/` 时，**不写入**并在 UI 报冲突；若已存在完全相同的规则，则不重复写入、不据为己有（停止时也不会删除）。
- push：写入一条恒等 `url.https://github.com/.pushInsteadOf = https://github.com/` 让 push 保持直连；镜像站不支持带鉴权的写入转发，UI 与提示明确说明"push 不走镜像"。用户已有 push 改写时保持原配置。
- 历史污染清理：设置页会扫描全局 `http.proxy`/`https.proxy`，按值匹配本项目曾用过的镜像 host 后列出 `key = value [来源文件]`，确认后一键删除这些精确条目，不碰用户自己的代理。
- 不覆盖 SSH 远程（`git@github.com:`）：`insteadOf` 只匹配 `https://github.com/` 前缀，SSH 场景仍归 P2-3。

**沙箱验证（未触碰用户全局 git 配置）**：
```powershell
$env:GIT_CONFIG_GLOBAL = "<临时文件>"; $env:HOME = "<临时目录>"; $env:GIT_CONFIG_NOSYSTEM = "1"
git config --global --add "url.https://ghfast.top/https://github.com/.insteadOf" "https://github.com/"
git config --global --add "url.https://github.com/.pushInsteadOf" "https://github.com/"
git ls-remote --heads https://github.com/octocat/Hello-World.git   # 成功返回 refs
git clone --depth 1 https://github.com/octocat/Hello-World.git <临时目录>\clone  # exit 0
git config --global --unset-all "url.https://ghfast.top/https://github.com/.insteadOf" '^https://github\.com/$'
git config --global --unset-all "url.https://github.com/.pushInsteadOf" '^https://github\.com/$'
```
结果：`remote get-url` 为镜像地址、`remote get-url --push` 仍为 `https://github.com/...`；ls-remote 与 clone 均成功；停止后沙箱配置 SHA256 与写入前一致（逐行 diff 为 0）。

**仍未覆盖**：SSH 远程；CLI 旧 `--git` 路径（本地 HttpListener 方案）未在本批改动。

---

### P1-2 ghcr.io / GitHub Packages：可行性结论（状态：ghcr.io 只能文档指引；pkg.github.com 做不了）

**结论**

| 目标 | 结论 | 原因 |
|---|---|---|
| `ghcr.io` | **只能文档指引，不做自动开关** | OCI Registry，客户端走 `https://ghcr.io/v2/...` + token 认证；本程序基于 Hosts/URL 前缀改写，无法改变容器客户端的 registry 协议；没有公开、稳定的 ghcr 镜像站，Hosts 只改解析地址并不能替代 registry 协议 |
| `pkg.github.com` | **做不了，不加开关** | 它不是包下载端点。GitHub Packages 实际端点是 `npm.pkg.github.com`、`nuget.pkg.github.com`、`maven.pkg.github.com`；私有包下载必须携带绑定 host 的 token，公开镜像无法代拉，URL 改写还会破坏 token 校验 |

UI 只在设置页保留一段纯说明文字，没有任何"容器加速"开关（避免假开关）。

**ghcr.io 手动配置片段（需自建/可信镜像；没有可用公共镜像时不要填）**

`containerd` 的 `hosts.toml`：
```toml
# 例：/etc/containerd/certs.d/ghcr.io/hosts.toml
server = "https://ghcr.io"

[host."https://<你的可信镜像地址>"]
  capabilities = ["pull", "resolve"]
  skip_verify = false
```

或者给容器运行时/命令行设置代理（不改系统代理）：
```powershell
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
docker pull ghcr.io/owner/image:tag
```

**GitHub Packages 手动配置片段（企业/本地代理，不做自动改写）**

npm（`%USERPROFILE%\.npmrc`）：
```ini
proxy=http://127.0.0.1:7890
https-proxy=http://127.0.0.1:7890
registry=https://npm.pkg.github.com/
//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}
```

NuGet（当前进程）：
```powershell
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
dotnet nuget list source   # 源仍为 https://nuget.pkg.github.com/OWNER/index.json
```

Maven（`~/.m2/settings.xml` 的 `<proxies>`）：
```xml
<proxy>
  <active>true</active>
  <protocol>https</protocol>
  <host>127.0.0.1</host>
  <port>7890</port>
</proxy>
```


---

### P1-3 失败路径：从"报错"到"下一步"（状态：已做，2026-11-05）

**现状（改造前）**：全源失败只给一句"所有数据源均获取失败，请检查网络"（[MainWindowViewModel#L1379](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1379)）；写入失败提示"请以管理员身份运行"（[#L1447-L1449](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1447-L1449)，同样文案还出现在 L1569、L1727），但用户此时没有"以管理员重启"的按钮（P0-1）。日志在 `%AppData%\...\Logs`（[LogService#L95-L110](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/LogService.cs#L95-L110)），用户不会去找。

**做法（落地）**
- [FailureAdvisor.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/FailureAdvisor.cs)：`FailureKind` 六值 → "一句人话原因 + 一个可执行按钮"。
  - 提权被拒 → "需要管理员权限才能改 Hosts"，按钮"以管理员身份重试"
  - 全部镜像不可达 → "所有加速源都连不上"，按钮"换成全部数据源再试"
  - Hosts 被第三方软件改写 → "Hosts 中的加速段被其它软件改过"，按钮"查看 Hosts 内容"
  - git 配置冲突 → "Git 里已有别的加速配置"，按钮"打开设置"
  - 无网络 → "本机连不上 GitHub"，按钮"测试 GitHub 连接"
  - 默认兜底也给一句话 + 一个动作，不弹错误码。
- 主窗口通知栏上方新增失败自救条（橙色左边框，原因文本 + 动作按钮 + ✕），不是只改底部 `StatusMessage`。
- 诊断包 [DiagnosticsBundleService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/DiagnosticsBundleService.cs)：预览列出 8 个文件与 5 条脱敏规则，实测 68.1 KB；包含日志（最近 2 个 × 300 行）、系统信息（OS/.NET/版本/是否管理员）、设置（脱敏）、Git 相关配置（**只取** `url.*.insteadof/pushinsteadof`、`http(s).proxy`）、镜像健康结果、**仅本程序的 Hosts 标记块**（先 `ContainsBlock` 再 `GetBlock`，不导整份 hosts）。
- 脱敏规则 [DiagnosticsSanitizer.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/DiagnosticsSanitizer.cs)：`C:\Users\<x>`、`/home/<x>`、当前用户名、`ghp_*` / `github_pat_*` / `x-access-token`、`token|pat|password|secret|authorization` 键值（含 `Bearer ` 前缀）、`https://user:pass@host`。
- 预览窗（[DiagnosticsPreviewWindow.axaml](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/DiagnosticsPreviewWindow.axaml)）：导出前必须先看清单；保存走系统文件选择器写本地 zip；**没有任何上传/发送代码路径**。日志被占用时用 `FileShare.ReadWrite` 共享读，失败文案不暴露路径。
- 设置页新增"故障排查 · 诊断包"卡片（按钮"生成诊断包（先预览）"）；Git 状态下方新增 `GitRewriteAdvice` 提示（检测到已有 rewrite 时给出人话说明）。

**不做会怎样**：当前用户失败后唯一能做的是"自己猜"；对非技术用户等价于"这软件坏了"，且没有任何信息能反馈给你（QQ 是唯一渠道）。

**影响面**：`MainWindowViewModel`、`MainWindow.axaml`、`SettingsView.axaml`/`SettingsViewModel`、新增诊断与失败服务。
**改变默认行为**：无（诊断包仅手动触发、仅本地保存）。
**回滚**：新增组件独立，可移除。

---

### P1-4 长任务：进度、预算、取消（状态：已做，2026-11-05）

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

### P1-5 假开关：要么实现，要么删掉（状态：已做，2026-11-05）

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

### P1-8 镜像池健康化（状态：已做，2026-11-05）

**实测记录（2026-11-05 19:48 +08:00，10s 超时；校验取回内容是否为 `Hello World!`，git 端点校验 `# service=git-upload-pack`）**

| 条目 | 结果 | 判定依据 | 决策 |
|---|---|---|---|
| ghfast.top | 200 / 973ms / 内容正确 | 取回 13 字节 `Hello World!` | 保留，健康排序第 1 |
| GHProxy ghproxy.net | 21:45 复测：raw 502、页面/codeload 超时、git ls-remote 69.66s | 早先 19:48 的 895ms 仅为瞬时；复测证实严重超时 | **降为末位并标注"严重超时，不建议使用"**（见 §九） |
| gh.api.99988866.xyz | 000 / 674ms / TLS 握手失败 | `SEC_E_ILLEGAL_MESSAGE`，未取回内容 | 保留但排末位（证据不足以判定停服） |
| jsDelivr CDN | 000 / 10039ms 超时（19:35 为 2794ms 正常） | 直连 CDN，非前缀代理 | 保留；不可达时自动降级 |
| GitMirror gitmirror.com | DNS 无法解析 / 76ms | `Could not resolve host` | **移除** |
| raw.fastgit.org | DNS 无法解析 / 54ms | `Could not resolve host`（FastGit 2022 已停服） | **移除** |
| raw.gitmirror.com | DNS 无法解析 / 49ms | `Could not resolve host` | **移除** |
| raw.sevencdn.com | DNS 无法解析 / 82ms | `Could not resolve host` | **移除** |
| github.com.cnpmjs.org | DNS 无法解析 / 54ms | `Could not resolve host` | **移除** |
| gitclone.com | 200 / 360ms / git 协议正确 | `application/x-git-upload-pack-advertisement` | 保留，clone 列表第 1 |

**实现**
- 新增 [MirrorHealthService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/MirrorHealthService.cs)：并行探测可达性、响应时间与内容正确性；**不只看 200**，HTML 错误页/内容不匹配都判为不健康；5s 超时，结果缓存 10 分钟。
- 排序：健康（延迟升序）→ 可达但内容错误 → 不可达；`MirrorUrlService.ConvertUrlAsync` 按实测顺序重排结果。全源不可用时的提示包含检测时间、前两条失败原因，并明确"已保留原始链接，可稍后重试或直接使用原始地址"。
- 合并镜像配置：`DownloadAccelerationService` 不再维护第二份含死站的列表，统一读取 `MirrorUrlService.AvailableMirrors`。
- 移除仅依据 DNS 不可解析（确定性证据）的 5 个死站；超时/握手失败的条目保留并降级，不凭单次超时删除。
- 测试：`MirrorHealthServiceTests` 共 11 个，覆盖排序、内容校验、缓存、全失败提示、URL 重排与死站移除。

---

### P1-9 版本检查与更新提示（状态：已做，2026-11-05；仅手动检查）

**现状（改造前）**：全仓无版本检查/更新逻辑；`publish/` 里没有版本标识文件，README 版本号靠手写（[README#L116](file:///E:/CsharpCode/repos/GithubAccelerator/README.md#L116)）。

**可行性判定**：仓库是 private，没有公共更新通道。三条路：
1. 带鉴权检查 → 要在客户端内置 token/PAT，对普通用户是"把凭据塞进客户端"，负分且不可轮换，**否决**。
2. 公开清单文件 → 需要在仓库外或公开 Gist 维护第二份版本源，多一处易失效的东西，本批不引入。
3. 手动检查 → 用户点"检查更新"时才请求一次，最简且诚实，**采用**。

**做法（落地）**
- [UpdateCheckService.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/UpdateCheckService.cs)：`HttpUpdateCheckClient` 调 GitHub Releases `releases/latest` 匿名 GET，**无 token、无用户标识、无机器信息**，只带 `User-Agent: GithubAccelerator` 与 `Accept: application/vnd.github+json`；超时 5s；`Timeout`/`Failed`/`NoReleaseInfo`/`UpToDate`/`UpdateAvailable` 全部收敛为可读状态，失败只更新一行文字（静默降级）。
- [AboutWindow.axaml](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/AboutWindow.axaml)：新增"检查更新"按钮 + 状态行 + "打开发布页"按钮（有新版才出现）。**不随启动运行、不加"自动更新"开关**，避免"假装能自动更新"。
- 与诊断上报完全独立：本功能不读写 `DiagnosticsEnabled`，UI 也不把它和诊断开关混在一起。

**代价**：不会自动提示更新，用户需自己点一次；私有仓库的 Releases 匿名可读（只要仓库公开前不会命中私有 404，`NoReleaseInfo` 有明确文案）。实测返回"已是最新版本（v1.5.5.0）"。

**不做会怎样**：Hosts 类工具对"源/域名变化"高度敏感，版本落后会表现为"越来越不好用"，而用户没有升级路径。

**影响面**：新增 `UpdateCheckService`、关于窗口。
**改变默认行为**：无（不随启动请求）。
**回滚**：移除按钮与服务。

---

### P1-10 全部源失败时的兜底（状态：已做，2026-11-05；本批口径见 §八）

**现状**：三个源（GitHub520 及 4 个镜像、ittuann、DNS 探测）全失败就抛异常（[GithubHostsService#L171](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.Core/Services/GithubHostsService.cs#L171)），应用没有任何内置数据。

**做法**：随包内置一份"最近一次成功生成"的 Hosts 快照（首次运行时写入 `%AppData%`，每次成功应用后更新），全源失败时提示"使用本地缓存（生成于 X，可能已过期）"，并允许用户继续。这样即使断网/源站全挂，用户仍能恢复到上次可用状态。

**不做会怎样**：弱网环境（正是目标用户）下，应用最需要它的时刻恰好完全不可用。

**影响面**：`GithubHostsService`/`MainWindowViewModel`、`%AppData%` 新增 `last-good-hosts.txt`。
**改变默认行为**：失败时会写入缓存内容（需要明确提示"缓存可能过期"）。
**回滚**：删除缓存分支。

---

### P2 级（可选，按收益排序）

> **状态：自用版全部不做。** 具体理由见 §8.6 汇总表（逐项一句）。

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

---

## 八、第 6 批（收尾批）报告（2026-11-05）

### 8.1 体积审计（先只读审计，再动刀）

**审计 1：WinForms 23.16MB 的来源**

| 环节 | 内容 |
|---|---|
| 引用路径 | [GithubAccelerator.UI.csproj](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/GithubAccelerator.UI.csproj) 的 `<UseWindowsForms>true</UseWindowsForms>` → 框架引用 `Microsoft.WindowsDesktop.App.WindowsForms` → 自包含发布带入整套 WinForms |
| 实际使用点 | 仅 [TrayIconManager.cs](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Helpers/TrayIconManager.cs)（WinForms `NotifyIcon` + `System.Drawing.Icon`，托盘图标与气泡） |
| 第二条传递链 | `NewLife.Core` 的 `net10.0-windows7.0` 资产在 `obj/project.assets.json` 中声明 `frameworkReference Microsoft.WindowsDesktop.App.WindowsForms`，即使关掉 `UseWindowsForms`，只要保留 Windows 资产仍会被捎带 |
| 是否有第二套 WinForms UI | **没有**。解决方案内只有 Avalonia UI；不存在"与 Avalonia 功能重合的 WinForms 界面" |
| 结论 | **不能安全移出，保持原状**。移出需同时：① 托盘改用 Avalonia `TrayIcon` 或 `Shell_NotifyIcon` P/Invoke；② 切断 NewLife.Core 的 Windows 资产。跨托盘与依赖两处，超出"收尾批不跨越 UI 大重构"的纪律 |

**审计 2：ASP.NET 21.60MB 的来源**

| 环节 | 内容 |
|---|---|
| 引用路径 | [GithubAccelerator.UI.csproj](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/GithubAccelerator.UI.csproj) → `PackageReference NewLife.Stardust.Extensions` → 包 nuspec `frameworkReference Microsoft.AspNetCore.App` → 自包含发布带入 ASP.NET Core 组件 |
| 是否本地代理/Kestrel 必需 | **不是**。项目内无 Kestrel、无 `HttpListener`、无任何本地 HTTP 服务，代码里也无 Stardust 类型引用 |
| 处理 | 仅删除这一条 PackageReference；`59.14MB` 为**实测**（`dotnet publish` 单文件 exe） |
| 省多少 | **70.03MB → 59.14MB，-10.89MB（实测）** |

### 8.2 P1-4 长任务进度与取消（已做）

| 长任务 | 进度口径（确定数字） | 取消行为 |
|---|---|---|
| 镜像健康检测 | "已完成 X / 共 N 项"，有界并发（默认 3），取槽后二次检查取消 | 取消后不再发起任何站点请求；未完成项标 `CANCELED / 已取消`；UI 回滚到检测前快照 |
| 应用 Hosts（全部源 / 单源 / 选中源） | "已完成 X 项，共 N 步"（N 随是否刷新 DNS 变化） | 传入 `CancellationToken`；取消清空进度并回到上一稳定状态 |
| Git 改写写入 / 回滚 | 写入 4 步；回滚 `已有条目数 + 2` 步，设置页显示"已完成 X / 共 N 项" | 取消不落半写状态 |
| 诊断包生成 | 固定 8 步，预览窗显示"正在生成预览…已完成 X / 共 N 项" | 关窗即停 |

不编造百分比：以上均为"已完成 X / 共 N"，没有用时间估算伪装进度。

### 8.3 P1-5 设置项真假清单（盘点后）

| 设置项 | 存储位置 | 生效点（文件:行） | 结论 |
|---|---|---|---|
| 数据源测试间隔 | `settings.json` | [MainWindowViewModel.cs:679](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L679)、[#L685-L687](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L685-L687) | 真（30–3600 秒截断，改后即时重设定时器） |
| 开机自启 | `settings.json` + 启动项 | [SettingsViewModel.cs:94-102](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L94-L102) → `IStartupManager` | 真 |
| 启动时自动加速 | `settings.json` | [MainWindowViewModel.cs:715](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L715)、[#L2278](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L2278) | 真 |
| 自动切换最优源 | `settings.json` | [MainWindowViewModel.cs:2284](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L2284) | 真（仅已加速时） |
| 最小化到托盘 | `settings.json` | [MainWindow.axaml.cs:28-40](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L28-L40)、[#L72](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L72) | 真（改后即时生效） |
| 托盘气泡通知 | `settings.json` | [MainWindow.axaml.cs:76](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Views/MainWindow.axaml.cs#L76) | 真（改后即时生效） |
| 应用后刷新 DNS | `settings.json` | [MainWindowViewModel.cs:1729](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1729)、[#L1901](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1901)、[#L2093](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L2093) | 真（决定步骤数与是否执行 flush） |
| Git 镜像地址 | `settings.json` | [SettingsViewModel.cs:360-366](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L360-L366) | 真 |
| 日志级别 | `settings.json` | [SettingsViewModel.cs:172-182](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L172-L182) → `LogService.SetLogLevel` | 真（影响后续日志过滤） |
| 诊断信息上报 | `settings.json` | [SettingsViewModel.cs:126-131](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/SettingsViewModel.cs#L126-L131) → [DiagnosticsSettings.cs:26-31](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Services/DiagnosticsSettings.cs#L26-L31) | 真（默认关闭，切换即生效） |
| 启动显示使用指南 | `settings.json` | [MainWindowViewModel.cs:1058](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/ViewModels/MainWindowViewModel.cs#L1058) | **恒真（无界面开关）**：没有任何 UI 会把它改成 false；实际"不再显示"由 `HasCompletedFirstRunGuide` 控制。不属假开关，但属死配置，留待下版本收口 |

**本批删除的两个假开关**：`CloseToTray`、`StartMinimized`（只持久化/导出、无任何消费点），同步删除 `SettingsData` 字段与导入导出映射。

### 8.4 P1-10 全源失败兜底（已做）

- 检测时间 + 每个源的具体失败原因（空响应统一写"无响应（未返回内容）"），见 [AllSourcesFailureReport](file:///E:/CsharpCode/repos/GithubAccelerator/GithubAccelerator.UI/Models/AccelerationStateMachine.cs)。
- 一键动作"改用原始链接（关闭加速）"：已应用则立即移除加速段（复用 `ClearAppliedHosts`），未应用则明确提示"当前已是原始直连"。
- 新状态 `AcceleratedUnavailable`："已加速，但当前数据源不可用（可改用原始链接）"，不再出现"加速中但其实全挂"。
- 原方案里的"内置 last-good 快照"**未做**（本批按用户给定口径做可见性与直连恢复），建议下版本评估。

### 8.5 验收记录

| 项目 | 结果 |
|---|---|
| Release 构建 | `dotnet build GithubAccelerator.slnx -c Release`：**0 error**；警告唯一行 18，与基线一致（签名级新增 **0**） |
| 全量测试 | `dotnet test GithubAccelerator.Tests -c Release`：**316 passed / 0 failed**（基线 308 + 新增 8：取消语义、无残留请求、全源失败报告、状态机、诊断包 8 步） |
| 体积 | 单文件 exe **70.03MB → 59.14MB（实测）**，PDB 由 `Release.ps1` 剔除、不随包分发 |
| 截图 | [batch6-health-progress-cancel.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-health-progress-cancel.png)、[batch6-health-cancelled.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-health-cancelled.png)、[batch6-all-sources-failed.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-all-sources-failed.png)、[batch6-settings-cleanup.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-settings-cleanup.png)（均只含产品界面，无用户名/token） |

### 8.6 22 项最终状态汇总

| 编号 | 状态 | 说明 |
|---|---|---|
| P0-1 | ✅ 已做 | `ElevationService`（runas 按需提权重启）+ 引导/失败条入口；不默认每次弹 UAC |
| P0-2 | ✅ 已做 | `AccelerationStateMachine` 单一开关 + 状态文本 |
| P0-3 | ✅ 已做 | 首启单页引导（可跳过，"不再显示"写 `HasCompletedFirstRunGuide`） |
| P0-4 | ✅ 已做 | 备份恢复不再包进加速标记；清除走原子写；卸载仅提示 |
| P0-5 | 🟡 部分 | 体积已降（59.14MB 实测）、有 SHA256SUMS 与安装说明；**代码签名与国内下载渠道未做**（不买证书） |
| P0-6 | ✅ 已做 | 遥测默认关闭，设置页可开，切换即生效 |
| P1-1 | ✅ 已做 | `url.insteadOf` 可逆改写，替代错误的反向代理配置 |
| P1-2 | ⛔ 判定不做 | `ghcr.io` 只能文档指引；`pkg.github.com` 非下载端点；不加假开关 |
| P1-3 | ✅ 已做 | 六类人话失败原因 + 动作按钮 + 诊断包（预览/脱敏/不外发） |
| P1-4 | ✅ 已做（本批） | 四个长任务确定进度；健康检测真正可中断；不编造百分比 |
| P1-5 | ✅ 已做（本批） | 删除 2 个假开关；托盘 2 项改后即时生效；清单见 §8.3（剩 1 项死配置已标注） |
| P1-6 | ✅ 已做 | `SingleInstanceGuard` 命名互斥 + 激活已有窗口；提权重启改名避让 |
| P1-7 | ✅ 已做 | Git 改写写 `~/.gitconfig`，不再要求管理员 |
| P1-8 | ✅ 已做 | 镜像池移除死站 + 运行时健康检查 |
| P1-9 | ✅ 已做 | 仅手动检查更新（私有仓库无公共通道，不假装自动更新） |
| P1-10 | ✅ 已做（本批） | 检测时间/逐源原因/一键直连/`AcceleratedUnavailable`；last-good 快照未做 |
| P2-1 | ⛔ 不做（自用版） | 托盘加速开关：自用只需开窗口操作，托盘加开关会引入又一组状态同步，收益低 |
| P2-2 | ⛔ 不做（自用版） | 应用内下载器：工作量 2–3 人日，自用可复制链接到浏览器/下载工具，性价比不足 |
| P2-3 | ⛔ 不做（自用版） | SSH over 443：本机实测 `ssh.github.com:443` TLS 握手失败，做了也无法验证；仅保留文档指引 |
| P2-4 | ⛔ 不做（自用版） | Hosts 内容强校验：自用且只写入已验收的源，强校验需维护 IP 段表，维护成本高于风险 |
| P2-5 | ⛔ 判定不做 | 审计确认 WinForms 为托盘依赖，无第二套 UI；不删（详见 §8.1） |
| P2-6 | ⛔ 不做（自用版） | 锁定 SDK：自用机器已固定 .NET 10；加 `global.json` 对自用无实际收益 |

> 按 22 项计：**已做 14 项 / 部分完成 1 项（P0-5）/ 不做 7 项（P1-2、P2-1~P2-6）**，无"未做悬空"项。


---

## 九、方法与能力矩阵（本机实测，2026-11-05 21:45，无外网出口）

### 9.1 最终定位

**GitHub 部分路径加速（自用）**。本工具只改写 GitHub 的若干下载/浏览路径到第三方镜像站，**不能、也不声称能让 GitHub 全站正常访问**；不内置代理、不装证书、不改系统代理与 Hosts（除用户显式点击已授权的 Git 改写动作）。

### 9.2 实测矩阵

测试方法：`curl.exe`（12s 超时、跟随重定向）+ 内容比对（HTML 标题、raw 内容、api JSON 字段、归档 content-type/size）+ `git ls-remote` + `ssh.github.com:443` 原始 TCP/TLS 握手。全部在本机执行，实测时间 2026-11-05 21:45，原始结果 `probe-results.json`（临时目录，不入库）。

| 方法 | 覆盖场景 | 需不需要出口 | 实测结果 | 实测时间 | 副作用 | 结论 |
|---|---|---|---|---|---|---|
| 直连（baseline） | 全部 | 不需要 | raw 200 内容一致 0.52s；api 200 0.34s；codeload 200 1.23s；git ls-remote 成功 0.96s；**github.com 页面 12s 超时；release 下载 12s 超时** | 21:45 | 无 | 基线：git/raw/codeload 直连可用，页面与 release 下载不可用 |
| 前缀反代 `ghfast.top` | git/raw/release/codeload/页面 | 不需要 | **raw 超时；页面超时；codeload 403；release HTTP 200，仅收到前 638044 字节（12s 截断）** | 21:45 | 不改全局配置 | 仅验证 HTTP 状态与前 638KB，**未做完整性与哈希校验**，不得表述为"已验证可用" |
| 前缀反代 `ghproxy.net` | git/raw/release/codeload/页面 | 不需要 | raw 502；页面超时；codeload 超时；**git ls-remote 成功但耗时 69.66s** | 21:45 | 不改全局配置 | **已从可用池移除**（降为末位并显式标注"严重超时、不建议使用"） |
| 前缀反代 `gh.api.99988866.xyz` | git/raw/页面 | 不需要 | raw 连接失败（0.6s）；页面 12s 超时 | 21:45 | 不改全局配置 | 不可用 → 排除 |
| 国内 Git 镜像 `gitclone.com` | git 协议 | 不需要 | **git ls-remote 成功 0.49s** | 21:45 | 不改全局配置 | 可集成（仅 git clone/fetch/pull） |
| CDN `cdn.jsdelivr.net` | 仓库内文件（非 release） | 不需要 | **200 内容一致 7.20s** | 21:45 | 不改全局配置 | 可集成（仅仓库文件，release 二进制不覆盖） |
| CDN `raw.githack.com` | 仓库内文件 | 不需要 | 12s 超时 | 21:45 | 不改全局配置 | 不可用 → 排除 |
| CDN `cdn.statically.io` | 仓库内文件 | 不需要 | 12s 超时 | 21:45 | 不改全局配置 | 不可用 → 排除 |
| Gitee 导入镜像 | 仓库镜像（人工同步） | 不需要 | `gitee.com/mirrors/redis` 405（机器人拦截）；raw 路径 404 | 21:45 | 不改全局配置 | 可达但不稳定、时效不可控 → 仅文档 |
| `ssh.github.com:443` | SSH over 443 | 不需要 | **TLS 握手失败：远端关闭传输流** | 21:45 | 不改全局配置 | 失败 → 仅文档（标注当前不可用） |
| 环境变量代理 `HTTPS_PROXY` | 全部（视出口） | **需要自备出口** | 本机未配置出口，未验证 | 21:45 | 改环境变量（非本工具行为） | 仅文档指引，不做开关 |
| 离线缓存 / 预下载 | 已提前拿到的归档/依赖 | 不需要 | 无网络测试（不适用） | — | 无 | 仅文档（适用批量预取，不覆盖网页与实时接口） |
| ~~FastGit `hub.fastgit.org`~~ | — | — | 12s 超时 | 21:45 | — | 排除（负面清单） |
| ~~`github.com.cnpmjs.org`~~ | — | — | DNS NXDOMAIN | 21:45 | — | 排除（负面清单） |
| ~~`gitmirror.com`~~ | — | — | DNS NXDOMAIN | 21:45 | — | 排除（负面清单） |

**四类覆盖现状（应用内已配置的映射）**

| 类别 | 是否已配置镜像改写 | 本机当前实测 |
|---|---|---|
| git 协议（clone/fetch/pull） | 是（`insteadOf` + `gitclone.com`） | gitclone.com 通过；ghproxy 通过但 69.66s（已降级）；直连也可用 |
| `raw.githubusercontent.com` | 是（前缀 + jsDelivr） | 直连通过；jsDelivr 通过；ghfast 当前超时 |
| `releases/download`（含 302 到 `objects.githubusercontent.com`） | 是（`github.com` 与 `githubusercontent.com` 前缀） | 直连超时；ghfast 仅部分字节；**`objects` 签名 URL 未取得，未实测** |
| `codeload.github.com` | **本批补齐** | 直连通过；ghfast 403；ghproxy 超时 |

### 9.3 策略设计（判定不做，提案备查）

> 结论：**不做策略列表界面、不加多策略勾选**。代码中没有策略选择相关死代码；界面只保留一个"打开镜像版页面"入口 + 状态提示。以下提案仅供后续版本参考，未进入实现。

1. **策略清单**：每条策略声明"覆盖的域名/场景 + 限制 + 三条状态"，不再用一排无差别开关。候选：①Git 改写（git 协议）②前缀镜像（raw/release/页面）③CDN（仓库文件）④离线预下载（文档型）。
2. **状态标签只有三种**：`可用`（最近一次检测内容校验通过）、`不可用（原因 + 检测时间）`、`需自备出口`（如 `HTTPS_PROXY`；只显示说明，不渲染开关）。未实测一律显示"不可用/未验证"，不允许默认看起来可用。
3. **优先级与互斥**：前缀改写类**互斥**（同一时刻只能选一个镜像前缀，避免同一 URL 被叠加两次改写）；CDN 与 Git 改写**可并存**（作用于不同场景）。Git 改写是全局配置，需用户显式点击，且只在 clone/fetch/pull 生效，push 仍直连。
4. **冲突文案**：多选互斥策略时**拒绝并解释**——"已启用「ghfast.top」前缀改写，无法同时启用「ghproxy.net」；如需切换请先停用前者"。任何切换前提示"这会影响后续 clone/下载地址"，不静默覆盖。

### 9.4 直白结论

**在不引入出口节点的前提下，本项目最多做到：git clone/fetch/pull、raw 文件、release 安装包、codeload 归档（可用性取决于镜像当下状态）+ 公开页面走镜像版浏览。** 不需要加速：`api.github.com` 接口与需要登录的操作（**直连可用，无需加速**）。不可能覆盖：页面内脚本请求、`ghcr.io` 容器镜像、`git push` 鉴权。且本机当前镜像池可用率偏低（稳定通过的只有 gitclone.com 的 git 协议与 jsDelivr 的仓库文件，ghproxy 严重超时已降级），因此**"通用加速"不成立**：只能按场景选可用路径，并把失败如实展示。

### 9.5 相关截图

- [batch6-mirror-link-entry.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-mirror-link-entry.png)：下载加速器入口（含"镜像版页面不是 github.com"与"不装证书/不改系统代理"说明）
- [batch6-mirror-unsupported.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-mirror-unsupported.png)：`api.github.com` 提示"直连可用，无需加速" + 原始链接回退
- [batch6-boundary-guide.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-boundary-guide.png)：首启引导的"能做什么 / 不能做什么"

---

## 十、自测（dogfooding，用产品自身测产品）

测试时间：2026-11-06 13:31–13:34（本机，Asia/Shanghai）。被测对象：本仓库 `E:\CsharpCode\repos\GithubAccelerator`（页面路径 `jingshui127/GithubAccelerator`）与小体量公开仓库 `octocat/Hello-World`。方法与产品运行时同一套规则（`MirrorUrlService.AvailableMirrors` 镜像前缀、`GitUrlRewriteService` 的 `insteadOf` 键名/取值），用 `curl.exe` 与 `git` 命令证实。全程未改系统代理、Hosts、防火墙，未装证书。

### 10.1 A. 网页路径

| 路径 | 状态码 | 耗时 | 内容校验 | 结论 |
|---|---|---|---|---|
| 直连 `https://github.com/jingshui127/GithubAccelerator` | 200 | 1.56s | 264074 字节，HTML 内含 `GithubAccelerator` 与 `jingshui127` | 本次可用（与 2026-11-05 的 12s 超时相反，网络状态会变） |
| 镜像版页面 `https://ghfast.top/https://github.com/jingshui127/GithubAccelerator`（产品镜像池首位） | 403 | 1.93s | 14 字节，非仓库页 | 不能替代 |
| 镜像版页面 `https://gh.api.99988866.xyz/https://github.com/jingshui127/GithubAccelerator` | 000（TLS 握手失败） | 0.61s | 无内容 | 不能替代 |

结论：**镜像版页面不能替代 github.com 页面浏览**——ghfast 对页面路径直接返回 403，99988866 TLS 握手失败；本次直连本身可用，不需要走镜像。产品界面的口径（"镜像站页面不是 github.com"、健康检测不通过时只给原始链接）不变。

### 10.2 B. git 读路径

用产品真实写入的全局改写（`url."https://<base>/https://github.com/".insteadOf = https://github.com/`，外加恒等 `url."https://github.com/".pushInsteadOf`）实测，测完立即按字节还原。

| 路径 | `git ls-remote` | `git clone --depth 1` | 改写命中证据 | 结论 |
|---|---|---|---|---|
| 直连 | 1.11s 成功 | 2.32s 成功 | — | 基线 |
| `ghfast.top`（产品默认 `DefaultMirrorBaseUrl`） | 5.70s 成功 | 3.03s 成功 | `GIT_TRACE`：`git-remote-https https://github.com/octocat/Hello-World.git https://ghfast.top/https://github.com/octocat/Hello-World.git` | 可用，但比直连慢 |
| `gitclone.com`（`GitCloneMirrors` 首位） | 0.52s 成功 | 1.12s 成功 | 同一 `insteadOf` 机制 | 比直连快，推荐 |

两条镜像链路克隆出的 HEAD 与直连一致（`7fd1a60b01f91b314f59955a4e4d4e80d8edf11d`），内容正确。

### 10.3 C. git 写路径（边界验证，不是缺陷）

- `git remote get-url --push origin` = `https://github.com/jingshui127/GithubAccelerator.git`。
- 在 `insteadOf` 生效期间执行 `GIT_TRACE=1 git push --dry-run origin main`：实际调用为 `git-remote-https origin https://github.com/jingshui127/GithubAccelerator.git`，**没有被改写到镜像**。
- 本轮真实 push（合入 `main`）成功，耗时 16.1s。
- 结论：**设计如此——镜像不做鉴权转发，push 无法加速**；产品额外写入恒等 `pushInsteadOf` 正是为此。不记作缺陷，也不列入改进项。此前 443 连接失败属于网络侧波动，与镜像能力无关。

### 10.4 D. 安全与还原

- 未改系统代理、Hosts、防火墙；未装证书；未占用任何被占用端口/串口。
- 全局 `~/.gitconfig` 测试前 SHA256 = `C9435D20D29028C41BBD6B57FE2078AABE0B1BA129CFB80609369CDB12D07CEC`，备份到临时目录 `dogfood\gitconfig-backup`；两次写入后均用备份文件按字节覆盖还原，**还原后 SHA256 与此前完全相同**。
- 写入前确认不存在任何 `url.*.insteadOf` / `pushInsteadOf` 条目（`git config --global --get-regexp` 无输出），因此**未覆盖用户既有配置**。

### 10.5 自测暴露的问题（如实记录，未在本轮改动）

1. **产品默认 git 镜像与实际推荐不一致**：`GitUrlRewriteService.DefaultMirrorBaseUrl` 是 `ghfast.top`，而本次实测 gitclone.com 更快（0.52s vs 5.70s），镜像清单里也把 gitclone.com 标为"推荐"。按"不改范围、不留半成品"要求，本轮只记录不动代码。
2. **"直连不可用"是随时间变化的结论**：2026-11-05 21:45 github.com 页面 12s 超时，2026-11-06 13:31 直连 200 / 1.56s。文档与界面凡"页面直连超时"的表述都应附检测时间，不能当固定事实。
3. **"打开镜像版页面"当前会打开一个 403 页面**：ghfast 对页面路径 403；产品只在健康检测判定全源不健康时拒绝打开并给原始链接，而健康检测若不覆盖页面路径，用户点击后仍可能看到 403。建议（未实施）：健康检测对页面路径单独判定。
4. **镜像不一定更快**：本次两条 git 镜像中 ghfast 反而比直连慢（5.70s vs 1.11s），说明网络恢复后"加速"可能为负收益；产品把直连作为回退是对的。

截图（本构建 UI 证据，2026-11-05 采集；今日镜像页返回 403，故沿用同一界面）：

- [batch6-mirror-link-entry.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-mirror-link-entry.png)：下载加速器界面，含"镜像站页面不是 github.com"与"不装证书/不改系统代理"提示
- [batch6-all-sources-failed.png](file:///E:/CsharpCode/repos/GithubAccelerator/docs/screenshots/batch6-all-sources-failed.png)：全源不可用时的兜底（检测时间 + 逐源原因 + 一键改用原始链接）

验收：本轮为命令行实测，未新增测试代码；Release 构建 0 error；全量测试 328 通过 / 0 失败（与基线一致）。


