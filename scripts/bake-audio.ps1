# bake-audio.ps1 —— 音频资产同步与落子采样烘焙管线
# 用法：
#   powershell -File scripts/bake-audio.ps1               # 重烘黑白子落盘/点击/悬停采样（不覆盖正式 BGM/胜负）
#   powershell -File scripts/bake-audio.ps1 -SyncMusic    # 同时从同级 ../programmed-music/out/nmoku_ogg/ 认领正式 BGM 与胜负音效
param(
    [switch]$SyncMusic
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$dest = "src/Nmoku.Game/assets/audio"
New-Item -ItemType Directory -Path $dest -Force | Out-Null

if ($SyncMusic) {
    $srcMusic = Join-Path (Resolve-Path "..").Path "programmed-music/out/nmoku_ogg"
    if (-not (Test-Path $srcMusic)) {
        throw "未找到正式音频目录: $srcMusic"
    }
    $claimed = @("bgm_menu.ogg", "bgm_game.ogg", "bgm_game_2.ogg", "bgm_guide.ogg", "bgm_pause.ogg", "sfx_win.wav", "sfx_lose.wav")
    foreach ($f in $claimed) {
        $from = Join-Path $srcMusic $f
        if (-not (Test-Path $from)) { throw "缺少正式音频文件: $from" }
        Copy-Item $from (Join-Path $dest $f) -Force
        Write-Host "sync $f ($([math]::Round((Get-Item (Join-Path $dest $f)).Length/1KB)) KB)"
    }
}

$env:MSBUILDDISABLENODEREUSE = "1"
dotnet run --project tools/AudioBake -m:1 -p:UseSharedCompilation=false -- --sfx-only $dest
if ($LASTEXITCODE -ne 0) { throw "AudioBake 失败" }

Write-Host "bake-audio 完成（记得运行 Godot --headless --import 刷新 .import 元数据）"