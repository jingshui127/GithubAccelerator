# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.6.10] - 2026-11-09

### Fixed
- 首启引导勾选「开机自启」只保存偏好，不再立即写 `HKCU\...\Run`；跳过引导不留下任何系统级改动
- Run 项改由加速真正生效时写入；关闭加速或回到原始 Hosts 时清理，用户偏好保留，下次开启加速自动补写

## [1.6.9] - 2026-11-08

### Fixed
- 深色主题：页面底色、卡片底色、正文与次要文字改为随主题切换，不再出现浅底白字或深底深字
- 深色主题：选中的页签文字改用主文字色，不再暗红、低对比
- 深色主题：下载加速器的链接、jsDelivr / Git Clone 结果卡、黄色提示块、错误提示块全部改用主题色，深色下不再出现刺眼浅色块
- 深色主题：Toast、空态、骨架屏、备份弹窗、关于 / 源码预览 / 诊断包窗口按当前主题取色
- 深色主题：首启引导的网络状态色随主题取色，深色下不再用深绿/深红压暗底
- 浅色主题：正常 / 较慢 / 失败计数、链接与警告文字改用更深色值，修掉原来对比度不足的问题
- 首启引导正文右侧被裁切，改为限宽 560 后正常换行

## [1.6.8] - 2026-11-08

### Changed
- 开启加速的判定过程会逐步说明进度：每完成一组对比就告诉你「目前比直连快/慢多少、距 20% 门槛还差几个百分点、还需再测几组」
- 第 2 组出结果后立刻给出阶段性结论，并说明「再补几组确认」，不用再干等 20 多秒却没有反馈
- 等待期间每秒刷新「已等待 N 秒」，长判定过程中不再有超过一秒的空档
- 「中止本次加速」在判定全程都可以点；取消后不写入 Hosts、不请求管理员权限

## [1.6.7] - 2026-11-08

### Changed
- 设置页开关改为中文「开 / 关」，不再夹带英文 On/Off
- 仪表盘在没测过速时不再显示「-- ms」占位；只有真正完成「应用前 → 应用后」对比后才出现这一行
- 官网诊断统一叫「重新检查」，和页顶按钮一致
- 关于窗口文案明确：只在比直连至少快 20% 且你确认后，才更新 Hosts 文件
- 诊断包预览完成后稳定显示「预览已生成」，不再看起来卡在进度 8/8
- 帮助页版本号改为「当前版本：v1.6.7」
- 整体走查 6 个页面与 4 个对话框，空态、失败态和下一步操作都补成一句人话

### Fixed
- 关于窗口底部说明被裁掉，版本、构建日期和联系方式现在完整可见
- 诊断包预览的最后一条进度会覆盖「预览已生成」，导致看起来一直没完成

## [1.6.6] - 2026-11-08

### Changed
- 删除「单次响应统计」折线图：只保留当前、平均、最快、最慢 4 个统计值，避免和仪表盘「单次请求响应」重复
- 没有采样数据时直接提示点「开始测速」，不再显示空图表或 0 ms
- 把「Hosts 内容」和「备份与恢复」合并成「Hosts 文件」一个入口：左边看 Hosts，右边做备份，不用来回切页
- 「分组」和「回到原始状态」保留为同一页的两个页签，还原入口仍在
- 左侧导航「Hosts 与备份」改名为「Hosts 文件」

## [1.6.5] - 2026-11-08

### Changed
- 全界面术语换成更好懂的说法：备选 IP、当前直连、整页打开、对比、预检查、加密连接、缓存有效期
- 每个页面补齐空态、加载中、失败态提示：没有数据、还没检测、暂无备份、暂无日志时，都告诉你下一步该点哪里
- 单次响应统计没有成功采样时显示「—」，不再显示会误导的 0 ms
- Hosts 没有匹配行、备份时间缺失时都有明确说明，不再出现 0001-01-01
- 官网诊断、Hosts 防护、线路详情里的表头和「怎么读」说明全部重写

## [1.6.4] - 2026-11-08

### Fixed
- 备选 IP 连不上时不再每轮都等满 10 秒：连续预检查失败会提前结束，坏网络下判断明显更快
- 当前直连整页打不开时同样快速结束，不再空等
- 缓存命中的常见场景不再被候选超时拖回 30 秒以上

### Changed
- 20% 准入门槛、有效对比组数、先判后提权、不写 Hosts 的规则保持不变

## [1.6.3] - 2026-11-08

### Changed
- 首屏改成一句直答「现在能不能用 GitHub？」：检查中 / 直连可用无需加速 / 直连不通建议开启 / 已开启加速
- 主按钮下方直接写明：比直连至少快 20% 才写入，否则不改任何系统文件，也不会弹管理员确认
- 删除首屏重复的 GitHub 状态徽标和「暂停 / 开始自动测速」开关；自动测速仍在后台运行
- 三张指标卡压缩到 1–2 行，只保留单位、含义、口径和更新时间

## [1.6.2] - 2026-11-08

### Changed
- 非管理员点主按钮的顺序改为「先判、后提权」：先用当前身份跑完只读的 20% 准入门槛判定，达标后才请求管理员权限并写入；判定不通过时明确提示「当前网络换 IP 改善不了」，全程不弹 UAC、不写 Hosts
- 提权续做实例携带判定票据（`--prechecked=ip@时间戳`），写入前先做新鲜度/候选一致性校验，再重新跑一次完整门槛判定（不信任旧结论）
- 判定链路本身只读（不建快照、不写 Hosts、不提权），非管理员可完整执行

### Added
- 测试覆盖：不达标不触发提权且 Hosts 指纹不变、达标才提权并写入、续做票据的过期/候选变化二次校验

## [1.6.1] - 2026-11-07

### Fixed
- 主按钮「🚀 开启网页加速」此前绕过 20% 准入门槛，现改为与防护版同一判定：对**即将写入**的 `github.com` 候选做交错配对，未快 20% 时明确提示「当前网络换 IP 改善不了」且不写入 Hosts
- 统一所有写 Hosts 入口：来源卡片应用、选中数据源应用、自动切换、自动刷新、分组「用此分组加速」、CLI、旧版 WinForms 均走同一道判定，不再存在绕过路径
- 测速对比在门槛拒绝时如实中止，不再用直连数据冒充「加速后」

## [1.6.0] - 2026-11-07

### Changed
- 主界面合并为三层结构：能不能用 / 快不快 / 出问题怎么办，常用操作只保留一个主按钮
- 左侧导航精简为 6 项：检查与修复（含网络监控）、Hosts 与备份、帮助与支持各自合并成一个页面
- 速度统一按「页面加载耗时（不执行 JS）」展示，直连与候选并排比较，不再混用解析耗时
- 「回到原始状态」合并了四种清除方式，写 Hosts 前先给预览，可一键撤销
- 术语改为白话：数据源 → 加速线路，去掉未解释的内部评分与重复状态文案
- 备份页、Hosts 防护面板、设置页各只保留一个恢复入口，避免同名按钮行为不一致

## [1.5.5] - 2026-07-21

### Added
- 全项目完整审查与优化：UI 一致性、ViewModel 健壮性、Core 服务层稳定性
- App.axaml 补全主题资源定义（BackgroundColor、CardBackground、InfoBackground、CardBorderBrush 等）
- 统一按钮样式：PrimaryButton、SuccessButton、DangerButton、SecondaryButton
- 统一页面标题样式（PageTitle）和卡片容器样式（CardBorder）
- LogViewer 空状态显示（"暂无日志"）
- BackupManager 空状态显示（"暂无备份，点击创建"）
- BackupManager 删除二次点击确认机制（3 秒超时自动取消）
- HostsGroupView 删除分组二次点击确认机制
- BackupItemViewModel.ViewContent 真实功能实现（弹窗显示备份内容）
- MainWindowViewModel 实现 IDisposable，统一释放 Timer 与事件订阅
- 高代价命令添加 CanExecute 约束（ApplyHosts、RunSpeedTest、RefreshSources、ClearAppliedHosts）

### Changed
- GitHubLatencyViewModel 实现 IDisposable，提取魔法数字为常量
- 合并 MainWindowViewModel 中重复的 _updateTimer 和 _autoUpdateTimer
- 修复 StartPeriodicHostsRefresh 中 Timer 局部变量泄漏问题
- 联系方式统一为 "QQ: 2492123056 | 微信公众号：科控物联"
- HostsOptimizerService 探针日志精简（仅输出警告和错误）
- GitProxyService 异步化改造（WaitForExitAsync 替代 Task.Run + WaitForExit）

### Fixed
- **[严重]** App.axaml 引用不存在的 CardStyles.axaml 导致启动样式加载失败
- **[严重]** HostsGroupView 重命名框与新建框共用 NewGroupName 导致输入互相干扰
- **[严重]** DownloadAcceleratorView DockPanel 双 Right 布局导致 TextBox 不填充
- **[严重]** HostsContentView "用记事本打开"按钮未按管理员权限禁用
- **[严重]** DownloadAcceleratorViewModel 使用 WinForms Clipboard 依赖（改用 Avalonia 委托注入）
- **[严重]** WindowsHostsFileService Hosts 写入非原子性（改用临时文件 + File.Replace 原子替换）
- **[严重]** GithubProxyService 完全禁用 SSL 证书验证（改为仅允许 SNI 不匹配）
- **[严重]** SourcePerformancePersistence 非原子写入导致历史数据丢失风险
- **[严重]** SourcePerformanceMonitor 并发 List 访问崩溃（加锁 + 新增 AddMetrics/CleanupBefore 线程安全方法）
- HostsOptimizerService 核心域名 requiredSuccesses Bug（注释说 2/3，代码两个分支都是 1）
- GithubConnectionService nslookup 解析 Bug（误匹配 DNS 服务器地址）+ 进程同步读取死锁风险
- GitProxyService 进程超时未 kill 导致僵尸进程 + 同步 ReadToEnd 死锁风险
- GitHubLatencyViewModel 构造函数 UpdateFromService 无效调用
- QuickGuideWindow 版本号硬编码 v1.3.0（改为动态读取程序集版本）
- IpSpeedTestService 排序 Bug（不可达 IP 的 -1 排到最前面）

### Security
- GithubProxyService SSL 证书验证改为按域名校验，拒绝证书链错误（防中间人攻击）
- GitProxyService 参数拼接改用 ProcessStartInfo.ArgumentList 自动转义（防 shell 注入）

### Removed
- 删除废弃的 PerformanceChartView 整套文件（View、ViewModel、Service）
- 删除废弃的 GithubAcceleratorService.cs（功能已拆分到其他服务）
- 删除废弃的 IntelligentSourceManager.cs（功能已被 SmartSourceSelector 取代）
- 删除临时文件：55、temp_screenshot_settings.py、test_git_proxy.ps1、深色主题.png、GithubAccelerator.UI.csproj.Backup.tmp、Assets/avalonia-logo.ico

---

## [1.5.4] - 2026-07-20

### Added
- 启动时若 GitHub 已可访问则跳过自动应用 Hosts（连通性前置检测）
- 智能优选后自动触发子域健康检查
- Hosts IP 实时优化功能（自动探测候选 IP 并生成优化 Hosts）

### Changed
- 移除冗余的"网络性能概览"页面（PerformanceChartView）
- 网络监控页简化为仅保留 GitHub 延迟监控
- 主页版本信息动态绑定到程序集版本

### Fixed
- DoH 查询串行导致 20 秒延迟（改为 Task.WhenAll 并行，压缩到 5 秒）
- 核心域名 3 次探针验证 + 兜底 IP 写入策略
- 多源DNS 总被选为最优源的评分偏差问题（移除硬编码保底分）
- GitHub 子域健康检查持续失败问题

---

## [1.5.3] - 2026-07-20

### Added
- LOGO 应用到应用图标、侧边栏、关于窗口
- 托盘设置扩充三个开关（最小化到托盘、关闭到托盘、显示托盘通知）
- 设置页"恢复默认"按钮
- 下载加速服务（DownloadAccelerationService）
- Git Proxy 配置功能
- 速度测试对比卡片

### Changed
- 5 个 GitHub520 镜像合并为单个"GitHub520 (智能镜像)"源，自动故障转移
- 关于窗口更新为动态版本号 + 科控物联公司信息

### Fixed
- 修复 LogViewer 剪贴板 bug（改用 Avalonia 原生 Clipboard API）
- 修复 ComboBox 绑定失效问题
- 修复 EXE 文件图标缺失（添加 ApplicationIcon 属性）

---

## [1.5.0] - 2026-07-19

### Added
- 多源 DNS 探测源（DoH 并行查询 AliDNS、TencentDNS、Google、Cloudflare）
- HTTPS 直连 IP 探针（绕过 DNS）
- SSL 证书验证（拒绝中间人，允许 CDN SNI 不匹配）

---

## [1.4.0] - 2026-05-15

### Added
- GitHub 子域健康检查功能
- Hosts 分组管理功能
- Hosts 备份管理功能
- 数据导入导出功能

### Changed
- UI 全面现代化改造
- 性能监控系统完善

---

## [1.3.0] - 2026-05-01

### Added
- 智能数据源选择器（SmartSourceSelector）
- 数据源性能持久化（SourcePerformancePersistence）
- 操作历史记录功能
- Toast 通知系统

### Changed
- 数据源推荐算法优化
- 日志系统改用 Serilog

---

## [1.2.4] - 2026-04-23

### Added
- 在侧边栏底部和设置页面添加科控物联QQ:2492123056联系方式
- 创建自定义应用图标替换Avalonia默认图标
- 新增SpeedScore速度评分，优化数据源推荐算法

### Changed
- 数据源推荐算法优化：速度评分权重调整为响应时间60%、成功率20%、稳定性15%、完整性5%
- GetBestSource()优先按速度评分排序，确保推荐延迟最低的数据源
- 暗色主题适配优化：添加导航按钮暗色主题样式、文字颜色适配
- 整理项目文档结构：将散落文档和截图统一归档到docs目录

### Fixed
- 修复暗色主题下导航按钮文字不可见问题
- 修复暗色主题下导航按钮悬停/按下状态背景色不正确问题

---

## [1.2.3] - 2026-04-22

### Changed
- 恢复之前的数据源列表，包括 GitHub520、GitMirror 等
- 新增 ittuann/GitHub-IP-hosts 数据源
- 保留 GitCDN.top 和 ineo6/hosts 数据源
- 数据源总数增加到6个，提供更多选择

---

## [1.2.2] - 2026-04-22

### Fixed
- 修复 GitHub 延迟监控启动时机问题，现在应用启动时就开始执行监控
- 修复主页数据源列表显示不完整问题，增加底部边距
- 修复设置页面日志等级设置无法生效的问题

### Changed
- 数据源测试异常日志等级从 Error 改为 Debug，避免日志刷屏
- LogService 支持动态调整日志等级
- 设置变更时自动应用日志等级到 LogService

---

## [1.2.1] - 2026-04-22

### Fixed
- 修复侧边栏导航亮色主题配色问题，按钮背景色在亮色主题下显示正常
- 修复日志界面显示为空的问题，改用单例 LogService 并在应用启动时初始化
- 修复 LogService 跨线程更新 UI 的问题

### Changed
- 重构侧边栏导航按钮样式，使用 NavButton 类替代动态绑定
- LogService 改为单例模式，确保全局日志一致性
- 更新项目计划，将跨平台开发推迟，优先完善功能

---

## [1.2.0] - 2026-04-22

### Added
- 新增侧边栏导航设计，优化用户导航体验
- 新增图表悬停交互功能，支持数据点高亮和工具提示
- 新增骨架屏加载动画，提升加载状态用户体验
- 新增数据源搜索和排序功能
- 新增 Toast 通知系统，支持成功/警告/错误/信息四种类型
- 新增空状态显示，当搜索无结果时显示友好提示
- 新增图标辅助类 IconHelper，统一管理应用图标
- 新增页面切换动画，使用 TransitioningContentControl 实现平滑过渡

### Changed
- 重构导航结构，从顶部导航改为侧边栏导航
- 优化图表控件，添加悬停高亮效果
- 改进加载状态显示，使用骨架屏替代简单加载指示器
- 完善快捷键系统，支持 Ctrl+R、Ctrl+S、Ctrl+H、F5、Ctrl+1-4 等快捷键

---

## [1.1.0] - 2026-04-22

### Added
- 新增 Avalonia UI 现代化界面项目 (GithubAccelerator.UI)
- 新增数据源性能监控服务 (SourcePerformanceMonitor)
- 新增智能数据源选择器 (SmartSourceSelector)
- 新增数据源统计服务 (SourceStatisticsService)
- 新增自绘图表控件 (SimpleLineChart/SimpleBarChart)
- 新增延迟监控和性能图表功能
- 新增系统托盘功能，支持最小化到托盘
- 新增开机自启功能
- 新增数据导入导出功能
- 新增深色/浅色主题切换
- 新增操作日志查看功能
- 新增 Hosts 备份管理功能
- 新增 Hosts 分组管理功能
- 新增快捷键支持 (Ctrl+R, Ctrl+S, F5 等)
- 新增完整项目文档体系

### Changed
- 优化启动速度，延迟加载非关键服务
- 优化内存使用，限制历史数据大小
- 优化 UI 响应，使用虚拟化列表

### Fixed
- 修复关于窗口闪退问题 (ShowDialog owner 问题)
- 修复图表页面切换闪退问题 (替换第三方图表库为自绘控件)
- 修复跨线程 UI 更新异常
- 修复 XAML 绑定表达式错误

---

## [1.0.0] - 2026-04-15

### Added
- 初始版本发布
- 核心 Hosts 数据获取服务
- IP 测速优选功能
- 多数据源备份机制
- 自动刷新功能
- CLI 命令行界面
- Windows Forms 传统界面
- 基础单元测试

### Features
- 从多个数据源获取 GitHub Hosts
- 自动测速选择最优 IP
- 一键应用/清除 Hosts
- 自动备份 Hosts 文件
- 支持自定义数据源
