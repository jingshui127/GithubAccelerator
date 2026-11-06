using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace GithubAccelerator.Core.Services;

public enum GitRewriteState
{
    Unavailable,
    NotEnabled,
    EnabledOwned,
    EnabledExternal
}

public sealed record GitRewriteStatus(
    GitRewriteState State,
    string? ActivePrefix,
    string Message,
    string? GitVersion);

public sealed record GitRewriteConflict(string ExistingKey, string ExistingValue, string Reason);

public sealed record GitRewriteEnableResult(
    bool Success,
    string Message,
    string? Prefix,
    bool OwnedByApp,
    GitRewriteConflict? Conflict = null);

public sealed record GitRewriteDisableResult(
    bool Success,
    string Message,
    IReadOnlyList<string> RemovedKeys,
    string? Error = null);

public sealed record LegacyGitProxyEntry(string Key, string Value, string Origin, string Reason);

public sealed record GitLegacyCleanupResult(
    bool Success,
    string Message,
    IReadOnlyList<LegacyGitProxyEntry> Removed);

/// <summary>
/// 用 <c>url.&lt;镜像前缀&gt;.insteadOf</c> 做 Git 加速，替代原先把反代站写进
/// <c>http.proxy</c>/<c>https.proxy</c> 的错误做法。
/// 只改写 https://github.com/ 开头的 URL，不影响其它站点；写入来源记录在
/// <c>%AppData%\GithubAccelerator\git-url-rewrite.json</c>，停止时只撤销本程序写入的条目。
/// 注意：push 需要 GitHub 鉴权，镜像站不支持写入转发，本服务额外写入一条恒等
/// <c>pushInsteadOf</c> 让 push 保持直连；如用户已有 push 改写则保持用户配置不动。
/// </summary>
public sealed class GitUrlRewriteService
{
    /// <summary>
    /// 默认镜像：gitclone.com。2026-11-06 本机实测 git ls-remote 0.52s / clone --depth 1 1.12s，
    /// 优于直连（1.11s / 2.32s）与 ghfast.top（5.70s / 3.03s）；ghfast.top 保留在镜像池中但不作默认。
    /// </summary>
    public const string DefaultMirrorBaseUrl = "https://gitclone.com";

    public const string InsteadOfValue = "https://github.com/";
    private const string PushInsteadOfKey = "url.https://github.com/.pushInsteadOf";

    /// <summary>
    /// gitclone.com 采用 host-path 形式（https://gitclone.com/github.com/{owner}/{repo}.git），
    /// 与前缀反代（https://&lt;mirror&gt;/https://github.com/...）不同，改写 key 需要单独构造。
    /// </summary>
    public const string GitCloneRewritePrefix = "https://gitclone.com/github.com/";

    internal static string BuildInsteadOfPrefix(string normalizedBaseUrl)
    {
        return normalizedBaseUrl.Equals("https://gitclone.com", StringComparison.OrdinalIgnoreCase)
            ? GitCloneRewritePrefix
            : normalizedBaseUrl + "/" + InsteadOfValue;
    }

    /// <summary>本程序历史版本可能写进 http(s).proxy 的镜像地址（按 host 判定）。</summary>
    private static readonly string[] KnownLegacyProxyHosts =
    {
        "ghproxy.net",
        "gh.api.99988866.xyz",
        "ghfast.top",
        "gitmirror.com",
        "gitclone.com",
        "github.com.cnpmjs.org",
        "gh-proxy.com",
        "ghproxy.com"
    };

    public static string DefaultProvenancePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubAccelerator",
        "git-url-rewrite.json");

    private sealed class ProvenanceFile
    {
        public int Version { get; set; } = 1;
        public List<ProvenanceEntry> Entries { get; set; } = new();
    }

    private sealed class ProvenanceEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string MirrorBase { get; set; } = string.Empty;
        public bool Owned { get; set; }
        public bool Pending { get; set; }
        public DateTimeOffset WrittenAt { get; set; } = DateTimeOffset.Now;
        public List<string> PreviousValues { get; set; } = new();
    }

    private readonly IGitCommandExecutor _git;
    private readonly string _provenancePath;
    private readonly string? _configFile;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>生产环境：使用本机全局 git 配置与默认来源记录路径。</summary>
    public GitUrlRewriteService()
        : this(new GitCommandExecutor(), DefaultProvenancePath, null)
    {
    }

    /// <summary>
    /// <paramref name="configFile"/> 传入时改用 <c>git config --file</c>，测试可在临时文件上
    /// 用真实 git 验证，完全隔离用户全局配置。
    /// </summary>
    public GitUrlRewriteService(IGitCommandExecutor git, string provenancePath, string? configFile = null)
    {
        _git = git;
        _provenancePath = provenancePath;
        _configFile = configFile;
    }

    public async Task<GitCheckResult> CheckGitAvailabilityAsync() => await _git.CheckAvailabilityAsync();

    public async Task<GitRewriteStatus> GetStatusAsync()
    {
        var check = await _git.CheckAvailabilityAsync();
        if (!check.IsAvailable)
        {
            return new GitRewriteStatus(GitRewriteState.Unavailable, null,
                $"未检测到 Git：{check.ErrorMessage}", null);
        }

        var rules = await GetUrlRulesAsync();
        var provenance = LoadProvenance();

        foreach (var entry in provenance.Entries.Where(e => e.Owned && !e.Pending))
        {
            var current = rules.Where(r => SameKey(r.Key, entry.Key) && r.Value == entry.Value).ToList();
            if (current.Count > 0)
            {
                return new GitRewriteStatus(GitRewriteState.EnabledOwned, entry.MirrorBase,
                    $"已启用（本程序写入）：{entry.MirrorBase}（push 保持直连 GitHub）", check.Version);
            }
        }

        var external = rules.FirstOrDefault(r =>
            ParseUrlRule(r.Key)?.Name.Equals("insteadof", StringComparison.OrdinalIgnoreCase) == true
            && MatchesGithub(r.Value));
        if (external != null)
        {
            return new GitRewriteStatus(GitRewriteState.EnabledExternal, null,
                $"检测到已有改写（非本程序写入）：{external.Key} = {external.Value}，停止加速不会删除它", check.Version);
        }

        return new GitRewriteStatus(GitRewriteState.NotEnabled, null, "未启用", check.Version);
    }

    /// <summary>
    /// 写入 insteadOf。已有用户同名配置或其它针对 github.com 的改写时不覆盖，返回冲突原因。
    /// </summary>
    /// <param name="onProgress">每完成一步回调 (已完成, 共 N 项)，不编造百分比。</param>
    public async Task<GitRewriteEnableResult> EnableAsync(string mirrorBaseUrl, Action<int, int>? onProgress = null)
    {
        const int totalSteps = 4;
        var doneSteps = 0;
        void Step() => onProgress?.Invoke(++doneSteps, totalSteps);

        await _lock.WaitAsync();
        try
        {
            var check = await _git.CheckAvailabilityAsync();
            if (!check.IsAvailable)
            {
                return new GitRewriteEnableResult(false, $"未检测到 Git：{check.ErrorMessage}", null, false);
            }
            Step();

            string baseUrl;
            try
            {
                baseUrl = NormalizeMirrorBase(mirrorBaseUrl);
            }
            catch (ArgumentException ex)
            {
                return new GitRewriteEnableResult(false, ex.Message, null, false);
            }

            var prefix = BuildInsteadOfPrefix(baseUrl);
            var mainKey = $"url.{prefix}.insteadOf";
            var rules = await GetUrlRulesAsync();
            Step();

            // 已存在完全相同的改写：不再写入，也不据为己有。
            var sameRule = rules.FirstOrDefault(r => SameKey(r.Key, mainKey) && r.Value == InsteadOfValue);
            if (sameRule != null)
            {
                var existingProvenance = LoadProvenance();
                var owned = existingProvenance.Entries.Any(e =>
                    e.Owned && !e.Pending && SameKey(e.Key, mainKey) && e.Value == InsteadOfValue);
                return new GitRewriteEnableResult(true,
                    owned
                        ? "已启用（本程序此前写入，无需重复）"
                        : "已存在相同改写（非本程序写入），本程序未做改动，停止加速时也不会删除",
                    baseUrl, owned);
            }

            // 其它 key 已针对 github.com 做改写：git 前缀匹配存在歧义，不写。
            var otherGithubRule = rules.FirstOrDefault(r =>
                !SameKey(r.Key, mainKey)
                && ParseUrlRule(r.Key)?.Name.Equals("insteadof", StringComparison.OrdinalIgnoreCase) == true
                && MatchesGithub(r.Value));
            if (otherGithubRule != null)
            {
                return new GitRewriteEnableResult(false,
                    $"检测到已有 GitHub URL 改写，未做任何改动：{otherGithubRule.Key} = {otherGithubRule.Value}",
                    null, false,
                    new GitRewriteConflict(otherGithubRule.Key, otherGithubRule.Value,
                        "已有改写同样匹配 https://github.com/，继续写入会导致改写优先级不确定"));
            }

            // 同名 key 已被用户占用（值不同）：不覆盖。
            var sameKeyValues = rules.Where(r => SameKey(r.Key, mainKey)).Select(r => r.Value).ToList();
            if (sameKeyValues.Count > 0)
            {
                return new GitRewriteEnableResult(false,
                    $"同名配置已被占用，未覆盖：{mainKey} = {string.Join(", ", sameKeyValues)}",
                    null, false,
                    new GitRewriteConflict(mainKey, string.Join(", ", sameKeyValues),
                        "同名 key 已存在不同值，覆盖会破坏用户既有配置"));
            }

            // 先落来源记录（Pending），崩溃时也能据此判断是否需要清理。
            var provenance = LoadProvenance();
            var mainEntry = new ProvenanceEntry
            {
                Key = mainKey,
                Value = InsteadOfValue,
                Kind = "insteadOf",
                MirrorBase = baseUrl,
                Owned = false,
                Pending = true,
                PreviousValues = new List<string>()
            };
            provenance.Entries.RemoveAll(e => SameKey(e.Key, mainKey) && e.Value == InsteadOfValue);
            provenance.Entries.Add(mainEntry);
            SaveProvenance(provenance);

            var addMain = await ExecConfigAsync("--add", mainKey, InsteadOfValue);
            if (addMain.ExitCode != 0)
            {
                provenance.Entries.Remove(mainEntry);
                SaveProvenance(provenance);
                var error = $"写入 url 改写失败：{FirstLine(addMain.Error)}";
                Log.Warning("[GitRewrite] {Message}", error);
                return new GitRewriteEnableResult(false, error, null, false);
            }
            Step();

            // push 保护：镜像站不支持带鉴权的写入转发；若用户已有 push 改写则保持原样。
            var pushRules = rules.Where(r => ParseUrlRule(r.Key)?.Name.Equals("pushinsteadof", StringComparison.OrdinalIgnoreCase) == true
                                             && MatchesGithub(r.Value)).ToList();
            var pushEntry = new ProvenanceEntry();
            if (pushRules.Count == 0)
            {
                pushEntry = new ProvenanceEntry
                {
                    Key = PushInsteadOfKey,
                    Value = InsteadOfValue,
                    Kind = "pushInsteadOf",
                    MirrorBase = baseUrl,
                    Owned = false,
                    Pending = true
                };
                provenance.Entries.Add(pushEntry);
                SaveProvenance(provenance);

                var addPush = await ExecConfigAsync("--add", PushInsteadOfKey, InsteadOfValue);
                if (addPush.ExitCode != 0)
                {
                    // 回滚主条目，避免出现"fetch 被改写、push 被改写却没有保护"的半成品状态。
                    await RemoveExactValueAsync(mainKey, InsteadOfValue);
                    provenance.Entries.Remove(mainEntry);
                    provenance.Entries.Remove(pushEntry);
                    SaveProvenance(provenance);
                    var error = $"写入 push 保护配置失败，已回滚：{FirstLine(addPush.Error)}";
                    Log.Warning("[GitRewrite] {Message}", error);
                    return new GitRewriteEnableResult(false, error, null, false);
                }
            }

            mainEntry.Owned = true;
            mainEntry.Pending = false;
            if (pushRules.Count == 0)
            {
                pushEntry.Owned = true;
                pushEntry.Pending = false;
            }
            SaveProvenance(provenance);
            Step();

            Log.Information("[GitRewrite] 已启用 url 改写: {Prefix}", prefix);
            var pushNote = pushRules.Count == 0 ? "push 保持直连" : "push 沿用你的既有配置";
            return new GitRewriteEnableResult(true,
                $"已启用：clone/fetch/pull 走 {baseUrl}，{pushNote}（镜像不支持 push 鉴权）",
                baseUrl, true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>只撤销本程序写入且当前仍存在的条目，用户自己添加的值不动。</summary>
    /// <param name="onProgress">每完成一步回调 (已完成, 共 N 项)，不编造百分比。</param>
    public async Task<GitRewriteDisableResult> DisableAsync(Action<int, int>? onProgress = null)
    {
        await _lock.WaitAsync();
        try
        {
            var check = await _git.CheckAvailabilityAsync();
            if (!check.IsAvailable)
            {
                return new GitRewriteDisableResult(false, $"未检测到 Git：{check.ErrorMessage}", Array.Empty<string>());
            }

            var provenance = LoadProvenance();
            var owned = provenance.Entries.Where(e => e.Owned).ToList();
            if (owned.Count == 0)
            {
                onProgress?.Invoke(1, 1);
                return new GitRewriteDisableResult(true, "没有由本程序写入的改写，无需清理", Array.Empty<string>());
            }

            // 步骤 = 检测 git + 逐条撤销 + 清理来源记录
            var totalSteps = owned.Count + 2;
            var doneSteps = 1;
            onProgress?.Invoke(doneSteps, totalSteps);

            var removed = new List<string>();
            var failures = new List<string>();

            foreach (var entry in owned)
            {
                var current = await GetValuesAsync(entry.Key);
                if (current.Contains(entry.Value))
                {
                    var result = await RemoveExactValueAsync(entry.Key, entry.Value);
                    if (result.ExitCode != 0)
                    {
                        failures.Add($"{entry.Key}: {FirstLine(result.Error)}");
                        onProgress?.Invoke(++doneSteps, totalSteps);
                        continue;
                    }
                    removed.Add(entry.Key);
                }
                onProgress?.Invoke(++doneSteps, totalSteps);
            }

            if (failures.Count > 0)
            {
                // 保留未成功撤销的记录，下次还能继续清理。
                provenance.Entries.RemoveAll(e => e.Owned && removed.Any(k => SameKey(k, e.Key)));
                SaveProvenance(provenance);
                return new GitRewriteDisableResult(false,
                    $"部分条目清理失败：{string.Join("；", failures)}", removed, string.Join("；", failures));
            }

            provenance.Entries.RemoveAll(e => e.Owned);
            SaveProvenance(provenance);
            onProgress?.Invoke(++doneSteps, totalSteps);
            Log.Information("[GitRewrite] 已撤销 {Count} 条由本程序写入的改写", removed.Count);
            return new GitRewriteDisableResult(true,
                removed.Count == 0 ? "没有需要移除的改写（可能已被手动删除）" : $"已移除 {removed.Count} 条本程序写入的配置",
                removed);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>列出全局 git 配置里疑似由本程序历史版本写入的 http(s).proxy 条目（只读）。</summary>
    public async Task<IReadOnlyList<LegacyGitProxyEntry>> InspectLegacyProxyAsync()
    {
        var check = await _git.CheckAvailabilityAsync();
        if (!check.IsAvailable) return Array.Empty<LegacyGitProxyEntry>();

        var list = await ExecConfigAsync("--list", "--show-origin");
        if (list.ExitCode != 0) return Array.Empty<LegacyGitProxyEntry>();

        var entries = new List<LegacyGitProxyEntry>();
        foreach (var rawLine in list.Output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var tab = line.LastIndexOf('\t');
            if (tab <= 0) continue;

            var origin = line[..tab];
            var pair = line[(tab + 1)..];
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            var key = pair[..eq];
            var value = pair[(eq + 1)..];
            if (!key.Equals("http.proxy", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("https.proxy", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!LooksLikeLegacyMirrorProxy(value)) continue;

            entries.Add(new LegacyGitProxyEntry(key, value, origin,
                "值与本项目历史版本使用过的镜像地址一致，按值判定为本项目写入"));
        }

        return entries;
    }

    /// <summary>删除 InspectLegacyProxyAsync 列出的条目；只删精确匹配的 key=value。</summary>
    public async Task<GitLegacyCleanupResult> CleanupLegacyProxyAsync(IEnumerable<LegacyGitProxyEntry> entries)
    {
        await _lock.WaitAsync();
        try
        {
            var check = await _git.CheckAvailabilityAsync();
            if (!check.IsAvailable)
            {
                return new GitLegacyCleanupResult(false, $"未检测到 Git：{check.ErrorMessage}", Array.Empty<LegacyGitProxyEntry>());
            }

            var removed = new List<LegacyGitProxyEntry>();
            var failures = new List<string>();

            foreach (var entry in entries)
            {
                var current = await GetValuesAsync(entry.Key);
                if (!current.Contains(entry.Value))
                {
                    continue; // 用户已手动清理
                }

                var result = await RemoveExactValueAsync(entry.Key, entry.Value);
                if (result.ExitCode != 0)
                {
                    failures.Add($"{entry.Key}={entry.Value}: {FirstLine(result.Error)}");
                    continue;
                }
                removed.Add(entry);
            }

            if (failures.Count > 0)
            {
                return new GitLegacyCleanupResult(false,
                    $"部分历史配置清理失败：{string.Join("；", failures)}", removed);
            }

            Log.Information("[GitRewrite] 已清理历史 git proxy 条目 {Count} 条", removed.Count);
            return new GitLegacyCleanupResult(true,
                removed.Count == 0 ? "没有需要清理的历史条目" : $"已清理 {removed.Count} 条历史 git proxy 配置",
                removed);
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record UrlRule(string Key, string Value);

    private sealed record ParsedUrlKey(string BaseUrl, string Name);

    private static ParsedUrlKey? ParseUrlRule(string key)
    {
        if (!key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)) return null;
        var rest = key[4..];
        var lastDot = rest.LastIndexOf('.');
        if (lastDot <= 0) return null;
        return new ParsedUrlKey(rest[..lastDot], rest[(lastDot + 1)..]);
    }

    private static bool SameKey(string a, string b)
    {
        var pa = ParseUrlRule(a);
        var pb = ParseUrlRule(b);
        if (pa == null || pb == null) return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        return string.Equals(pa.BaseUrl, pb.BaseUrl, StringComparison.Ordinal)
               && pa.Name.Equals(pb.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGithub(string value)
    {
        var v = value.TrimEnd('/');
        return v.Equals("https://github.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool LooksLikeLegacyMirrorProxy(string value)
    {
        try
        {
            var uri = new Uri(value.Trim());
            var host = uri.Host.ToLowerInvariant();
            return KnownLegacyProxyHosts.Any(h => host == h || host.EndsWith("." + h));
        }
        catch
        {
            return false;
        }
    }

    internal static string NormalizeMirrorBase(string input)
    {
        var trimmed = (input ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("镜像地址不能为空");

        Uri uri;
        try
        {
            uri = new Uri(trimmed);
        }
        catch
        {
            throw new ArgumentException($"镜像地址格式无效：{input}");
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("镜像地址必须是 https:// 开头（避免明文传输）");
        if (uri.Query.Length > 0)
            throw new ArgumentException("镜像地址不能包含查询参数");
        if (trimmed.Any(char.IsWhiteSpace))
            throw new ArgumentException("镜像地址不能包含空白字符");

        return trimmed;
    }

    private async Task<IReadOnlyList<UrlRule>> GetUrlRulesAsync()
    {
        var result = await ExecConfigAsync("--get-regexp", "url");
        if (result.ExitCode != 0) return Array.Empty<UrlRule>();

        var rules = new List<UrlRule>();
        foreach (var rawLine in result.Output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var space = line.IndexOf(' ');
            if (space <= 0) continue;
            var key = line[..space];
            var value = line[(space + 1)..].Trim();
            if (ParseUrlRule(key) == null) continue;
            rules.Add(new UrlRule(key, value));
        }
        return rules;
    }

    private async Task<List<string>> GetValuesAsync(string key)
    {
        var result = await ExecConfigAsync("--get-all", key);
        if (result.ExitCode != 0) return new List<string>();
        return result.Output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    private async Task<GitCommandResult> RemoveExactValueAsync(string key, string value)
    {
        var pattern = "^" + Regex.Escape(value) + "$";
        return await ExecConfigAsync("--unset-all", key, pattern);
    }

    private async Task<GitCommandResult> ExecConfigAsync(params string[] args)
    {
        var scoped = _configFile == null
            ? new[] { "config", "--global" }.Concat(args).ToArray()
            : new[] { "config", "--file", _configFile }.Concat(args).ToArray();
        return await _git.ExecuteAsync(scoped);
    }

    private ProvenanceFile LoadProvenance()
    {
        try
        {
            if (File.Exists(_provenancePath))
            {
                var json = File.ReadAllText(_provenancePath, Encoding.UTF8);
                var data = JsonSerializer.Deserialize<ProvenanceFile>(json);
                if (data != null && data.Entries != null) return data;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[GitRewrite] 读取来源记录失败: {Path}", _provenancePath);
        }
        return new ProvenanceFile();
    }

    private void SaveProvenance(ProvenanceFile data)
    {
        try
        {
            var directory = Path.GetDirectoryName(_provenancePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var temp = _provenancePath + ".tmp";
            File.WriteAllText(temp, json, Encoding.UTF8);
            File.Move(temp, _provenancePath, true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[GitRewrite] 保存来源记录失败: {Path}", _provenancePath);
        }
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "未知错误";
        var idx = trimmed.IndexOfAny(new[] { '\r', '\n' });
        return idx < 0 ? trimmed : trimmed[..idx];
    }
}
