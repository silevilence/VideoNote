$ErrorActionPreference = 'Stop'
$fixtureDirectory = $PSScriptRoot
& ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i 'testsrc2=size=160x90:rate=5:duration=125' -f lavfi -i 'sine=frequency=440:sample_rate=16000:duration=125' -i (Join-Path $fixtureDirectory 'subtitles.srt') -map 0:v -map 1:a -map 2:s -c:v libx264 -preset ultrafast -crf 35 -c:a aac -c:s srt (Join-Path $fixtureDirectory 'sample.mkv')
if ($LASTEXITCODE -ne 0) { throw '样例视频生成失败。' }
