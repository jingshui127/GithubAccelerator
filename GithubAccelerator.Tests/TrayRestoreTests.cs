using Avalonia.Controls;
using GithubAccelerator.UI.Helpers;

namespace GithubAccelerator.Tests;

/// <summary>
/// 托盘"把窗口找回来"的纯逻辑测试：所有入口共用 <see cref="WindowRestorer"/>，
/// 这里用假窗口断言状态复位、未被销毁、可重复调用，以及左键单击/双击的路由判定。
/// </summary>
public class TrayRestoreTests
{
    private sealed class FakeWindow : IRestorableWindow
    {
        public bool IsVisible { get; set; }
        public bool ShowInTaskbar { get; set; } = false;
        public WindowState WindowState { get; set; } = WindowState.Normal;
        public int ShowCalls { get; private set; }
        public int ActivateCalls { get; private set; }
        public int CloseCalls { get; private set; }

        public void ShowWindow()
        {
            ShowCalls++;
            IsVisible = true;
        }

        public void ActivateWindow() => ActivateCalls++;

        /// <summary>模拟"窗口被销毁"的唯一动作，用于断言恢复流程不会调用它。</summary>
        public void Close()
        {
            CloseCalls++;
            IsVisible = false;
        }
    }

    [Fact]
    public void Restore_FromHiddenMinimized_ShowsResetsStateAndActivates()
    {
        var window = new FakeWindow { IsVisible = false, WindowState = WindowState.Minimized };

        WindowRestorer.Restore(window);

        Assert.True(window.IsVisible);
        Assert.Equal(1, window.ShowCalls);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.True(window.ShowInTaskbar);
        Assert.Equal(1, window.ActivateCalls);
        Assert.Equal(0, window.CloseCalls);
    }

    [Fact]
    public void Restore_FromVisibleMinimized_ResetsStateWithoutReShowing()
    {
        var window = new FakeWindow { IsVisible = true, WindowState = WindowState.Minimized };

        WindowRestorer.Restore(window);

        Assert.Equal(0, window.ShowCalls);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(1, window.ActivateCalls);
        Assert.Equal(0, window.CloseCalls);
    }

    [Fact]
    public void Restore_FromVisibleNormal_KeepsWindowAndActivates()
    {
        var window = new FakeWindow { IsVisible = true, WindowState = WindowState.Normal };

        WindowRestorer.Restore(window);

        Assert.Equal(0, window.ShowCalls);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(1, window.ActivateCalls);
        Assert.Equal(0, window.CloseCalls);
    }

    [Fact]
    public void Restore_CalledRepeatedly_DoesNotDestroyWindow()
    {
        var window = new FakeWindow { IsVisible = false, WindowState = WindowState.Minimized };

        WindowRestorer.Restore(window);
        WindowRestorer.Restore(window);

        Assert.True(window.IsVisible);
        Assert.Equal(1, window.ShowCalls);
        Assert.Equal(2, window.ActivateCalls);
        Assert.Equal(0, window.CloseCalls);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void TrayClickRouter_LeftSingleOrDouble_RestoresWindow(bool isLeft, int clicks)
    {
        Assert.True(TrayClickRouter.ShouldRestoreOnClick(isLeft, clicks));
    }

    [Fact]
    public void TrayClickRouter_RightClick_DoesNotStealContextMenu()
    {
        Assert.False(TrayClickRouter.ShouldRestoreOnClick(false, 1));
        Assert.False(TrayClickRouter.ShouldRestoreOnClick(false, 2));
    }
}
