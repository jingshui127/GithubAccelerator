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

## [1.2.0] - 2026-04-22

### Added
- 新增侧边栏导航设计，优化用户导航体验
- 新增图表悬停交互功能，支持数据点高亮和工具提示
- 新增骨架屏加载动画，提升加载状态用户体验
- 新增数据源搜索和排序功能
- 新增 Toast 通知系统，支持成功/警告/错误/信息四种类型

### Changed
- 重构导航结构，从顶部导航改为侧边栏导航
- 优化图表控件，添加悬停高亮效果
- 改进加载状态显示，使用骨架屏替代简单加载指示器

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
