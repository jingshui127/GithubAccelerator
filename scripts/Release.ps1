# 发布脚本：本机生成单文件 exe → 计算校验值 → 打包 →（可选）上传到 GitHub Releases。
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts\Release.ps1                 # 只本地生成
#   powershell -ExecutionPolicy Bypass -File scripts\Release.ps1 -Upload         # 生成并上传
#   powershell -ExecutionPolicy Bypass -File scripts\Release.ps1 -SkipBuild      # 复用上次构建产物
# 说明：上传需要先 `gh auth login`；脚本不读取任何 token，也不改写 git 全局配置。

param(
    [string]$Version = "",
    [switch]$Upload,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    [xml]$csproj = Get-Content (Join-Path $root 'GithubAccelerator.UI\GithubAccelerator.UI.csproj')
    $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw '无法从 csproj 读取版本号，请用 -Version 指定' }

$staging = Join-Path $root 'artifacts\staging'
$distDir = Join-Path $root "artifacts\v$Version"
$zipPath = Join-Path $root "artifacts\GithubAccelerator_v${Version}_win-x64.zip"
$exeName = 'GithubAccelerator.UI.exe'

if (-not $SkipBuild) {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Write-Host "==> 发布构建 v$Version（自包含单文件 + 压缩）"
    & dotnet publish (Join-Path $root 'GithubAccelerator.UI\GithubAccelerator.UI.csproj') `
        -c Release -p:PublishProfile=FolderProfile -p:Version=$Version --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }
}

if (-not (Test-Path (Join-Path $staging $exeName))) { throw "未找到发布产物：$staging\$exeName" }

# 只允许发布一个 exe：符号文件、其它 DLL 与空语言目录一律剔除，避免把 publish/ 里的杂物带进包。
Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Name -ne $exeName } |
    ForEach-Object { Write-Host "剔除非发布文件: $($_.Name)"; Remove-Item $_.FullName -Force }
Get-ChildItem $staging -Recurse -Directory | Sort-Object FullName -Descending |
    ForEach-Object { if (-not (Get-ChildItem $_.FullName -Recurse -File)) { Remove-Item $_.FullName -Force } }

if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
Copy-Item (Join-Path $staging $exeName) $distDir

# 托盘图标是运行时从磁盘读取的（程序不会从 exe 里取图标文件），必须随包提供。
$iconPath = Join-Path $root 'GithubAccelerator.UI\Assets\app-icon.ico'
if (Test-Path $iconPath) {
    $assetsDir = Join-Path $distDir 'Assets'
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
    Copy-Item $iconPath $assetsDir
}

$installDoc = Join-Path $root 'docs\INSTALL.md'
if (Test-Path $installDoc) { Copy-Item $installDoc (Join-Path $distDir 'INSTALL.md') }

# SHA256SUMS：格式 `<hash>  <文件名>`，可直接被 Get-FileHash 结果人工核对。
$exePath = Join-Path $distDir $exeName
$hash = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $exeName" | Set-Content (Join-Path $distDir 'SHA256SUMS') -Encoding ASCII

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host ("==> 产物：{0}（{1} MB），zip {2} MB" -f $exePath, $sizeMb, $zipMb)
Write-Host ("    SHA256：{0}" -f $hash)
Write-Host ("    zip 内容：{0}" -f ((& tar -tf $zipPath) -join ', '))

if ($Upload) {
    $tag = "v$Version"
    $existing = & gh release view $tag --json tagName 2>$null
    if ($LASTEXITCODE -eq 0 -and $existing) {
        Write-Host "==> 追加到已有 Release $tag"
        & gh release upload $tag $zipPath (Join-Path $distDir 'SHA256SUMS') --clobber
    }
    else {
        Write-Host "==> 创建 Release $tag"
        & gh release create $tag $zipPath (Join-Path $distDir 'SHA256SUMS') `
            --title "GitHub 加速器 Pro $tag" `
            --notes "未签名版本。首次运行 Windows SmartScreen 会提示，属正常现象；校验方法与放行步骤见 INSTALL.md。"
    }
    if ($LASTEXITCODE -ne 0) { throw "上传失败（退出码 $LASTEXITCODE），本地产物已保留在 $distDir" }
    else { Write-Host '==> 上传完成' }
}
else {
    Write-Host "（未上传）确认无误后执行：powershell -File scripts\Release.ps1 -Version $Version -Upload"
}
