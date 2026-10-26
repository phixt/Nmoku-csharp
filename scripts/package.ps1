# Nmoku Windows 打包脚本（M5 预备）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 [-Version 0.1.0-alpha]
# 前提（一次性）：导出模板已安装——编辑器菜单「编辑器 → 管理导出模板 → 下载并安装」，
#   mono 版约 1GB，装到 %APPDATA%\Godot\export_templates\4.6.2.stable.mono\
param(
    [string]$Version = ""
)
$ErrorActionPreference = "Stop"
$godot = "D:\Tools\Godot\Godot_console.exe"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Nmoku.Game"

# 版本号：缺省从 project.godot 的 config/version 读取
if (-not $Version) {
    $line = Select-String -Path (Join-Path $project "project.godot") -Pattern 'config/version="(.+)"'
    if (-not $line) { Write-Error "project.godot 无 config/version，也未传 -Version"; exit 1 }
    $Version = $line.Matches[0].Groups[1].Value
}
Write-Output "== Nmoku 打包 v$Version =="

# 1) 导出模板检查（mono 版，须与引擎版本一致）
$templateDir = Join-Path $env:APPDATA "Godot\export_templates\4.6.2.stable.mono"
if (-not (Test-Path $templateDir)) {
    Write-Error ("导出模板缺失：$templateDir 不存在。`n" +
        "一次性安装：打开 Godot 编辑器 → 顶部菜单「编辑器 → 管理导出模板 → 下载并安装」（mono 版约 1GB），`n" +
        "或从 godotengine.org 下载 Godot_v4.6.2-stable_mono_export_templates.tpz 后在同一对话框「从文件安装」。")
    exit 1
}

# 2) 构建 C#（确保导出用的是最新程序集）
Write-Output "== dotnet build =="
dotnet build $project
if ($LASTEXITCODE -ne 0) { Write-Error "构建失败"; exit 1 }

# 3) 导出（release 模板，pck 内嵌单 exe + .NET 数据目录）
$outDir = Join-Path $repoRoot "build\Nmoku-$Version-win64"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Write-Output "== export-release =="
& $godot --headless --path $project --export-release "Windows Desktop" (Join-Path $outDir "Nmoku.exe")
if ($LASTEXITCODE -ne 0) { Write-Error "导出失败（检查 preset 名与模板版本）"; exit 1 }

# 4) 冒烟：打包产物能启动且打印 boot OK（校验启动日志，防"秒退也算过"的假阳性）
Write-Output "== 冒烟 =="
$exe = Join-Path $outDir "Nmoku.exe"
$smokeLog = Join-Path $outDir "smoke.log"
$proc = Start-Process $exe -ArgumentList "--headless" -PassThru -WindowStyle Hidden -RedirectStandardOutput $smokeLog
Start-Sleep -Seconds 6
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
$proc.WaitForExit()
$bootOk = (Get-Content $smokeLog -Raw -ErrorAction SilentlyContinue) -match "engine boot OK"
if (-not $bootOk) { Write-Error "冒烟失败：打包产物未打印 boot OK，详见 $smokeLog"; exit 1 }
Write-Output "冒烟通过（boot OK）"

# 5) 压缩发布 zip
$zip = Join-Path $repoRoot "build\Nmoku-$Version-win64.zip"
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force
Write-Output "== 完成：$zip =="
