# GithubAccelerator 竞品分析与改进计划

> 文档版本：v1.5.0
> 创建日期：2026-04-21
> 最后更新：2026-04-22
> 分析范围：Hosts 文件管理、GitHub 加速、网络优化工具

---

## 一、竞品分析

### 1.1 主要竞品一览

| 产品名称 | 平台 | 技术栈 | Stars | 最后更新 | 特点 |
|---------|------|--------|-------|----------|------|
| **HostFileManager** | Windows | .NET WinForms | ~50 | 2019 | 基础的Hosts文件管理 |
| **SwitchHosts** | 多平台 | Electron + React | 12k+ | 2024 | 功能完善的Hosts管理工具 |
| **gasgot的Hosts** | Windows | Python/Tkinter | ~200 | 2020 | 命令行Hosts管理 |

### 1.2 核心竞品深度分析

#### 1.2.1 SwitchHosts (https://github.com/oldratlee/hosts)

**优点：**
- 多Hosts文件切换
- Hosts语法高亮
- 支持远程Hosts配置
- 良好的UI/UX设计
- 自动更新功能

**缺点：**
- 不支持自动测速
- 不支持一键应用
- 界面较复杂

#### 1.2.2 HostFileManager

**优点：**
- 轻量级
- 操作简单

**缺点：**
- 界面过时
- 功能单一
- 无自动化功能

### 1.3 行业最佳实践

#### 1.3.1 核心功能模式
1. **Hosts分组管理** - 按来源/用途分组
2. **一键切换** - 快速启用/禁用Hosts配置
3. **自动测速** - 多源测速选择最优
4. **云端同步** - 配置跨设备同步
5. **定时更新** - 自动更新Hosts数据

#### 1.3.2 UI/UX设计模式
1. **仪表盘概览** - 一目了然的健康状态
2. **快捷操作栏** - 常用功能一键访问
3. **实时反馈** - 操作结果即时展示
4. **暗色模式** - 保护眼睛的深色主题
5. **响应式布局** - 适应不同屏幕尺寸

#### 1.3.3 性能优化技术
1. **连接池复用** - HttpClient连接复用
2. **异步加载** - 非阻塞式数据加载
3. **缓存策略** - 本地缓存减少网络请求
4. **后台更新** - 静默更新不打扰用户
5. **增量更新** - 只下载变化部分

#### 1.3.4 安全实现
1. **权限最小化** - 只请求必要权限
2. **管理员模式** - 仅必要时提升权限
3. **配置备份** - 定期自动备份
4. **操作审计** - 记录所有修改操作
5. **数据加密** - 敏感信息加密存储

---

## 二、GithubAccelerator 现状评估

### 2.1 已实现功能
✅ Hosts文件读取和写入
✅ 多数据源性能监控
✅ 自动选择最优数据源
✅ 实时状态仪表盘
✅ Hosts备份与恢复
✅ 日志查看器
✅ 系统托盘集成
✅ 半透明主题支持
✅ 暗色主题支持 (v1.1.0)
✅ 快捷键支持 (v1.1.0)
✅ 操作历史记录 (v1.2.0)
✅ 通知提醒 (v1.2.0)
✅ Hosts分组管理 (v1.2.0)
✅ 数据导出/导入 (v1.2.0)

### 2.2 缺失功能（对比竞品）
❌ 远程Hosts配置
❌ 自动更新功能
❌ 配置文件云同步
✅ 性能图表
❌ 多语言支持

---

## 三、改进计划（按优先级排序）

### 3.1 高优先级改进（影响大、实现简单）

#### P0 - 立即实施

| 序号 | 改进项 | 影响 | 复杂度 | 预估工时 | 状态 |
|------|--------|------|--------|----------|------|
| 1 | **暗色主题支持** | ⭐⭐⭐⭐⭐ | 低 | 2h | ✅ 已完成 |
| 2 | **快捷键支持** | ⭐⭐⭐⭐ | 低 | 1h | ✅ 已完成 |
| 3 | **操作历史记录** | ⭐⭐⭐⭐ | 中 | 3h | ✅ 已完成 |
| 4 | **通知提醒** | ⭐⭐⭐ | 低 | 1h | ✅ 已完成 |

#### P1 - 计划实施

| 序号 | 改进项 | 影响 | 复杂度 | 预估工时 | 状态 |
|------|--------|------|--------|----------|------|
| 5 | **Hosts分组管理** | ⭐⭐⭐⭐⭐ | 中 | 4h | ✅ 已完成 |
| 6 | **性能图表** | ⭐⭐⭐⭐ | 中 | 4h | ✅ 已完成 |
| 7 | **数据导出/导入** | ⭐⭐⭐ | 中 | 3h | ✅ 已完成 |

### 3.2 中优先级改进（影响中、实现复杂）

#### P2 - 后续版本

| 序号 | 改进项 | 影响 | 复杂度 | 预估工时 |
|------|--------|------|--------|----------|
| 8 | **远程Hosts配置** | ⭐⭐⭐⭐ | 高 | 6h |
| 9 | **自动更新功能** | ⭐⭐⭐⭐ | 高 | 5h |
| 10 | **配置文件云同步** | ⭐⭐⭐⭐ | 高 | 8h |

### 3.3 低优先级改进（锦上添花）

#### P3 - 未来版本

| 序号 | 改进项 | 影响 | 复杂度 | 预估工时 |
|------|--------|------|--------|----------|
| 11 | **多语言支持** | ⭐⭐⭐ | 中 | 4h |
| 12 | **插件系统** | ⭐⭐⭐⭐ | 高 | 10h |
| 13 | **Web界面** | ⭐⭐⭐ | 高 | 12h |

---

## 四、详细改进方案

### 4.1 P0-1: 暗色主题支持 ✅ 已完成

**目标：** 支持亮色/暗色主题切换

**实现方案：**
1. 使用Avalonia的Theme支持
2. 添加主题切换按钮
3. 记住用户偏好设置

**技术要点：**
- 使用 `RequestedThemeVariant`
- 定义暗色配色方案
- 使用Converter处理动态颜色

### 4.2 P0-2: 快捷键支持 ✅ 已完成

**目标：** 提供键盘快捷键提升操作效率

**快捷键清单：**
| 快捷键 | 功能 |
|--------|------|
| Ctrl+R | 刷新数据源 |
| Ctrl+S | 应用Hosts |
| Ctrl+H | 查看Hosts内容 |
| Ctrl+, | 打开设置 |
| F5 | 启动/停止监控 |
| Esc | 最小化到托盘 |

### 4.3 P0-3: 操作历史记录

**目标：** 记录所有操作便于追溯

**记录内容：**
- 操作时间
- 操作类型
- 操作详情
- 操作结果

**实现方案：**
1. 创建操作历史数据库
2. 在每个操作时记录
3. 提供历史查看界面

### 4.4 P0-4: 通知提醒

**目标：** 通过系统通知提醒用户重要事件

**通知场景：**
- Hosts应用成功/失败
- 发现更优数据源
- 数据源离线
- 定时更新完成

**实现方案：**
- 使用Windows ToastNotification
- 支持用户配置通知开关

### 4.5 P1-5: Hosts分组管理

**目标：** 支持按组管理不同的Hosts配置

**功能清单：**
- 创建/删除分组
- 启用/禁用分组
- 分组排序
- 批量操作

---

## 五、版本规划

### v1.1.0 (2026-04-21) ✅ 已完成
- ✅ 暗色主题支持
- ✅ 快捷键支持

### v1.2.0 (2026-04-22) ✅ 已完成
- ✅ 操作历史记录
- ✅ 通知提醒
- ✅ Hosts分组管理
- ✅ 数据导出/导入

### v1.3.0 (2026-04-22) ✅ 已完成
- ✅ 性能图表（响应时间趋势图、源对比柱状图、评分分布图）
- ✅ Canvas 自定义图表渲染（折线图、柱状图、面积图）
- ✅ 图表类型切换（趋势图/对比图/评分图）
- ✅ 性能数据实时刷新
- ✅ 图例显示和数据范围标注
- ✅ GitHub 访问延迟实时监控（2 秒刷新，实时曲线）
- ✅ Hosts 分组功能完善（UI 优化、命令绑定修复、通知反馈）

### v2.0.0 (远期)
- 配置文件云同步
- 插件系统
- Web界面

---

## 六、技术参考

### 6.1 相关开源项目
- SwitchHosts: https://github.com/oldratlee/hosts
- gasgot/Hosts: https://github.com/gasgot/Hosts

### 6.2 技术文档
- Avalonia主题文档
- Windows ToastNotification 文档
- Serilog日志文档

---

## 七、v1.1.0 实施记录

### 2026-04-21 实施内容

#### 1. 暗色主题支持
- **文件**: `Services/ThemeManager.cs` (新增)
- **文件**: `App.axaml.cs` (修改)
- **文件**: `ViewModels/MainWindowViewModel.cs` (修改)
- **文件**: `Views/MainWindow.axaml` (修改)
- **功能**:
  - 点击底部栏 ☀️/🌙 按钮切换主题
  - 使用 Avalonia RequestedThemeVariant 实现
  - 支持亮色/暗色主题切换

#### 2. 快捷键支持
- **文件**: `Views/MainWindow.axaml` (修改)
- **功能**:
  - `Ctrl+R` - 刷新数据源
  - `Ctrl+S` - 应用 Hosts
  - `Ctrl+H` - 查看 Hosts 内容
  - `F5` - 启动/停止监控

### 技术说明
- ThemeManager 在 App.Initialize() 时初始化
- MainWindowViewModel 代理 ThemeManager 状态
- 使用 CommunityToolkit.Mvvm 的 [RelayCommand] 实现命令

---

## 八、变更日志

### v1.2.0 (2026-04-22)
#### 新增功能
- 操作历史记录（记录所有操作，支持查看和清除）
- 通知提醒（系统通知反馈操作结果）
- Hosts分组管理（创建/删除/启用/禁用分组，添加/移除/切换规则）
- 数据导出/导入（ZIP压缩格式导出，支持导入恢复）

#### 新增文件
- `Services/OperationHistoryService.cs` - 操作历史记录服务
- `Services/NotificationService.cs` - 系统通知服务
- `Services/HostsGroupService.cs` - Hosts分组管理服务
- `Services/DataExportImportService.cs` - 数据导出/导入服务
- `ViewModels/HostsGroupViewModel.cs` - 分组管理视图模型
- `Views/HostsGroupView.axaml` / `.axaml.cs` - 分组管理视图

#### 技术改进
- 使用单例模式管理核心服务（HostsGroupService, DataExportImportService）
- 数据导出使用 ZIP 压缩格式，同时兼容 JSON 格式导入
- Hosts 分组支持持久化存储到 AppData 目录
- 修复 MVVMTK0034 警告（使用生成属性替代字段引用）
- 单元测试使用唯一标识避免并行冲突（113个测试全部通过）

#### Bug 修复
- 修复主题切换后 Hosts 查看界面不正常问题
- 修复备份按钮点击卡住问题（改为异步操作）
- 修复 Avalonia 11 文件对话框 API 兼容问题（使用 StorageProvider）

### v1.1.0 (2026-04-21)
#### 新增功能
- 暗色主题支持（点击底部栏 ☀️/🌙 按钮切换）
- 快捷键支持（Ctrl+R/S/H, F5）

#### 技术改进
- 新增 ThemeManager 服务类统一管理主题状态
- App.axaml.cs 中初始化 ThemeManager

#### Bug 修复
- 修复主题切换不生效问题
- 修复查看 Hosts 按钮无响应问题

### v1.0.0 (2026-04-20)
#### 初始版本
- Hosts 文件读取和写入
- 多数据源性能监控
- 自动选择最优数据源
- 实时状态仪表盘
- Hosts 备份与恢复
- 日志查看器
- 系统托盘集成

### v1.3.0 (2026-04-22)
#### 新增功能
- 性能图表（响应时间趋势图、源对比柱状图、评分分布图）
- Canvas 自定义图表渲染（折线图、柱状图、面积图）
- 图表类型切换（趋势图/对比图/评分图）
- 性能数据实时刷新
- 图例显示和数据范围标注

#### 新增文件
- `Services/PerformanceChartService.cs` - 性能图表数据服务
- `ViewModels/PerformanceChartViewModel.cs` - 图表视图模型
- `ViewModels/PerformanceChartConverters.cs` - 图表值转换器
- `Views/PerformanceChartView.axaml` / `.axaml.cs` - 图表视图

#### 技术改进
- 使用 StreamGeometry 实现高性能矢量图形渲染
- Canvas 自定义绘制支持网格线、坐标轴、数据点标记
- 支持多数据系列同时显示，自动颜色分配
- 响应式布局适配不同窗口尺寸
- 数据缓存机制减少重复计算

#### 单元测试
- ChartDataPointTests (2 个测试)
- ChartSeriesTests (2 个测试)
- PerformanceChartServiceTests (7 个测试)
- 修复 ExportImport_Roundtrip 测试并行冲突问题
- 全部 124 个测试通过

#### Bug 修复
- 修复 Hosts 分组 UI 命令绑定问题（ItemsControl 中的命令路径）
- 修复分组操作后 UI 不刷新的问题（改用局部更新而非全量刷新）
- 修复 ToggleGroup 命令状态显示错误
- 优化通知反馈，所有操作都有明确提示
- 修复删除分组后选中项更新问题
- 修复"应用此源"按钮 hover 状态文字看不清的问题（使用主色背景 + 白字）
- 修复 GitHub 延迟曲线显示异常问题（Y 轴范围计算错误导致曲线被压缩）

#### 新增功能
- GitHub 延迟监控服务（每 2 秒自动检测）
- 实时延迟曲线显示（动态 Y 轴，面积图效果）
- 延迟状态评估（优秀/良好/一般/较差）
- 成功率统计和展示
- Hosts 分组重命名功能（输入新名称后点击重命名按钮）

### v1.4.1 (2026-04-22)
#### Bug 修复
- 修复性能曲线和延迟曲线不更新的问题
  - 原因：ObservableCollection 的 CollectionChanged 事件未被正确监听
  - 解决方案：在 GitHubLatencyView 和 PerformanceChartView 中添加 CollectionChanged 事件处理
  - 文件：`Views/GitHubLatencyView.axaml.cs`, `Views/PerformanceChartView.axaml.cs`
- 修复编译错误：添加缺失的 `using System.Collections.Generic;` 指令
  - 文件：`Views/PerformanceChartView.axaml.cs`

#### 技术改进
- 优化图表更新逻辑：监听所有 PropertyChanged 事件而非单个属性
- 确保集合变化时自动重绘图表
- 修复后曲线实时更新正常，数据变化立即可见

### v1.4.2 (2026-04-22)
#### Bug 修复
- 修复延迟曲线显示全为 0 的问题
  - 原因 1：监控服务需要手动启动，用户未点击 Start 按钮
  - 解决：ViewModel 构造函数中自动启动监控服务
  - 原因 2：GetRecentRecords 返回新的 ObservableCollection 导致性能问题
  - 解决：改为返回 `List<GitHubLatencyRecord>` 避免重复创建集合
  - 原因 3：UpdateFromService 每次清空重建集合导致性能问题
  - 解决：改为增量更新，只在数据变化时更新集合
  - 原因 4：空状态显示条件错误（Count 永远不为 null）
  - 解决：改为基于 IsMonitoring 状态显示空状态提示
  - 文件：`ViewModels/GitHubLatencyViewModel.cs`, `Services/GitHubLatencyMonitorService.cs`, `Views/GitHubLatencyView.axaml`

#### 技术改进
- 监控服务自动启动，无需用户手动操作
- 优化数据更新逻辑，减少不必要的集合重建
- 改进空状态提示，引导用户理解监控状态

### v1.5.0 (2026-04-22)
#### 重大改进 - UI/UX 重构
根据用户反馈进行了全面的界面优化：

##### 仪表盘页面简化（极简模式）
- 移除冗余内容，只保留核心监控指标和快速操作
- Hosts 文件查看功能移至独立页面
- 操作历史记录移至独立页面
- 界面更加清爽，操作更加直观

##### 数据源多选功能
- 每个数据源添加复选框，支持多选
- 新增"全选"/"取消"快捷按钮
- 显示已选数据源数量
- 支持批量应用选中的数据源
- 每个数据源保留独立"应用"按钮

##### 深色主题适配
- 修复 Hosts 文件内容在深色主题下不可见的问题
- 使用动态资源替代硬编码颜色
- 完善主题切换体验

##### 默认设置优化
- 测试间隔从 60 秒调整为 30 秒，更快响应网络变化
- 监控服务自动启动，无需手动操作

#### 新增命令
- `ApplySelectedSourcesCommand` - 批量应用选中的数据源
- `SelectAllSourcesCommand` - 全选所有数据源
- `DeselectAllSourcesCommand` - 取消全选
- `ApplySingleSourceCommand` - 应用单个数据源

#### 文件变更
- `Views/DashboardView.axaml` - 完全重构，极简设计
- `ViewModels/MainWindowViewModel.cs` - 新增多选相关属性和命令
- `ViewModels/SettingsViewModel.cs` - 调整默认测试间隔
- `ViewModels/GitHubLatencyViewModel.cs` - 自动启动监控
- `Services/GitHubLatencyMonitorService.cs` - 优化数据返回类型

### v1.5.1 (2026-04-22)
#### Bug 修复
- 修复延迟监控无法获取数据的问题
  - 原因：仅测试 `api.github.com`，在中国大陆可能无法访问
  - 解决：使用多个测试地址（`github.com/favicon.ico`、`github.com/`、`api.github.com/`）
  - 只要有一个地址成功即记录延迟
  - 文件：`Services/GitHubLatencyMonitorService.cs`
- 修复延迟曲线显示异常的问题
  - 原因1：Y 轴固定为 1000ms，当实际延迟较小时曲线被压缩
  - 解决1：改为动态 Y 轴，根据实际数据自动调整范围（实际最大值 × 1.2）
  - 原因2：失败记录（延迟为 -1 或 0）被绘制在底部
  - 解决2：过滤掉无效记录（LatencyMs ≤ 0），只显示有效数据
  - 文件：`Views/GitHubLatencyView.axaml.cs`

#### 新增功能
- 添加 Hosts 内容查看导航入口
  - 在顶部导航栏添加 "📄 Hosts" 按钮
  - 创建专门的 HostsContentView 视图
  - 支持刷新和用记事本打开功能
  - 文件：`Views/HostsContentView.axaml`, `Views/MainWindow.axaml`
- 数据源自动检测
   - 应用启动时自动开始检测所有数据源状态
   - 无需手动点击"开始"按钮
   - 始终保持监控运行，实时更新数据源状态
   - 文件：`ViewModels/MainWindowViewModel.cs`

### v1.5.2 (2026-04-22)
#### Hosts 文件管理优化
- 实现**标记区域管理**功能，解决 Hosts 文件无限增长问题

##### 标记区域机制
```
# === GitHub Accelerator Start ===
... 我们添加的 hosts 内容 ...
# === GitHub Accelerator End ===
```

##### 核心改进
- **ApplyGithubHostsAsync**：写入内容时自动包裹标记
- **RemoveGithubHostsBlock**：移除标记区域内的所有内容（包括标记本身）
- **效果**：每次应用新数据源时，先清除旧标记区域，再添加新内容
- **Hosts 文件大小恒定**：不会因为重复操作而无限增长

##### 新增功能
- **ClearAppliedHostsCommand**：一键清除我们添加的 Hosts 内容
  - 只删除标记区域内的内容
  - 不影响用户原有的 Hosts 配置
  - 操作前自动备份
  - 在仪表盘显示 "🗑️ 清除Hosts" 按钮（仅当已应用时可见）

##### 文件变更
- `Services/WindowsHostsFileService.cs` - 添加标记区域支持
- `ViewModels/MainWindowViewModel.cs` - 新增 ClearAppliedHosts 命令
- `Views/DashboardView.axaml` - 添加清除按钮

### v1.5.3 (2026-04-22)
#### 延迟曲线重构 - 暂时移除 LiveCharts2
- **替换 Canvas 手绘为列表显示（临时方案）**

##### 问题
- LiveCharts2 (SkiaSharpView.Avalonia) 在 Avalonia 12 中存在兼容性问题
  - 初始化时导致应用闪退
  - SkiaSharp 渲染引擎与 Avalonia 12 的集成不稳定

##### 临时解决方案
使用**数据列表**替代图表显示延迟历史：
- 显示当前延迟值（大字体）
- 显示平均延迟值
- 延迟历史记录列表（时间 + 延迟值）
- 底部统计卡片（Current/Average/Min/Success）

##### 后续计划
1. 等待 LiveCharts2 发布 Avalonia 12 正式支持版本
2. 或考虑使用其他图表库（如 OxyPlot.Avalonia）
3. 或自行实现轻量级 Canvas 图表组件

##### 文件变更
- `Views/GitHubLatencyView.axaml` - 改用 ItemsControl 列表显示
- `ViewModels/GitHubLatencyViewModel.cs` - 移除 LiveCharts2 依赖，添加 LatencyRecords 属性

---

*文档由 GithubAccelerator 开发团队维护*
*最后更新：2026-04-22*
