using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Forms;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Core.Services;

namespace GithubAccelerator.UI.ViewModels;

public partial class DownloadAcceleratorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _inputUrl = "";

    [ObservableProperty]
    private MirrorResult? _result;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "粘贴 GitHub 链接，一键生成加速链接";

    [ObservableProperty]
    private bool _hasResults;

    /// <summary>
    /// 转换后的镜像 URL 列表（用于 UI 绑定）
    /// </summary>
    public ObservableCollection<ConvertedUrlItemViewModel> MirrorUrlItems => Result != null
        ? new ObservableCollection<ConvertedUrlItemViewModel>(Result.MirrorUrls.Select(m =>
            new ConvertedUrlItemViewModel(m.SiteName, m.Url, m.Description)))
        : new();

    /// <summary>
    /// 转换后的 Git Clone 命令列表（用于 UI 绑定）
    /// </summary>
    public ObservableCollection<GitCloneCommandViewModel> GitCloneCommandItems => Result != null
        ? new ObservableCollection<GitCloneCommandViewModel>(Result.GitCloneCommands.Select(c =>
            new GitCloneCommandViewModel(c.MirrorName, c.Command, c.Description)))
        : new();

    private readonly MirrorUrlService _mirrorService = new();

    /// <summary>
    /// 转换 URL 为加速链接
    /// </summary>
    [RelayCommand]
    private void ConvertUrl()
    {
        if (string.IsNullOrWhiteSpace(InputUrl))
        {
            StatusMessage = "请输入 GitHub 链接";
            return;
        }

        IsBusy = true;
        try
        {
            Result = _mirrorService.ConvertUrl(InputUrl);
            HasResults = Result.HasResults;

            if (HasResults)
            {
                var typeNames = new Dictionary<GithubUrlType, string>
                {
                    { GithubUrlType.RawFile, "Raw 文件" },
                    { GithubUrlType.ReleaseOrZip, "Release/ZIP 下载" },
                    { GithubUrlType.Repository, "仓库页面" },
                    { GithubUrlType.GitClone, "Git Clone" },
                    { GithubUrlType.JsDelivr, "jsDelivr 链接" },
                };
                var typeName = Result.UrlType != GithubUrlType.Unknown && typeNames.ContainsKey(Result.UrlType)
                    ? typeNames[Result.UrlType] : "链接";
                StatusMessage = $"检测到 {typeName}，已生成 {Result.MirrorUrls.Count + (Result.GitCloneCommands.Count > 0 ? 1 : 0) + (!string.IsNullOrEmpty(Result.JsDelivrUrl) ? 1 : 0)} 个加速方案";
            }
            else
            {
                StatusMessage = "未能识别该链接格式，请检查 URL 是否正确";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"转换失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 从剪贴板粘贴
    /// </summary>
    [RelayCommand]
    private void PasteFromClipboard()
    {
        try
        {
            var text = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                InputUrl = text.Trim();
                StatusMessage = "已从剪贴板粘贴";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"粘贴失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 清空输入和结果
    /// </summary>
    [RelayCommand]
    private void ClearInput()
    {
        InputUrl = "";
        Result = null;
        HasResults = false;
        StatusMessage = "粘贴 GitHub 链接，一键生成加速链接";
    }

    /// <summary>
    /// 复制 jsDelivr 链接
    /// </summary>
    [RelayCommand]
    private void CopyJsDelivr()
    {
        if (!string.IsNullOrEmpty(Result?.JsDelivrUrl))
        {
            CopyToClipboard(Result.JsDelivrUrl);
        }
    }

    /// <summary>
    /// 在浏览器中打开 jsDelivr 链接
    /// </summary>
    [RelayCommand]
    private void OpenJsDelivrBrowser()
    {
        if (!string.IsNullOrEmpty(Result?.JsDelivrUrl))
        {
            OpenInBrowser(Result.JsDelivrUrl);
        }
    }

    /// <summary>
    /// 复制所有加速链接
    /// </summary>
    [RelayCommand]
    private void CopyAll()
    {
        if (Result == null) return;

        var allLinks = new List<string>();

        // 添加镜像站链接
        foreach (var mirror in Result.MirrorUrls)
        {
            allLinks.Add($"[{mirror.SiteName}] {mirror.Url}");
        }

        // 添加 jsDelivr 链接
        if (!string.IsNullOrEmpty(Result.JsDelivrUrl))
        {
            allLinks.Add($"[jsDelivr CDN] {Result.JsDelivrUrl}");
        }

        // 添加 Git Clone 命令
        foreach (var cmd in Result.GitCloneCommands)
        {
            allLinks.Add($"[{cmd.MirrorName}] {cmd.Command}");
        }

        var text = string.Join(Environment.NewLine, allLinks);
        CopyToClipboard(text);
    }

    /// <summary>
    /// 复制文本到剪贴板
    /// </summary>
    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            StatusMessage = "已复制到剪贴板！";
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 在浏览器中打开链接
    /// </summary>
    private void OpenInBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
            StatusMessage = "已在浏览器中打开";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开浏览器失败：{ex.Message}";
        }
    }

    public List<MirrorSite> AvailableMirrors => MirrorUrlService.AvailableMirrors.ToList();
}

/// <summary>
/// 转换后的 URL 项 ViewModel（用于 UI 绑定）
/// </summary>
public partial class ConvertedUrlItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _siteName = "";

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _description = "";

    public ConvertedUrlItemViewModel(string siteName, string url, string description)
    {
        SiteName = siteName;
        Url = url;
        Description = description;
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(Url);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "复制 URL 失败");
        }
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "打开浏览器失败");
        }
    }
}

/// <summary>
/// Git Clone 命令 ViewModel（用于 UI 绑定）
/// </summary>
public partial class GitCloneCommandViewModel : ObservableObject
{
    [ObservableProperty]
    private string _mirrorName = "";

    [ObservableProperty]
    private string _command = "";

    [ObservableProperty]
    private string _description = "";

    public GitCloneCommandViewModel(string mirrorName, string command, string description)
    {
        MirrorName = mirrorName;
        Command = command;
        Description = description;
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(Command);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "复制 Git 命令失败");
        }
    }
}
