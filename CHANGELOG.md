# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- 待添加新功能

### Changed
- 待变更内容

### Deprecated
- 待废弃内容

### Removed
- 待移除内容

### Fixed
- 待修复问题

### Security
- 待安全更新

---

## [1.2.3] - 2026-04-22

### Changed
- 重新测试并更新 GitHub hosts 数据源列表，移除所有不可用的数据源
- 仅保留经过测试确认可用的2个数据源：GitCDN.top 和 ineo6/hosts
- 确保数据源在添加前都经过可用性测试

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

### Security
- 移除敏感信息，确保代码安全

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

---

## Version History

| Version | Release Date | Type | Description |
|---------|--------------|------|-------------|
| 1.1.0 | 2026-04-22 | Minor | 现代化 UI，性能监控 |
| 1.0.0 | 2026-04-15 | Major | 初始版本发布 |

---

## Release Types

- **Major**: 不兼容的 API 变更
- **Minor**: 向后兼容的功能新增
- **Patch**: 向后兼容的问题修复

---

## Contributing

请参阅 [CONTRIBUTING.md](CONTRIBUTING.md) 了解如何为此项目做出贡献。

---

## License

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件。
