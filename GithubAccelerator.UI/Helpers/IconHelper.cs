using System;
using System.Collections.Generic;

namespace GithubAccelerator.UI.Helpers;

public static class IconHelper
{
    public static class Navigation
    {
        public const string Dashboard = "📊";
        public const string Performance = "📈";
        public const string Latency = "⏱️";
        public const string HostsContent = "📝";
        public const string HostsGroup = "📁";
        public const string Backup = "💾";
        public const string Settings = "⚙️";
        public const string Log = "📋";
        public const string About = "ℹ️";
    }

    public static class Status
    {
        public const string Success = "✓";
        public const string Warning = "⚠";
        public const string Error = "✕";
        public const string Info = "ℹ";
        public const string Loading = "⏳";
        public const string Refresh = "🔄";
    }

    public static class Actions
    {
        public const string Apply = "✓";
        public const string Cancel = "✕";
        public const string Delete = "🗑️";
        public const string Edit = "✏️";
        public const string Add = "➕";
        public const string Search = "🔍";
        public const string Filter = "🔽";
        public const string Export = "📤";
        public const string Import = "📥";
        public const string Copy = "📋";
        public const string Paste = "📄";
        public const string Save = "💾";
        public const string Open = "📂";
        public const string Close = "✕";
        public const string Minimize = "➖";
        public const string Maximize = "⬜";
        public const string Restore = "❐";
    }

    public static class Source
    {
        public const string Globe = "🌐";
        public const string Server = "🖥️";
        public const string Cloud = "☁️";
        public const string Fast = "⚡";
        public const string Slow = "🐢";
        public const string Best = "🏆";
    }

    public static class Theme
    {
        public const string Light = "☀️";
        public const string Dark = "🌙";
        public const string Auto = "🔄";
    }

    public static class EmptyState
    {
        public const string NoData = "📭";
        public const string NoResults = "🔍";
        public const string NoConnection = "🔌";
        public const string Error = "❌";
    }

    public static class Toast
    {
        public const string Success = "✓";
        public const string Warning = "⚠";
        public const string Error = "✕";
        public const string Info = "ℹ";
    }

    public static string GetStatusIcon(double responseTime)
    {
        return responseTime switch
        {
            < 100 => Source.Fast,
            < 300 => Status.Success,
            < 500 => Status.Warning,
            _ => Source.Slow
        };
    }

    public static string GetScoreIcon(int score)
    {
        return score switch
        {
            >= 90 => Source.Best,
            >= 70 => Status.Success,
            >= 50 => Status.Warning,
            _ => Status.Error
        };
    }

    public static string GetConnectionStatusIcon(bool isConnected, bool isMonitoring)
    {
        if (!isMonitoring) return Status.Loading;
        return isConnected ? Status.Success : Status.Error;
    }
}
