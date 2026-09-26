# bake-audio.ps1 —— 占位音频管线：AudioBake 生成 WAV → BGM 转 OGG → 入库 assets/audio
# 用法：powershell -File scripts/bake-audio.ps1   （仓库根执行；需 ffmpeg 在 PATH）
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$tmp = Join-Path $env:TEMP "nmoku-audio-bake"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Path $tmp | Out-Null

dotnet run --project tools/AudioBake -- $tmp
if ($LASTEXITCODE -ne 0) { throw "AudioBake 失败" }

$dest = "src/Nmoku.Game/assets/audio"
New-Item -ItemType Directory -Path $dest -Force | Out-Null

# BGM：转 OGG（q4，单声道源），占位循环由 AudioManager 运行时设 loop
Get-ChildItem "$tmp/bgm_*.wav" | ForEach-Object {
    $out = Join-Path $dest ($_.BaseName + ".ogg")
    ffmpeg -y -loglevel error -i $_.FullName -c:a libvorbis -qscale:a 4 $out
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg 转码失败: $($_.Name)" }
    Write-Host "ogg  $($out)  $([math]::Round((Get-Item $out).Length/1KB)) KB"
}

# SFX：短样本直接用 WAV（无解码首帧延迟；源采样率 22050）
Get-ChildItem "$tmp/sfx_*.wav" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $dest $_.Name) -Force
    Write-Host "wav  $($_.Name)  $([math]::Round($_.Length/1KB)) KB"
}

Remove-Item $tmp -Recurse -Force
Write-Host "bake-audio 完成"
