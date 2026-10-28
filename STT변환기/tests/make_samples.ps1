# 테스트용 샘플 .wav 생성 스크립트 (Windows PowerShell 5.1, 한국어 TTS 음성 'Microsoft Heami Desktop' 필요)
# 사용법: powershell -ExecutionPolicy Bypass -File tests\make_samples.ps1 [-Long]
#   -Long : 65분짜리 긴 파일(긴파일_65분.wav)도 만든다 (약 125MB)
param([switch]$Long)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Speech

$outDir = Join-Path $PSScriptRoot "samples"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outDir "하위 폴더 (2팀)") | Out-Null

$text = "오늘 병리과 회의에서는 조직검사 결과 보고 절차를 논의하겠습니다. " +
        "검체 접수부터 판독까지 걸리는 시간을 줄이는 방안이 필요합니다. " +
        "다음 회의는 다음 주 화요일 오후 두 시에 열립니다."

function New-SpeechWav {
    param([string]$Path, [int]$Rate, $Channel, [string]$Text)
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice("Microsoft Heami Desktop")
        $fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
            $Rate, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, $Channel)
        $synth.SetOutputToWaveFile($Path, $fmt)
        $synth.Speak($Text)
    } finally {
        $synth.Dispose()   # 파일 핸들을 닫아야 완성된 wav가 된다
    }
    Write-Host "생성: $Path"
}

$mono = [System.Speech.AudioFormat.AudioChannel]::Mono
$stereo = [System.Speech.AudioFormat.AudioChannel]::Stereo

# 1) 기본 16kHz 모노, 2) 전화 8kHz 모노, 3) 44.1kHz 스테레오
$base = Join-Path $outDir "기본_16k.wav"
New-SpeechWav -Path $base -Rate 16000 -Channel $mono -Text $text
New-SpeechWav -Path (Join-Path $outDir "전화_8k.wav") -Rate 8000 -Channel $mono -Text $text
New-SpeechWav -Path (Join-Path $outDir "스테레오_44k.wav") -Rate 44100 -Channel $stereo -Text $text

# 4) 하위 폴더 + 공백·괄호·# 이 들어간 파일명
New-SpeechWav -Path (Join-Path $outDir "하위 폴더 (2팀)\회의 #1.wav") -Rate 16000 -Channel $mono -Text $text

# PCM 데이터만 담은 wav를 직접 쓴다 (16bit, 모노)
function Write-PcmWav {
    param([string]$Path, [int]$Rate, [int]$Channels, [byte[]]$Pcm, [int]$Repeat = 1)
    $dataLen = [long]$Pcm.Length * $Repeat
    $fs = [System.IO.File]::Create($Path)
    try {
        $bw = New-Object System.IO.BinaryWriter($fs)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
        $bw.Write([uint32](36 + $dataLen))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
        $bw.Write([uint32]16)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$Channels)
        $bw.Write([uint32]$Rate)
        $bw.Write([uint32]($Rate * $Channels * 2))
        $bw.Write([uint16]($Channels * 2))
        $bw.Write([uint16]16)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
        $bw.Write([uint32]$dataLen)
        for ($i = 0; $i -lt $Repeat; $i++) { $bw.Write($Pcm) }
        $bw.Flush()
    } finally {
        $fs.Dispose()
    }
    Write-Host "생성: $Path"
}

# 5) 무음 10초 (16kHz 모노 16bit)
Write-PcmWav -Path (Join-Path $outDir "무음.wav") -Rate 16000 -Channels 1 -Pcm (New-Object byte[] (16000 * 2 * 10))

# 6) 빈 파일(0바이트), 7) 가짜 wav(텍스트 내용)
[System.IO.File]::WriteAllBytes((Join-Path $outDir "빈파일.wav"), [byte[]]@())
[System.IO.File]::WriteAllText((Join-Path $outDir "가짜.wav"), "이것은 wav가 아니라 텍스트 파일입니다.", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "생성: 빈파일.wav, 가짜.wav"

# 8) 긴 파일: 기본_16k.wav의 PCM 데이터를 반복해 65분 분량으로 만든다 (-Long 일 때만)
if ($Long) {
    $bytes = [System.IO.File]::ReadAllBytes($base)
    # 'data' 청크를 찾아 PCM 위치를 구한다
    $pos = 12
    $pcmStart = -1; $pcmLen = 0
    while ($pos + 8 -le $bytes.Length) {
        $id = [System.Text.Encoding]::ASCII.GetString($bytes, $pos, 4)
        $size = [BitConverter]::ToUInt32($bytes, $pos + 4)
        if ($id -eq "data") { $pcmStart = $pos + 8; $pcmLen = [int]([Math]::Min($size, $bytes.Length - $pcmStart)); break }
        $pos += 8 + $size
    }
    if ($pcmStart -lt 0) { throw "기본_16k.wav에서 data 청크를 찾지 못했습니다." }
    $pcm = New-Object byte[] $pcmLen
    [Array]::Copy($bytes, $pcmStart, $pcm, 0, $pcmLen)
    $secPerCopy = $pcmLen / (16000 * 2)
    $repeat = [int][Math]::Ceiling(65 * 60 / $secPerCopy)
    Write-PcmWav -Path (Join-Path $outDir "긴파일_65분.wav") -Rate 16000 -Channels 1 -Pcm $pcm -Repeat $repeat
}

Write-Host "샘플 생성 완료: $outDir"
