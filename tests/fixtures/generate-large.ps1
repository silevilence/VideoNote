param([string]$OutputPath = (Join-Path $PSScriptRoot '../../work-tests/large.mp4'))
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $target) { throw '目标文件已存在，请指定新的测试输出路径。' }
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
& ffmpeg -hide_banner -loglevel error -nostdin -f lavfi -i 'color=red:size=64x64:rate=1:duration=1' -c:v libx264 $target
if ($LASTEXITCODE -ne 0) { throw '样例生成失败。' }
$stream = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::Write)
try {
    $stream.Seek(0,[IO.SeekOrigin]::End) | Out-Null
    $stream.Write([byte[]](0x14,0,0,0,0x66,0x72,0x65,0x65))
    $stream.SetLength($stream.Length + 320MB - 8)
} finally { $stream.Dispose() }
Write-Output '已生成含 320 MiB free box 的有效 MP4，可用于大文件流式上传验证。'
