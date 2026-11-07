using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubAccelerator.Core.Services;
using GithubAccelerator.Services;
using GithubAccelerator.UI.Services;

namespace GithubAccelerator.UI.ViewModels;

/// <summary>
/// 「防护版 Hosts 加速」交互层：默认关闭；先预览（写哪些域 / 选中 IP / 预计收益）再确认写入。
/// 所有写入都由 <see cref="OfficialSiteHostsGuardService"/> 完成，界面不直接碰 Hosts。
/// </summary>
public partial class OfficialSiteHostsGuardViewModel : ObservableObject
{
    private readonly OfficialSiteHostsGuardService _service;
    private readonly IHostsBackupService _backupService;

    private bool _initialized;
    private bool _suppressToggle;
    private bool _applyArmed;
    private bool _undoArmed;
    private bool _restoreArmed;
    private OfficialSiteGuardPlan? _lastPlan;

    public OfficialSiteHostsGuardViewModel()
        : this(OfficialSiteHostsGuardService.CreateDefault(), null)
    {
    }

    public OfficialSiteHostsGuardViewModel(
        OfficialSiteHostsGuardService service, IHostsBackupService? backupService = null)
    {
        _service = service;
        _backupService = backupService ?? new HostsBackupService(new WindowsHostsFileService());
        ApplyButtonText = "确认写入 Hosts";
        UndoButtonText = "恢复原始状态";
        RestoreButtonText = "恢复到首启前快照";
    }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _isApplied;

    [ObservableProperty]
    private bool _canApply;

    [ObservableProperty]
    private bool _needsElevation;

    [ObservableProperty]
    private string _domainText = "写入域：github.com（api.github.com 无实测数据，不写；CDN 域一律不写）";

    [ObservableProperty]
    private string _previewIpText = "尚未预览";

    [ObservableProperty]
    private string _previewSourceText = "—";

    [ObservableProperty]
    private string _previewCountsText = "—";

    [ObservableProperty]
    private string _previewBenefitText = "点击「预览并检测」查看将写入的域名、选中 IP 与预计收益；默认关闭，未确认不会写 Hosts。";

    /// <summary>交错配对表：每组两侧数值、配对差值、中位差值与稳定性判定。</summary>
    [ObservableProperty]
    private string _previewPairingText = string.Empty;

    public bool HasPairingText => !string.IsNullOrEmpty(PreviewPairingText);

    partial void OnPreviewPairingTextChanged(string value) => OnPropertyChanged(nameof(HasPairingText));

    [ObservableProperty]
    private string _appliedIpText = "未应用";

    [ObservableProperty]
    private string _appliedSourceText = "—";

    [ObservableProperty]
    private string _lastCheckText = "尚未检测";

    [ObservableProperty]
    private string _countersText = "成功 0 / 失败 0";

    [ObservableProperty]
    private string _statusText = "默认关闭。启用前先预览，确认后才会写入；只在这个 IP 建连超时的时段才有价值。";

    [ObservableProperty]
    private string _applyButtonText;

    [ObservableProperty]
    private string _undoButtonText;

    [ObservableProperty]
    private string _restoreButtonText;

    [ObservableProperty]
    private bool _isUndoVisible;

    [ObservableProperty]
    private bool _isRestoreVisible;

    public string ScopeWarning =>
        "只写 github.com；不写 githubassets/avatars/camo 等 CDN 域；不改 DNS/代理/证书；" +
        "不做后台常驻轮询。选优与准入看整页耗时：准入用 6 组交错配对的差值中位数，顺序测法仅作诊断、不作依据；" +
        "配对符号反复翻转时明确标注“结果不稳定，不作准入依据”。整页度量不执行 JS，不等同浏览器。";

    /// <summary>打开面板时读取状态；不联网、不写 Hosts（复测需要手动点击）。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var content = await _service.Hosts.ReadAsync();
        var state = _service.GetState();
        // 仅当“状态里有本程序的应用记录”且块内有本程序签名时才算已应用；
        // 否则系统里历史遗留的 GitHub520 等块会被误判并被撤销误删。
        IsApplied = _service.Hosts.IsBlockApplied(content)
                    && !string.IsNullOrEmpty(state.AppliedIp)
                    && content.Contains(OfficialSiteHostsGuardService.BlockSignature, StringComparison.Ordinal);
        ApplyStateToUi(state);
        if (IsApplied)
        {
            StatusText = "检测到本程序已应用的加速块；点击「复测并自愈」核对当前 IP。";
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggle) return;
        if (IsBusy) return;

        if (value)
        {
            // 启用 = 先展示预览，不立即写入
            _applyArmed = false;
            ApplyButtonText = "确认写入 Hosts";
            _ = PreviewInternalAsync();
        }
        else
        {
            StatusText = IsApplied
                ? "已关闭启用开关，但 Hosts 仍保留当前映射；请点击「恢复原始状态」确认移除。"
                : "已关闭启用开关；未写 Hosts。";
        }
    }

    [RelayCommand]
    private async Task Preview()
    {
        await PreviewInternalAsync();
    }

    private async Task PreviewInternalAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        NeedsElevation = false;
        _applyArmed = false;
        ApplyButtonText = "确认写入 Hosts";
        if (!IsEnabled) SetToggleSilently(true);
        StatusText = "正在对候选与直连做交错配对整页实测（每轮 A/B 相邻完成，共 6 组；另含 3 轮顺序诊断，仅参考）…";

        try
        {
            _lastPlan = await _service.PreviewAsync();
            HasPreview = true;
            PreviewPairingText = _lastPlan.PairingText;
            var selected = _lastPlan.Selection.Selected;
            var qualified = _service.CountQualified(_lastPlan.Selection.Candidates);
            var baselineText = _lastPlan.BaselineIp == null
                ? "直连：未解析到 IPv4"
                : $"直连 {_lastPlan.BaselineIp}：{(_lastPlan.BaselinePageMedianMs.HasValue ? _lastPlan.BaselinePageMedianMs + "ms" : "整页不可用")}" +
                  $"（{_lastPlan.BaselinePageSuccessCount}/{_lastPlan.BaselinePageRounds}）";

            if (selected == null)
            {
                CanApply = false;
                PreviewIpText = "无整页可达候选";
                PreviewSourceText = "—";
                PreviewCountsText = $"候选 {_lastPlan.Selection.Candidates.Count} 个，达标 0 个；{baselineText}";
                PreviewBenefitText = _lastPlan.ExpectedBenefitText;
                StatusText = _lastPlan.DecisionText;
                return;
            }

            CanApply = _lastPlan.CanApply;
            PreviewIpText = $"{selected.Entry.Ip}（{selected.PageMetricText}；诊断 {selected.DiagnosticText}）";
            PreviewSourceText = selected.Entry.Source + $"（加入时间 {selected.Entry.AddedAt:yyyy-MM-dd HH:mm}）";
            PreviewCountsText = $"候选 {_lastPlan.Selection.Candidates.Count} 个，达标 {qualified} 个；" +
                                $"选中 {selected.SuccessRateText}；{baselineText}；检测 {_lastPlan.PreviewedAt:yyyy-MM-dd HH:mm:ss}";
            PreviewBenefitText = _lastPlan.ExpectedBenefitText;
            StatusText = _lastPlan.CanApply
                ? "预览完成，准入通过但尚未写入。确认域名、IP 与两侧实测后，再点击「确认写入 Hosts」。"
                : _lastPlan.DecisionText;
        }
        catch (Exception ex)
        {
            HasPreview = false;
            CanApply = false;
            PreviewPairingText = string.Empty;
            StatusText = "预览失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (IsBusy) return;

        if (!_applyArmed)
        {
            if (_lastPlan == null) await PreviewInternalAsync();
            if (_lastPlan == null || !_lastPlan.CanApply) return;

            _applyArmed = true;
            ApplyButtonText = "再次点击确认写入";
            StatusText = $"即将写入 {string.Join("、", _lastPlan.WriteDomains)} → {_lastPlan.SelectedIp}；" +
                         "来源：" + _lastPlan.SelectedSource + "。再次点击确认，或点「取消确认」。";
            return;
        }

        await ApplyInternalAsync();
    }

    private async Task ApplyInternalAsync()
    {
        if (_lastPlan == null) return;
        IsBusy = true;
        _applyArmed = false;
        ApplyButtonText = "确认写入 Hosts";
        NeedsElevation = false;

        try
        {
            var result = await _service.ApplyAsync(_lastPlan);
            NeedsElevation = result.NeedsElevation;
            StatusText = result.Message;
            if (result.Success)
            {
                IsApplied = true;
                SetToggleSilently(true);
                ApplyStateToUi(_service.GetState());
            }
        }
        catch (Exception ex)
        {
            StatusText = "写入失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        _applyArmed = false;
        _undoArmed = false;
        _restoreArmed = false;
        ApplyButtonText = "确认写入 Hosts";
        UndoButtonText = "恢复原始状态";
        RestoreButtonText = "恢复到首启前快照";
        StatusText = "已取消确认。";
    }

    [RelayCommand]
    private async Task VerifyAndHeal()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在复测已应用 IP；失败会自动尝试下一候选，全部失效则回退…";
        try
        {
            var result = await _service.VerifyAndHealAsync();
            StatusText = result.Message;
            IsApplied = result.Outcome != OfficialSiteGuardHealOutcome.NotApplied
                        && result.Outcome != OfficialSiteGuardHealOutcome.RolledBack
                        && result.Ip != null;
            if (result.Outcome == OfficialSiteGuardHealOutcome.RolledBack)
                SetToggleSilently(false);
            ApplyStateToUi(_service.GetState());
        }
        catch (Exception ex)
        {
            StatusText = "复测失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Undo()
    {
        if (IsBusy) return;

        if (!_undoArmed)
        {
            _undoArmed = true;
            UndoButtonText = "再次点击确认恢复";
            StatusText = "撤销只会删除本程序的标记块，块外内容不动；点击第二次确认。";
            return;
        }

        IsBusy = true;
        _undoArmed = false;
        UndoButtonText = "恢复原始状态";
        try
        {
            var result = await _service.UndoAsync();
            StatusText = result.Message;
            IsApplied = false;
            SetToggleSilently(false);
            ApplyStateToUi(_service.GetState());
        }
        catch (Exception ex)
        {
            StatusText = "撤销失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreFirstRunSnapshot()
    {
        if (IsBusy) return;

        if (!_restoreArmed)
        {
            _restoreArmed = true;
            RestoreButtonText = "再次点击确认恢复到首启前快照";
            StatusText = "将用「首启快照」整体还原 Hosts（需要管理员权限，且会覆盖当前全部内容）。再次点击确认。";
            return;
        }

        IsBusy = true;
        _restoreArmed = false;
        RestoreButtonText = "恢复到首启前快照";
        try
        {
            var snapshot = await _backupService.GetFirstRunSnapshotAsync();
            if (snapshot == null)
            {
                StatusText = "没有找到首启快照，无法恢复。";
                return;
            }

            var ok = await _backupService.RestoreBackupAsync(snapshot.BackupPath);
            if (ok)
            {
                // 快照可能已包含或不包含本程序块；统一清一次状态，避免界面显示“仍已应用”
                await _service.UndoAsync();
                IsApplied = false;
                SetToggleSilently(false);
                ApplyStateToUi(_service.GetState());
                StatusText = $"已按首启快照恢复 Hosts（{snapshot.BackupTime:yyyy-MM-dd HH:mm:ss}）。";
            }
            else
            {
                NeedsElevation = !_service.HasWriteAccess();
                StatusText = NeedsElevation
                    ? "恢复失败：当前不是管理员，无法写入 Hosts；本程序不会静默提权。"
                    : "恢复失败：请检查 Hosts 权限或文件是否被占用。";
            }
        }
        catch (Exception ex)
        {
            StatusText = "恢复失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyStateToUi(OfficialSiteGuardState state)
    {
        AppliedIpText = string.IsNullOrEmpty(state.AppliedIp) ? "未应用" : state.AppliedIp!;
        AppliedSourceText = string.IsNullOrEmpty(state.AppliedSource) ? "—" : state.AppliedSource!;
        LastCheckText = (state.LastVerificationAt ?? state.LastCheckAt)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未检测";
        var rounds = state.MeasuredRounds > 0 ? state.MeasuredRounds : state.SuccessCount + state.FailureCount;
        CountersText = $"最近一次检测：成功 {state.SuccessCount} / 失败 {state.FailureCount}" +
                       (rounds > 0 ? $"（共 {rounds} 轮）" : string.Empty) +
                       $"；复测累计成功 {state.VerifySuccessCount} / 失败 {state.VerifyFailureCount}";
        IsUndoVisible = IsApplied;
        IsRestoreVisible = true;
        SetToggleSilently(IsApplied && state.Enabled);
    }

    private void SetToggleSilently(bool value)
    {
        if (IsEnabled == value) return;
        _suppressToggle = true;
        IsEnabled = value;
        _suppressToggle = false;
    }
}


