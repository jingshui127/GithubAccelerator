using System;
using GithubAccelerator.Models;
using Xunit;

namespace GithubAccelerator.Tests;

public class AccelerationStateMachineTests
{
    [Fact]
    public void InitialState_IsNotAccelerated()
    {
        var machine = new AccelerationStateMachine();
        Assert.Equal(AccelerationState.NotAccelerated, machine.State);
        Assert.False(machine.IsBusy);
        Assert.False(machine.CanCancel);
        Assert.Equal("未加速", machine.StateText);
    }

    [Fact]
    public void BeginThenComplete_ReachesAccelerated()
    {
        var machine = new AccelerationStateMachine();
        Assert.True(machine.Begin());
        Assert.True(machine.IsBusy);
        Assert.Equal("进行中（可取消）", machine.StateText);

        machine.Complete();
        Assert.Equal(AccelerationState.Accelerated, machine.State);
        Assert.False(machine.IsBusy);
    }

    [Fact]
    public void BeginTwice_IsRejectedSoOnlyOneStateExists()
    {
        var machine = new AccelerationStateMachine();
        Assert.True(machine.Begin());
        Assert.False(machine.Begin());
        Assert.Equal(AccelerationState.Starting, machine.State);
    }

    [Fact]
    public void Cancel_ReturnsToPreviousStableState()
    {
        var machine = new AccelerationStateMachine();
        machine.SyncFromFile(true);
        Assert.Equal(AccelerationState.Accelerated, machine.State);

        Assert.True(machine.Begin());
        machine.Cancel();

        Assert.Equal(AccelerationState.Accelerated, machine.State);
        Assert.False(machine.IsBusy);
    }

    [Fact]
    public void CancelFromNotAccelerated_StaysNotAccelerated()
    {
        var machine = new AccelerationStateMachine();
        machine.Cancel();
        Assert.Equal(AccelerationState.NotAccelerated, machine.State);
    }

    [Fact]
    public void CompleteWithIssues_ExposesReadableReason()
    {
        var machine = new AccelerationStateMachine();
        machine.Begin();
        machine.CompleteWithIssues("DNS 缓存刷新失败（Hosts 已写入）");

        Assert.Equal(AccelerationState.PartiallyFailed, machine.State);
        Assert.Contains("DNS 缓存刷新失败", machine.StateText);
        Assert.Contains("部分失败", machine.StateText);
        Assert.False(machine.IsBusy);
    }

    [Fact]
    public void CompleteWithIssues_AfterCancel_IsIgnored()
    {
        var machine = new AccelerationStateMachine();
        machine.Begin();
        machine.Cancel();
        machine.CompleteWithIssues("迟到的问题");

        Assert.Equal(AccelerationState.NotAccelerated, machine.State);
    }

    [Fact]
    public void ElevationDenied_ReturnsToPreviousStableState()
    {
        var machine = new AccelerationStateMachine();
        machine.SyncFromFile(true);

        machine.BeginElevation();
        Assert.Equal(AccelerationState.WaitingForElevation, machine.State);
        Assert.Contains("等待用户确认", machine.StateText);

        machine.EndElevation(false);
        Assert.Equal(AccelerationState.Accelerated, machine.State);
    }

    [Fact]
    public void ElevationGranted_MovesToStarting()
    {
        var machine = new AccelerationStateMachine();
        machine.BeginElevation();
        machine.EndElevation(true);

        Assert.Equal(AccelerationState.Starting, machine.State);
        Assert.True(machine.CanCancel);
    }

    [Fact]
    public void BeginDuringElevationWait_IsRejected()
    {
        var machine = new AccelerationStateMachine();
        machine.BeginElevation();
        Assert.False(machine.Begin());
        Assert.Equal(AccelerationState.WaitingForElevation, machine.State);
    }

    [Fact]
    public void SyncFromFile_DoesNotOverrideTransientStates()
    {
        var machine = new AccelerationStateMachine();
        machine.Begin();
        machine.SyncFromFile(true);
        Assert.Equal(AccelerationState.Starting, machine.State);

        var waiting = new AccelerationStateMachine();
        waiting.BeginElevation();
        waiting.SyncFromFile(false);
        Assert.Equal(AccelerationState.WaitingForElevation, waiting.State);
    }

    [Fact]
    public void FailedBeforeWrite_ReturnsToLastStableState()
    {
        var machine = new AccelerationStateMachine();
        machine.SyncFromFile(true);
        machine.Begin();
        machine.FailedBeforeWrite("Hosts 写入失败");

        Assert.Equal(AccelerationState.Accelerated, machine.State);
        Assert.False(machine.IsBusy);
    }
}
