using Avalonia.Controls;

namespace GithubAccelerator.UI.Helpers;

/// <summary>
/// "把窗口找回来"所需的最小窗口能力。抽出来是为了让
/// <see cref="WindowRestorer"/> 成为纯逻辑、可单测，并被所有入口共用：
/// 托盘左键单击/双击、托盘右键菜单、第二个实例启动。
/// </summary>
public interface IRestorableWindow
{
    bool IsVisible { get; }
    bool ShowInTaskbar { get; set; }
    WindowState WindowState { get; set; }
    void ShowWindow();
    void ActivateWindow();
}

/// <summary>
/// 统一的"恢复并激活主窗口"逻辑：先让窗口可见，再复位最小化状态，
/// 最后激活到前台。恢复只做 Show/状态复位，不销毁窗口，因此可重复调用。
/// </summary>
public static class WindowRestorer
{
    public static void Restore(IRestorableWindow window)
    {
        if (!window.IsVisible)
        {
            window.ShowWindow();
        }

        window.ShowInTaskbar = true;

        // 从托盘回来时窗口处于"隐藏 + 最小化"：不复位就只会在任务栏闪一下。
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.ActivateWindow();
    }
}

/// <summary>
/// 托盘点击的路由判定。左键单击、左键双击都恢复窗口（托盘惯例是单击即开，
/// 不依赖双击语义）；右键交给上下文菜单，不抢菜单。
/// </summary>
public static class TrayClickRouter
{
    public static bool ShouldRestoreOnClick(bool isLeftButton, int clicks) => isLeftButton;
}
