# GitHub 加速器操作手册

## 目录

1. [功能简介](#功能简介)
2. [系统要求](#系统要求)
3. [快速开始](#快速开始)
4. [命令行操作](#命令行操作)
5. [Windows 图形界面](#windows-图形界面)
6. [Linux 服务器部署](#linux-服务器部署)
7. [Git 代理配置](#git-代理配置)
8. [故障排除](#故障排除)

---

## 功能简介

GitHub 加速器通过以下方式提升 GitHub 访问速度：

1. **Hosts 优化** - 自动获取最新的 GitHub IP 地址并写入系统 hosts 文件
2. **IP 测速优选** - 对多个 IP 进行测速，选择最优的 IP 使用
3. **HTTP 代理** - 提供本地代理服务，进一步加速访问
4. **自动刷新** - 每 2 小时自动更新 hosts 文件

---

## 系统要求

| 平台 | 最低版本 | 权限要求 |
|------|----------|----------|
| Windows | Windows 10+ | 管理员权限 |
| Linux | Ubuntu 18.04+ / CentOS 8+ | root 权限 |
| macOS | macOS 10.15+ | sudo 权限 |

---

## 快速开始

### Windows

```powershell
# 克隆项目
git clone https://github.com/your-repo/GithubAccelerator.git
cd GithubAccelerator

# 编译
dotnet build

# 以管理员身份运行
dotnet run --project GithubAccelerator
```

或直接运行编译后的 exe 文件。

### Linux 服务器

```bash
# 克隆项目
git clone https://github.com/your-repo/GithubAccelerator.git
cd GithubAccelerator

# 编译 (需要先安装 .NET 10 SDK)
dotnet publish -c Release -r linux-x64 --self-contained

# 以 root 身份运行
sudo ./bin/Release/net10.0/linux-x64/GithubAccelerator
```

---

## 命令行操作

启动程序后，输入单字母命令进行操作：

| 命令 | 功能 | 说明 |
|------|------|------|
| `1` / `s` | 检查状态 | 查看当前加速状态、Hosts 记录数 |
| `2` / `a` | 启用加速 | 获取并应用最优 Hosts 配置 |
| `3` / `r` | 恢复原状 | 移除 GitHub 相关 Hosts 记录 |
| `4` / `f` | 刷新 Hosts | 重新获取最新 Hosts 数据并测速 |
| `5` / `t` | 测试连接 | 测试 GitHub 各端点的连接速度 |
| `6` / `p` | 查看预览 | 显示 Hosts 预览内容 |
| `7` / `x` | 启动/停止代理 | 切换 HTTP 代理服务状态 |
| `8` / `g` | 配置 Git 代理 | 为 Git 设置代理 |
| `0` / `?` 或 `？` | 显示帮助 | 显示所有可用命令 |
| `q` | 退出程序 | 退出 CLI |

### 命令示例

```bash
# 查看当前状态
s

# 启用加速
a

# 测试连接
t

# 启动本地代理
x

# 配置 Git 使用代理
g

# 退出
q
```

---

## Windows 图形界面

### 主界面功能

1. **状态显示** - 实时显示加速状态、连接延迟
2. **一键启用** - 点击"启用加速"按钮自动配置
3. **一键恢复** - 点击"恢复原状"按钮还原 hosts
4. **手动刷新** - 手动刷新 Hosts 数据
5. **连接测试** - 测试 GitHub 各端点连接
6. **代理控制** - 启动/停止本地 HTTP 代理
7. **开机自启** - 勾选开机自动启动

### 系统托盘

程序支持最小化到系统托盘：

- 右键托盘图标显示菜单
- 双击图标恢复窗口
- 关闭按钮最小化而非退出

---

## Linux 服务器部署

### 部署步骤

#### 1. 安装 .NET 运行时

```bash
# Ubuntu/Debian
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0.0 --runtime dotnet

# CentOS/RHEL
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0.0 --runtime dotnet
```

#### 2. 编译项目

```bash
git clone https://github.com/your-repo/GithubAccelerator.git
cd GithubAccelerator
dotnet publish -c Release -r linux-x64 --self-contained -o ./publish
```

#### 3. 复制到服务器

```bash
scp -r ./publish user@your-server:/opt/github-accelerator
```

#### 4. 运行程序

```bash
cd /opt/github-accelerator
sudo ./GithubAccelerator
```

### 开机自启 (systemd)

程序会自动创建 systemd user service：

```bash
# 在程序内启用自启
x  # 启动代理（会自动创建服务）

# 手动管理服务
systemctl --user status github-accelerator
systemctl --user enable github-accelerator
systemctl --user start github-accelerator
```

### 常用命令

```bash
# 启用加速
sudo ./GithubAccelerator
# 输入 a 启用加速

# 测试连接
# 输入 t 测试连接

# 查看帮助
# 输入 ? 查看所有命令
```

---

## Git 代理配置

### 自动配置

程序提供 `g` 命令自动配置 Git 代理：

```
Command: g
请输入代理地址 (直接回车使用本地代理 localhost:7890):
```

输入代理地址后，程序会自动配置：

```bash
# 设置 HTTP/HTTPS 代理
git config --global http.proxy http://localhost:7890
git config --global https.proxy http://localhost:7890

# 取消代理
git config --global --unset http.proxy
git config --global --unset https.proxy
```

### 手动配置

```bash
# 使用 HTTP 代理
git config --global http.proxy http://proxy:port
git config --global https.proxy http://proxy:port

# 使用 SOCKS5 代理
git config --global http.proxy socks5://proxy:port
git config --global https.proxy socks5://proxy:port

# 查看配置
git config --global --get http.proxy

# 取消代理
git config --global --unset http.proxy
```

---

## 故障排除

### Windows

#### 问题：无法修改 hosts 文件

**解决方法**：
1. 以管理员身份运行程序
2. 检查 hosts 文件是否被其他程序占用

#### 问题：DNS 缓存未刷新

**解决方法**：
```powershell
ipconfig /flushdns
```

#### 问题：代理无法启动

**解决方法**：
1. 检查端口 7890 是否被占用
2. 关闭占用端口的程序

### Linux

#### 问题：Permission denied (/etc/hosts)

**解决方法**：
```bash
sudo ./GithubAccelerator
```

#### 问题：DNS 缓存刷新失败

**解决方法**：
```bash
# 手动刷新 DNS
sudo systemd-resolve --flush-caches
# 或
sudo resolvectl flush-caches
# 或
sudo nscd -i hosts
```

#### 问题：systemd 服务无法启动

**解决方法**：
```bash
# 检查服务状态
journalctl --user -u github-accelerator -f

# 重新加载配置
systemctl --user daemon-reload

# 查看错误日志
cat ~/.config/systemd/user/github-accelerator.service
```

### 通用

#### 问题：GitHub 连接超时

**解决方法**：
1. 尝试刷新 Hosts：`f`
2. 测试不同端点：`t`
3. 使用代理：`x` 启动代理，`g` 配置 Git 使用代理

#### 问题：测速结果不理想

**解决方法**：
1. 手动刷新：`f`
2. 等待自动刷新（每 2 小时）
3. 检查网络环境

---

## 技术细节

### Hosts 数据源

- 主数据源：GitHub520 项目
- 更新频率：每 2 小时自动检查

### 测速机制

1. 对每个 IP 进行 HTTP HEAD 请求测速
2. 选择延迟最低的 3 个 IP 作为最优 IP
3. 测速超时：5 秒

### 代理服务

- 默认端口：7890
- 支持 HTTP/HTTPS 代理
- 透明代理功能

---

## 附录

### 文件位置

| 文件 | Windows | Linux |
|------|---------|-------|
| Hosts 文件 | `C:\Windows\System32\drivers\etc\hosts` | `/etc/hosts` |
| 备份文件 | `C:\Windows\System32\drivers\etc\hosts.backup` | `/etc/hosts.backup` |
| 日志目录 | `%APPDATA%\GithubAccelerator` | `~/.config/github-accelerator` |

### 端口使用

| 端口 | 用途 |
|------|------|
| 7890 | HTTP 代理默认端口 |

### 相关链接

- GitHub520 项目：https://github.com/521xueweihan/GitHub520
- .NET 下载：https://dotnet.microsoft.com/download
