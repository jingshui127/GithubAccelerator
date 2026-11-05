using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// 诊断包脱敏：把用户名、token、PAT 等替换成占位符。
/// 只做文本替换，不改动原始文件；规则同时用于"预览"里向用户展示将脱敏哪些字段。
/// </summary>
public sealed class DiagnosticsSanitizer
{
    public const string UserPlaceholder = "<user>";
    public const string SecretPlaceholder = "<已脱敏>";

    private static readonly Regex WindowsProfilePath = new(
        @"([A-Za-z]:[\\/]Users[\\/])([^\\/\s""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnixProfilePath = new(
        @"(/home/)([^/\s""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GitHubToken = new(
        @"(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|x-access-token:[^@\s""']+)",
        RegexOptions.Compiled);

    private static readonly Regex KeyValueSecret = new(
        @"(?i)(\b(?:token|pat|password|passwd|secret|authorization)\b[\s""']*[:=][\s""']*(?:Bearer\s+)?)([^\s"",;]+)",
        RegexOptions.Compiled);

    private static readonly Regex CredentialUrl = new(
        @"(https?://)([^/@\s:]+):([^/@\s]+)@", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string? _userName;
    private readonly string? _userProfile;

    public DiagnosticsSanitizer(string? userName = null, string? userProfile = null)
    {
        _userName = userName ?? Environment.UserName;
        _userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>预览里展示给用户的脱敏规则（说清楚不会打包什么）。</summary>
    public static IReadOnlyList<string> RulesDescription { get; } = new[]
    {
        "路径中的用户名 → " + UserPlaceholder,
        "GitHub token / PAT（ghp_、github_pat_、x-access-token）→ " + SecretPlaceholder,
        "键值对里的 token / password / secret / authorization → " + SecretPlaceholder,
        "带账号密码的 URL → 账号密码替换为 " + SecretPlaceholder,
        "Hosts 只导出本程序自己的标记块，不导出整份文件"
    };

    public string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = text;

        // 先替换完整用户目录（含可能的空格路径），再兜底通配。
        if (!string.IsNullOrEmpty(_userProfile))
        {
            result = ReplaceIgnoreCase(result, _userProfile, UserPlaceholder);
        }

        result = WindowsProfilePath.Replace(result, m => m.Groups[1].Value + UserPlaceholder);
        result = UnixProfilePath.Replace(result, m => m.Groups[1].Value + UserPlaceholder);

        if (!string.IsNullOrEmpty(_userName) && _userName.Length >= 3)
        {
            result = Regex.Replace(result, Regex.Escape(_userName), UserPlaceholder,
                RegexOptions.IgnoreCase);
        }

        result = GitHubToken.Replace(result, SecretPlaceholder);
        result = KeyValueSecret.Replace(result, m => m.Groups[1].Value + SecretPlaceholder);
        result = CredentialUrl.Replace(result,
            m => m.Groups[1].Value + SecretPlaceholder + "@");

        return result;
    }

    /// <summary>文本里是否仍像含有未脱敏的用户名/密钥（用于导出前的自检）。</summary>
    public bool LooksUnsafe(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!string.IsNullOrEmpty(_userName) && _userName.Length >= 3
            && text.Contains(_userName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return GitHubToken.IsMatch(text) || CredentialUrl.IsMatch(text);
    }

    private static string ReplaceIgnoreCase(string source, string value, string replacement)
    {
        var index = 0;
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var found = source.IndexOf(value, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                builder.Append(source, index, source.Length - index);
                break;
            }
            builder.Append(source, index, found - index);
            builder.Append(replacement);
            index = found + value.Length;
        }
        return builder.ToString();
    }
}
