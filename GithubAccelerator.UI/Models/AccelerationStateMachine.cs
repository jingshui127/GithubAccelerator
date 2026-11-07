using System;
using System.Collections.Generic;
using System.Linq;

namespace GithubAccelerator.Models;

/// <summary>
/// 一键开始/停止的可观测状态。任意时刻只有一个状态，稳定状态与进行中状态互斥。
/// </summary>
public enum AccelerationState
{
    /// <summary>未加速。</summary>
    NotAccelerated,

    /// <summary>进行中，可取消；此期间不会写入 Hosts。</summary>
    Starting,

    /// <summary>已加速。</summary>
    Accelerated,

    /// <summary>部分失败：主体已完成，但有步骤未成功（原因可读）。</summary>
    PartiallyFailed,

    /// <summary>已写入加速，但当前全部数据源不可用：不再是"加速中"的假象。</summary>
    AcceleratedUnavailable,

    /// <summary>等待用户确认（需要提权的动作在等 UAC）。</summary>
    WaitingForElevation
}

/// <summary>
/// 状态机：只负责状态与唯一性约束，不做任何 IO，便于单元测试。
/// 允许的迁移：稳定状态 ⇄ 进行中；稳定状态 ⇄ 等待提权；等待提权 → 进行中 / 回到原稳定状态。
/// </summary>
public sealed class AccelerationStateMachine
{
    private AccelerationState _state = AccelerationState.NotAccelerated;
    private AccelerationState _lastStable = AccelerationState.NotAccelerated;
    private string? _reason;

    public AccelerationState State => _state;

    /// <summary>上一次稳定状态（取消/提权被拒后回到这里）。</summary>
    public AccelerationState LastStableState => _lastStable;

    public string? PartialFailureReason => _reason;

    public bool IsBusy => _state == AccelerationState.Starting;

    public bool CanCancel => IsBusy;

    public string StateText => _state switch
    {
        AccelerationState.NotAccelerated => "未加速",
        AccelerationState.Starting => "进行中（可取消）",
        AccelerationState.Accelerated => "已加速",
        AccelerationState.AcceleratedUnavailable => $"已加速，但当前加速线路不可用：{_reason ?? "全部加速线路不可达"}（可恢复原始访问）",
        AccelerationState.PartiallyFailed => $"部分失败：{_reason ?? "有步骤未完成"}",
        AccelerationState.WaitingForElevation => "等待用户确认（提权）",
        _ => "未知"
    };

    /// <summary>从稳定状态进入进行中。已在执行中时返回 false（防重入）。</summary>
    public bool Begin()
    {
        if (IsBusy || _state == AccelerationState.WaitingForElevation) return false;
        _lastStable = StableOf(_state);
        _state = AccelerationState.Starting;
        _reason = null;
        return true;
    }

    /// <summary>进行中 → 已加速。</summary>
    public void Complete()
    {
        if (!IsBusy) return;
        EnterStable(AccelerationState.Accelerated, null);
    }

    /// <summary>进行中 → 部分失败（说明哪部分没成）。</summary>
    public void CompleteWithIssues(string reason)
    {
        if (!IsBusy) return;
        EnterStable(AccelerationState.PartiallyFailed, string.IsNullOrWhiteSpace(reason) ? "有步骤未完成" : reason);
    }

    /// <summary>进行中 → 回到上一个稳定状态（取消且未写入 Hosts）。</summary>
    public void Cancel()
    {
        if (!IsBusy) return;
        _state = _lastStable;
        _reason = null;
    }

    /// <summary>进行中结束但不能确认已加速（例如写入失败）→ 回到上一个稳定状态。</summary>
    public void FailedBeforeWrite(string? reason = null)
    {
        if (!IsBusy) return;
        _state = _lastStable;
        _reason = reason;
    }

    /// <summary>
    /// 已写入加速、但全部数据源都不可达：进入"已加速但源不可用"稳定状态。
    /// 调用方必须确认 Hosts 仍处于已应用状态。
    /// </summary>
    public void MarkSourceUnavailable(string? reason = null)
    {
        if (_state == AccelerationState.WaitingForElevation) return;
        EnterStable(AccelerationState.AcceleratedUnavailable,
            string.IsNullOrWhiteSpace(reason) ? "全部加速线路不可达" : reason);
    }

    /// <summary>稳定状态 → 等待提权确认。</summary>
    public void BeginElevation()
    {
        if (IsBusy) return;
        _lastStable = StableOf(_state);
        _state = AccelerationState.WaitingForElevation;
    }

    /// <summary>提权结果：授予则进入进行中，拒绝/失败则回到原稳定状态。</summary>
    public void EndElevation(bool granted)
    {
        if (_state != AccelerationState.WaitingForElevation) return;
        if (granted)
        {
            _state = AccelerationState.Starting;
            _reason = null;
        }
        else
        {
            _state = _lastStable;
        }
    }

    /// <summary>按文件实际状态对齐（仅在稳定状态生效，进行中/等待提权时不动）。</summary>
    public void SyncFromFile(bool applied)
    {
        if (IsBusy || _state == AccelerationState.WaitingForElevation) return;

        if (applied)
        {
            // 已加速但源不可用是一个有意义的状态，不能因为文件还在就改写成"已加速"。
            if (_state != AccelerationState.PartiallyFailed
                && _state != AccelerationState.Accelerated
                && _state != AccelerationState.AcceleratedUnavailable)
                EnterStable(AccelerationState.Accelerated, null);
        }
        else
        {
            EnterStable(AccelerationState.NotAccelerated, null);
        }
    }

    private void EnterStable(AccelerationState state, string? reason)
    {
        _state = state;
        _reason = reason;
        _lastStable = state;
    }

    private static AccelerationState StableOf(AccelerationState state) =>
        state == AccelerationState.WaitingForElevation ? AccelerationState.NotAccelerated : state;
}

/// <summary>
/// 全部数据源失败时的说明文本：检测时间 + 每个源的失败原因 + 可执行的直连动作。
/// 纯函数，便于测试；不编造"即将恢复"之类的推测。
/// </summary>
public static class AllSourcesFailureReport
{
    public const string DirectActionText = "恢复原始访问（关闭加速）";

    public static string Build(
        DateTime checkedAt,
        IEnumerable<(string Name, string? Error)> sources)
    {
        var lines = sources
            .Select(s => $"- {s.Name}：{(string.IsNullOrWhiteSpace(s.Error) ? "无响应（未返回内容）" : s.Error!.Trim())}")
            .ToList();

        return string.Join(Environment.NewLine, new[]
        {
            $"检测时间：{checkedAt:yyyy-MM-dd HH:mm:ss}",
            $"结果：共 {lines.Count} 条加速线路全部不可用。",
            string.Join(Environment.NewLine, lines),
            $"建议动作：点击“{DirectActionText}”。若当前已应用加速，会移除本程序的 Hosts 块，让 GitHub 回到原始链接直连。"
        });
    }
}
