using System;

namespace GithubAccelerator.UI.Services;

/// <summary>已知失败类型。每种都给一句人话原因 + 一个可执行按钮。</summary>
public enum FailureKind
{
    ElevationDenied,
    AllMirrorsUnreachable,
    HostsWriteDenied,
    HostsRewrittenByThirdParty,
    GitConfigConflict,
    NoNetwork
}

public sealed record FailureAdvice(FailureKind Kind, string Cause, string ActionText);

/// <summary>
/// 把技术错误翻译成"人话原因 + 下一步按钮"。这里只做映射，不执行动作；
/// 动作由界面按 Kind 分派，避免把 UI 依赖塞进文案层。
/// </summary>
public static class FailureAdvisor
{
    public static FailureAdvice Get(FailureKind kind)
    {
        return kind switch
        {
            FailureKind.ElevationDenied => new FailureAdvice(kind,
                "你取消了系统的管理员确认，所以这次没有改动 Hosts；电脑保持原样。",
                "以管理员身份重试"),

            FailureKind.AllMirrorsUnreachable => new FailureAdvice(kind,
                "所有加速节点这次都没连上，多半是网络波动或当前网络限制了外部访问。",
                "重试一次"),

            FailureKind.HostsWriteDenied => new FailureAdvice(kind,
                "Hosts 文件没写成：通常是权限不够，或被安全软件占用/锁定。",
                "以管理员身份重试"),

            FailureKind.HostsRewrittenByThirdParty => new FailureAdvice(kind,
                "Hosts 里我们那段内容被别的软件改过，所以这次没有直接覆盖它。",
                "查看 Hosts 内容"),

            FailureKind.GitConfigConflict => new FailureAdvice(kind,
                "Git 全局配置里已有一条同名的加速改写项，属于你自己的设置，程序没有覆盖。",
                "打开设置处理冲突"),

            FailureKind.NoNetwork => new FailureAdvice(kind,
                "现在连不上外网，加速节点自然也连不上；先确认网络是否正常。",
                "重新检测网络"),

            _ => new FailureAdvice(kind,
                "这次操作没有完成，系统设置没有被修改。",
                "重试一次")
        };
    }
}
