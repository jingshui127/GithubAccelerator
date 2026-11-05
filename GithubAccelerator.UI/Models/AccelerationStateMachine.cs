using System;

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
            if (_state != AccelerationState.PartiallyFailed && _state != AccelerationState.Accelerated)
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
