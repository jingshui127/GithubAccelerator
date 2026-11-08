using System.Text.RegularExpressions;

namespace GithubAccelerator.UI.Services;

/// <summary>
/// 界面白话层：只做显示替换，不改 Core 的判定文案与判定结果。
/// 目的：把「候选 / 基线 / 整页 / 预筛 / 配对 / 中位数」等内部术语，
/// 在用户可见位置统一换成能一眼看懂的说法；日志与判定逻辑保持原词，便于排障。
/// </summary>
public static class UiText
{
    /// <summary>把内部术语换成用户能理解的说法；空值原样返回。</summary>
    public static string Plain(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var result = text;

        // 先处理带“IP”的组合，避免出现“备选 IP IP”。
        result = Regex.Replace(result, @"候选\s*IP", "备选 IP");
        result = result.Replace("候选", "备选 IP");

        // “整页”本身保留，但补上“打开”，避免读者误以为是整页截图。
        result = result.Replace("整页不可用", "整页打不开");
        result = Regex.Replace(result, @"整页(?!打开)", "整页打开");

        // 基线 = 系统当前解析的直连地址。
        result = result.Replace("基线", "当前直连");

        // 交错配对 / 配对差值 / 配对中位，按整词先换，避免拆坏语义。
        result = result.Replace("交错配对", "交替对比");
        result = result.Replace("有效配对", "有效对比组");
        result = result.Replace("配对差值", "两组差值");
        result = result.Replace("配对中位", "对比中间值");
        result = result.Replace("配对", "对比");

        // 中位数 → 中间值（采样排序后取中间）。
        result = result.Replace("中位数", "中间值");
        result = result.Replace("中位耗时", "中间值耗时");
        result = result.Replace("中位", "中间值");

        result = result.Replace("预筛", "预检查");
        result = result.Replace("TCP/TLS 预检查", "加密连接预检查");
        result = result.Replace("TCP/TLS", "加密连接");
        result = result.Replace("TCP 443", "443 端口");
        result = result.Replace("TLS 握手", "加密连接握手");
        result = result.Replace("IP连接", "IP 连接");
        result = result.Replace("IP能否", "IP 能否");
        result = result.Replace("A/B 相邻完成", "A/B 紧挨着测");
        result = result.Replace("TTL", "缓存有效期（TTL）");

        return result;
    }
}
