# API 驱动本地浏览器（只读优先）设计稿

文档状态：**设计稿，未实施**（本轮只做实测与设计，不改业务代码）
编写时间：2026-11-06（Asia/Shanghai）
适用产品：GithubAccelerator（自用版，无任何上游出口）
前置证据：路线 A 反代镜像实测见 §11；`api.github.com` 实测见 §2.0

---

## 1. 能力边界（先看这一节）

产品要解决的是"官网页面打不开时还能看代码仓库"。本方案用 GitHub REST API + 本地渲染，
不打开网页、不模拟登录、不接管流量。

### 1.1 能做什么（只读为主）

| 能力 | 依赖 | 是否需要 token |
|---|---|---|
| 查看仓库元数据（描述、star、默认分支、语言、license） | `GET /repos/{owner}/{repo}` | 否 |
| 浏览仓库目录树（含子目录、文件大小） | `GET /repos/{owner}/{repo}/git/trees/{ref}?recursive=1` | 否 |
| 查看单个文件内容（文本/二进制判定） | `GET /repos/{owner}/{repo}/contents/{path}` | 否 |
| 阅读 README（Markdown 渲染） | `GET /repos/{owner}/{repo}/readme` | 否 |
| 阅读 issue / PR 列表与正文、评论 | `GET /repos/.../issues`、`/pulls`、`/comments` | 否 |
| 搜索仓库 / issue（关键词） | `GET /search/repositories`、`/search/issues` | 否（限流更严） |
| 下载源码 zip | `GET /repos/.../zipball`（302 到 codeload 签名 URL） | 否（私有仓库需要） |
| 建 issue、发评论 | `POST /repos/.../issues`、`/issues/{n}/comments` | **是**（`public_repo`） |

### 1.2 做不到什么（必须让用户看见）

| 做不到 | 原因 | 产品里的说法 |
|---|---|---|
| 网页登录（账号密码 / OAuth 网页流程 / SSO） | API 不接受网页会话，本方案不做浏览器 | "本页不提供网页登录" |
| Settings、个人资料、SSH/GPG key 管理 | 需要网页会话或更高权限，且属写操作 | 直接不提供入口 |
| Actions 运行日志、Job 步骤输出 | 日志需下载 zip 或网页会话；API 仅给元数据 | 只展示运行状态，不给日志 |
| 网页特有内容（Projects、Discussions、Wiki 编辑、Marketplace） | 无对应稳定 REST 端点 | 不提供 |
| JS 交互（代码行内跳转、懒加载 diff） | 本地渲染为静态内容 | 说明"只读快照" |
| 绕过网络封锁 | 仍要连 `api.github.com` | 连不上就报错，不静默失败 |

**定位一句话**：这是"页面打不开时的降级只读入口"，不是网页浏览器的替代品。

---

## 2. API 端点映射表

### 2.0 可行性实测（2026-11-06 15:20:2x，curl 8.13 / Schannel）

| 请求 | 状态码 | 耗时（两次） | 字节 |
|---|---|---|---|
| `GET https://api.github.com/repos/jingshui127/GithubAccelerator` | 200 / 200 | 0.616s / 0.335s | 5638 |
| `GET https://api.github.com/repos/jingshui127/GithubAccelerator/issues?per_page=5&state=all` | 200 / 200 | 0.600s / 0.323s | 2（该仓库无 issue） |

结论：`api.github.com` 在本机当前网络下**可用且快（<0.7s）**，而同一时刻 `github.com` 页面路径 12s 超时。
这构成路线 B 的核心可行性证据。**注意**：仅为单机单时段实测，跨网络/跨时段未验证。

### 2.1 功能 → 端点

| 功能 | 方法与端点 | token | 单次配额 | 备注 |
|---|---|---|---|---|
| 仓库元数据 | `GET /repos/{o}/{r}` | 可选 | 1 | 未认证 60/h 的主要消耗点 |
| README | `GET /repos/{o}/{r}/readme` | 可选 | 1 | `Accept: application/vnd.github.raw+json` 取原文，本地渲染 |
| 目录树 | `GET /repos/{o}/{r}/git/trees/{ref}?recursive=1` | 可选 | 1 | 返回 `truncated=true` 时需按目录分批拉取 |
| 单文件 | `GET /repos/{o}/{r}/contents/{path}` | 可选 | 1 | 大文件返回 `download_url`，需二次请求 |
| issue 列表 | `GET /repos/{o}/{r}/issues?state=all&per_page=30` | 可选 | 1 | PR 也会混入，需按 `pull_request` 字段过滤 |
| issue 详情 + 评论 | `GET /repos/{o}/{r}/issues/{n}`、`/issues/{n}/comments` | 可选 | 1 + 1 | 评论分页 |
| PR 列表/详情 | `GET /repos/{o}/{r}/pulls`、`/pulls/{n}` | 可选 | 1 | 不含 diff 全文 |
| 仓库搜索 | `GET /search/repositories?q=` | 可选 | 1（独立限流） | 未认证 10 次/分；认证 30 次/分 |
| issue 搜索 | `GET /search/issues?q=repo:{o}/{r}+{kw}` | 可选 | 1（独立限流） | 同上 |
| zip 下载 | `GET /repos/{o}/{r}/zipball/{ref}` → 302 | 可选 | 1（下载本身不计配额） | 签名 URL 有效期短，需流式保存 |
| 建 issue | `POST /repos/{o}/{r}/issues` | **必需** | 1 | scope `public_repo`；M3 才做 |
| 发评论 | `POST /repos/{o}/{r}/issues/{n}/comments` | **必需** | 1 | 同上 |
| 配额自查 | `GET /rate_limit` | 可选 | 0 | 不计配额，可在状态栏轮询 |

### 2.2 限流差异（必须写进 UI 与文档）

| 身份 | 核心配额 | 搜索配额 | 影响 |
|---|---|---|---|
| 未认证（无 token） | **60 次/小时，按出口 IP** | 10 次/分 | 浏览一个中等仓库（元数据 + 树 + README + issue 列表）约 4–6 次，10 来个仓库就耗尽；共享 IP 可能被别人耗尽 |
| 认证（PAT） | **5000 次/小时，按用户** | 30 次/分 | 日常浏览足够；仍受二级限流（短时间高频）约束 |
| 条件请求命中 304 | **不计配额**（GitHub 官方行为） | 同左 | 缓存命中时 304 不消耗 60/h |

推论：**不登录也应可用，但必须缓存**；否则 60/h 会在几十分钟内被自己耗光。

---

## 3. Token 处理

### 3.1 存储位置与加密

| 项 | 方案 |
|---|---|
| 加密方式 | Windows **DPAPI**（`ProtectedData.Protect`，`DataProtectionScope.CurrentUser`），只绑定当前 Windows 用户 |
| 落盘路径 | `%LOCALAPPDATA%\GithubAccelerator\credentials.dat`（二进制密文，文件权限随用户目录） |
| 明文落盘 | **一律否决**：不写 JSON、不写注册表明文、不写日志、不写崩溃转储可读字段 |
| 实现依赖 | `System.Security.Cryptography.ProtectedData`（NuGet 包，仅 Windows 分支引用） |
| 内存态 | 解密后仅在内存持有；用 `char[]`/受限生命周期，不参与字符串拼接日志 |

### 3.2 是否可完全不存

可以，且是默认：

- 默认模式 = **不保存**（会话内内存 token，退出即失效）。
- 用户显式勾选"记住此 token（本机加密保存）"才落盘。
- 无 token 时功能自动降级为只读，不阻断主流程（见 §4.3）。

### 3.3 撤销与清除

| 动作 | 产品行为 |
|---|---|
| 产品内"清除 token" | 删除 `credentials.dat` + 清空内存 + 立即刷新配额显示 |
| 用户在 GitHub 撤销 | Settings → Applications → 撤销该 PAT；产品下次请求返回 401，提示"token 已失效，请重新生成或改用只读模式" |
| 换机 / 重装系统 | DPAPI 密文不可解密 → 视为无 token，提示重新输入 |

### 3.4 硬约束

- token **只发往 `api.github.com`**，绝不随镜像/反代请求携带（避免第三方中转拿到凭据）。
- 请求日志中的 `Authorization` 必须脱敏为 `Authorization: token ****`。
- 错误弹窗、Toast、导出文件不得包含 token 片段（测试断言覆盖）。

---

## 4. 限流与缓存

### 4.1 条件请求

- 每个 GET 保存 `ETag`；下次带 `If-None-Match`。
- 收到 `304`：用本地缓存，**不消耗配额**，UI 标注"缓存内容（最后更新 HH:mm）"。
- 收到 `200`：更新缓存与 ETag。

### 4.2 本地缓存表

| 资源 | 目录 | 建议 TTL | 键 |
|---|---|---|---|
| 仓库元数据 | `cache/repos/{o}/{r}/meta.json` | 10 分钟 | ETag |
| README | `cache/repos/{o}/{r}/readme.md` | 30 分钟 | ETag |
| 目录树 | `cache/repos/{o}/{r}/tree-{ref}.json` | 30 分钟 | ETag |
| issue 列表 | `cache/repos/{o}/{r}/issues-{state}-{page}.json` | 5 分钟 | ETag |
| issue 详情/评论 | `cache/.../issue-{n}.json` | 10 分钟 | ETag |
| 搜索结果 | 内存（不落盘） | 2 分钟 | 查询串 |

TTL 到期不删除，只标记"过期"，先发条件请求；网络不可达时**允许展示过期缓存**并明确标注
"离线缓存，最后更新 …"，而不是空白页。

### 4.3 超限与失败的可见提示（禁止静默失败）

| 情况 | 判定 | 文案（人话） |
|---|---|---|
| 配额耗尽 | `403` + `x-ratelimit-remaining: 0` | "GitHub 接口配额已用完（未登录 60 次/小时）。将于 15:42 恢复；现在可查看已缓存内容。" |
| 二级限流 | `403` + `retry-after` | "请求过于频繁，请等候 30 秒再试。" |
| token 失效 | `401` | "token 无效或已被撤销，已切回只读模式。" |
| 仓库不存在 / 无权限 | `404` | "仓库不存在，或私有仓库需要 token。" |
| 网络不可达 | 超时 / DNS / TLS | "连不上 api.github.com（超时 10s）。可先跑'官网访问诊断'看网络状况。" |
| 服务端错误 | `5xx` | "GitHub 服务暂时异常（502），请稍后重试。" |

---

## 5. 与现有产品的关系

现有四条通道的分工，避免功能重叠与"多写一处就坏一处"：

| 通道 | 解决什么 | 默认路径？ | 边界 |
|---|---|---|---|
| **直连官网**（现状） | 页面浏览、登录、Settings、Actions | **是（默认）** | 时变：2026-11-06 13:31 实测 200/1.56s，15:19 实测 12s 超时 |
| **Hosts 诊断**（M1 已上线） | 解析层择优，判断"是不是 DNS 问题" | 诊断用，不改默认访问 | 只读，不写 Hosts；解析层实测非瓶颈 |
| **反代镜像 / raw 代理** | raw 文件、zip、release 资源下载 | 下载时按可用站点择优 | 页面不可用（见 §11），只做文件，不做浏览 |
| **API 浏览器**（本设计） | 页面打不开时的只读浏览（仓库树/README/issue） | **否，用户显式进入或诊断失败后建议** | 无登录、无 Settings、无 Actions 日志 |

**默认路径结论**：直连页面仍是默认；API 浏览器是**降级入口**，不自动抢占默认。
触发建议时机只有一个：`官网访问诊断` 判定页面不可用（TCP/TLS 失败或超时）时，
在结论区给一个按钮"改用 API 只读浏览"，点击才进入。

---

## 6. UI 集成点

| 项 | 方案 |
|---|---|
| 导航位置 | "主要功能"分组，紧跟"官网访问诊断"之后，新增 `📚 仓库浏览（API）`；不改现有五项顺序 |
| 快捷键 | 暂不分配（Ctrl+5 已被占用/易冲突），M1 不加 |
| 首屏形态 | 顶部：`owner/repo` 输入框 + "打开"按钮 + 最近浏览（最多 5 条）；主体：左侧目录树、右侧 README/文件/issue 切换（Tab：README / 文件 / Issues） |
| 顶栏状态 | 右侧显示配额：`API 配额 57/60 · 未登录` 或 `4980/5000 · 已认证`，来源 `GET /rate_limit` |
| 空态文案 | "输入 `owner/repo` 开始浏览，例如 `jingshui127/GithubAccelerator`。未登录每小时可请求 60 次，登录 token 后 5000 次。" |
| 缓存态文案 | "缓存内容 · 最后更新 14:31"（灰字，不弹窗） |
| 错误态文案 | 见 §4.3 表格，逐条对应，不用"操作失败"这种空话 |
| 写操作入口（M3） | 只在有 token 时显示"新建 issue""评论"按钮；无 token 时置灰并 tooltip"需要 token（只读模式）" |

---

## 7. 不做清单

1. **不做内置代理**（HTTP/SOCKS/PAC），不改系统代理设置。
2. **不做 MITM**，不安装/信任任何证书。
3. **不模拟网页登录**：不注入 Cookie、不驱动 WebView 登录、不复用浏览器会话。
4. **不爬取超出 API 能力的内容**：Actions 日志、Settings、网页 diff 视图等一律不做。
5. **不把 token 发给任何第三方**（镜像、反代、统计服务）。
6. **不做自动写操作**：建 issue/评论必须用户点击确认，且只在 M3 提供。
7. **不改 Hosts/防火墙/DNS**（沿用 M1 纪律）。
8. **不引入浏览器内核**（不用 WebView2 渲染页面来"补全"网页能力）。

---

## 8. 测试计划

| 类别 | 用例 | 断言 |
|---|---|---|
| 端点解析 | `owner/repo` 输入、含 `https://github.com/` 前缀、含 `.git` 后缀、非法字符 | 归一化为 `owner/repo`；非法输入给明确错误，不发请求 |
| 端点解析 | ref 含 `/`（分支名）、路径含空格/中文 | URL 正确编码，不 404 |
| 树解析 | `truncated=true` 时 | 回退到逐目录 `contents` 拉取，UI 提示"目录过大，已分批" |
| 限流退避 | 403 + `x-ratelimit-reset` | 不再重试，展示恢复时间；到点后自动允许重试 |
| 限流退避 | 403 + `retry-after` | 等待指定秒数后单次重试，不做指数风暴 |
| 缓存命中 | 首次 200 → 二次带 `If-None-Match` → 304 | 返回缓存内容；请求数计入但配额不减（按 header 断言） |
| 缓存过期 | TTL 到期 + 网络不可达 | 展示离线缓存 + "最后更新"标注，不显示空白/无提示 |
| token 缺失降级 | 无 token | 只读功能全部可用；写入口隐藏/置灰；配额显示 60/h |
| token 失效 | 401 | 清空内存 token，切只读，提示重新输入 |
| 错误态展示 | 404 / 500 / 超时 | 文案与 §4.3 一致，可一键跳"官网访问诊断" |
| 安全 | 全流程日志、异常消息、导出内容 | 不出现 token 明文/片段 |
| 加密 | DPAPI 往返、换用户不可解密 | 保存→重启→读取一致；跨用户读取失败并降级 |
| 只读纪律 | M1/M2 全流程 | 无 Hosts 写入、无代理设置变更（沿用现有断言方式） |

测试分层：端点/解析/缓存为纯单元测试（无网络）；限流与错误态用本地假 HTTP 服务；
真网络只做人工冒烟（标时间，不放进 CI）。

---

## 9. 分阶段与工作量

规模用 S/M/L 表示（相对改动面，非天数）。

| 阶段 | 范围 | 规模 | 主要改动 | 验收 |
|---|---|---|---|---|
| **M1 只读浏览** | 仓库树 + README + 文件查看 + issue 列表 | M | 新增 `GitHubApiClient`（含 ETag 缓存）、`ApiBrowserViewModel/View`、导航项；无 token | 断网页时能看树/README/issue；配额显示正确；无静默失败 |
| **M2 搜索与下载** | 仓库/issue 搜索、zip 下载（复用现有下载加速通道） | M | 搜索限流退避；zip 走 `zipball` 302 后交给现有下载器 | 搜索有独立限流提示；zip 落地且走已有加速策略 |
| **M3 带 token 写操作** | 可选 token（DPAPI）、建 issue、评论 | S | `TokenStore`、写入口 UI、确认对话框、401/403 处理 | 明文不落盘；撤销后可清除；写操作二次确认 |

依赖顺序：M1 必须先落地缓存与限流提示（否则 60/h 立刻耗尽，产品体验反而更差）。

---

## 10. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| API 版本/字段变更 | 解析失败、界面空白 | 只依赖稳定 REST v3 字段；解析容错 + 明确错误态；不解析 HTML |
| token 泄露面 | 凭据被窃 | DPAPI + 仅当前用户；只发 `api.github.com`；日志脱敏；默认不落盘 |
| 私有仓库权限 | 私有仓库 404 被误判为"不存在" | 有 token 时区分 404 与 403；文案提示"可能需要 token 或无权限" |
| 配额被共享 IP 耗尽 | 未登录用户莫名 403 | 展示 `x-ratelimit-reset`，建议配置 token；缓存优先 |
| 与网页内容不一致 | 用户困惑（渲染差异、无评论嵌套、无 Actions 日志） | 页面顶部固定说明"只读快照，与网页版可能有差异" |
| `api.github.com` 本身不可达 | 整个方案失效 | 错误态引导去"官网访问诊断"；不假装成功 |
| 本地缓存膨胀 | 磁盘占用 | 单仓库上限（如 20MB）+ LRU 清理 + 设置页可清空 |
| 误把只读入口当完整官网 | 用户找不到登录/设置 | 导航命名带"（API）"，首屏一句话说明边界 |

---

## 11. 附录：路线 A 反代镜像实测（2026-11-06 15:19:25–15:20:27）

curl 8.13（Schannel），`--connect-timeout 5 --max-time 12`，只读 GET，未登录。

| 站点 | 页面路径 | 判定 | 证据 |
|---|---|---|---|
| `kkgithub.com` | `/{owner}/{repo}` | **不可用** | curl(35) `SEC_E_CERT_EXPIRED`（证书过期），000 / 6.10s |
| `bgithub.xyz` | 同上 | **不可用** | 连接超时，000 / 5.00s |
| `github.moeyy.xyz` | 同上 | **不可用** | DNS 解析失败，000 / 0.01s |
| `gh-proxy.com` | 同上 | **不可用（页面）** | 200 / 2.03s / 71870B，但为混淆 JS 反爬挑战页，无仓库特征串，assets=0 |
| `ghfast.top` | 同上 | **不可用（页面）** | 403 / 0.88s / 14B |
| `hub.whtrys.space` | path 与 prefix 变体 | **不可用** | DNS 解析失败 |
| `github.abskoop.workers.dev` | 同上 | **不可用** | 连接超时，000 / 5.00s |
| `ghproxy.net` | 同上 | **不可用（页面）** | 403 / 8.85s / 14B |
| 直连 `github.com` | 同路径 | **本轮不可用** | 000 / 12.00s 超时（同一时段 API 正常） |

raw 路径（`/https://raw.githubusercontent.com/.../README.md`）：

| 站点 | 结果 | 判定 |
|---|---|---|
| `gh-proxy.com` | 200 / 0.88s / 4423B，marker=True | 仅文件与 raw |
| `ghfast.top` | 200 / 1.94s / 4423B，marker=True | 仅文件与 raw |
| `ghproxy.net` | 200 / 5.93s / 4423B，marker=True | 仅文件与 raw |
| 其余 | 证书/超时/DNS 失败 | 不可用 |

分档结论：**可整站浏览 = 0 个；仅文件与 raw = 3 个（gh-proxy / ghfast / ghproxy.net）；其余不可用。**

未验证项：`gh-proxy.com` 挑战页是否在真实浏览器中可通过（本轮不启动浏览器、不交互）；
各站点长期稳定性、高峰时段表现；`codeload` / release 资源路径；镜像页面的登录行为（未登录，不测）。
