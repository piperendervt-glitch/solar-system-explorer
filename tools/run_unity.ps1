<#
.SYNOPSIS
    Unity Editor を batchmode で起動するラッパースクリプト。

.DESCRIPTION
    リポジトリ直下の unity/ を -projectPath として Unity を batchmode 起動し、
    logs/unity_yyyyMMdd_HHmmss.log にログを出力する。
    実行後にログを走査し、"error CS" / "Failed to" / "Aborting batchmode" を
    含む行を標準出力へ抜き出して件数サマリを表示する。

    設計上の注意:
      * -nographics は付けない。スクリーンショットによる目視検証に必要。
      * Unity.exe は GUI サブシステムのアプリなので & 演算子で呼ぶと即座に戻り
        $LASTEXITCODE が取れない。Start-Process -PassThru の .ExitCode を使う。
      * Start-Process に -Wait を付けない。-Wait は Unity が起動した子プロセス
        (LicensingClient デーモン等) の終了まで待つため、Unity 本体が終了しても
        戻ってこないことがある。実測: 本体 66 秒の実行で -Wait の復帰が 668.8 秒。
      * Start-Process の直後に $null = $proc.Handle を実行してから待つ。
        PowerShell 5.1 では Handle に触れていないと後で .ExitCode が $null に
        なることがある。これは必須。
      * 待ちは WaitForExit() の無引数版ではなくミリ秒指定のオーバーロードを使う。
        -TimeoutMinutes を超えたらプロセスツリーごと落として 124 で抜ける。
      * -projectPath はスクリプト自身の位置から解決するので、カレントディレクトリ
        に依存しない。

    終了コード:
        0     Unity が正常終了 (= Unity の ExitCode をそのまま返す)
        1     Unity Editor / unity/ が見つからない等、起動前のエラー
        124   -TimeoutMinutes を超えたので強制終了した
        その他 Unity の ExitCode

.PARAMETER Method
    -executeMethod に渡す静的メソッド名 (例: SolarSetup.Run)。
    省略した場合は -executeMethod を付けず -batchmode -quit のみで起動する。
    Step 0 時点では SolarSetup.Run が存在しないため、必ず省略して呼ぶこと。

.PARAMETER ExtraArgs
    Unity にそのまま渡す追加引数の配列。

.PARAMETER TimeoutMinutes
    Unity プロセスの待ち時間の上限 (分)。既定 15。
    超えたらプロセスツリーごと強制終了し、終了コード 124 を返す。

.EXAMPLE
    .\tools\run_unity.ps1
    .\tools\run_unity.ps1 -Method SolarSetup.Run
    .\tools\run_unity.ps1 -Method SolarSetup.Run -ExtraArgs '-outDir','verify/shots'
    .\tools\run_unity.ps1 -TimeoutMinutes 30
#>
[CmdletBinding()]
param(
    [Parameter()][string]   $Method,
    [Parameter()][string[]] $ExtraArgs = @(),
    [Parameter()][double]   $TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'

# Start-Process -ArgumentList は配列要素を空白で連結するだけで自動的に引用符を
# 付けない。空白や引用符を含む値は自前で括る必要がある。
function Format-Arg {
    param([string]$Value)
    if ($Value -match '[\s"]') { return '"' + ($Value -replace '"', '\"') + '"' }
    return $Value
}

# ---- 既知の良性ログノイズ -----------------------------------------------------
# 本環境で正常終了時にも必ず出る行。ここに載せた文字列を「含む」行は
# "Failed to" 等の検出から除外し、「既知ノイズ」として別カウントする。
# **除外を足すときは必ずここに 1 行足す。判定ロジック側は触らない。**
# 足す前に必ずログの前後を読み、本当に良性か確認すること。
$KnownBenignLogNoise = @(
    # Unity Hub 常駐の LicensingClient 由来。直後に
    # "[Licensing::Client] Successfully resolved entitlement details" が続き、
    # Unity Personal / Expiration: Unlimited でライセンス自体は解決している。
    '[Licensing::Module] Error: Access token is unavailable; failed to update',
    # Unity Hub 常駐の LicensingClient がプロトコル 1.18.0 を返して弾かれる (ResponseCode: 505)。
    # 直後に Editor が自前の "LicenseClient-pipe_render-6000.3.11" を起動し直して
    # 接続に成功し、Unity Personal で entitlement が解決している (2026-08-26 実測)。
    '[Licensing::Module] Error: Failed to handshake to channel:'
)

# 既知ノイズかどうか。-like や -match は [ ] を wildcard / 正規表現として
# 解釈してしまうので、必ず序数の部分文字列一致 (String.Contains) で判定する。
function Test-BenignNoise {
    param([string]$Line)
    foreach ($n in $KnownBenignLogNoise) {
        if ($Line.Contains($n)) { return $true }
    }
    return $false
}

# .NET Framework 4.x の Process.Kill() には entireProcessTree オーバーロードが
# 無く、自分自身しか殺せない。Unity の子 (LicensingClient 等) ごと落とすため
# taskkill /T /F を併用し、取りこぼしたときだけ Kill() にフォールバックする。
function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)
    try { & taskkill.exe /PID $Process.Id /T /F | Out-Null } catch { }
    try { if (-not $Process.HasExited) { $Process.Kill() } } catch { }
    try { [void]$Process.WaitForExit(10000) } catch { }
}

# ---- パス解決 (カレントディレクトリに依存させない) ----------------------------
$repoRoot    = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'unity'
$logDir      = Join-Path $repoRoot 'logs'

# ---- Unity Editor の解決 ------------------------------------------------------
$defaultEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe'
if ($env:UNITY_EDITOR_PATH) { $unity = $env:UNITY_EDITOR_PATH } else { $unity = $defaultEditor }

if (-not (Test-Path -LiteralPath $unity -PathType Leaf)) {
    Write-Host "[run_unity] ERROR: Unity Editor が見つかりません:" -ForegroundColor Red
    Write-Host "[run_unity]        $unity"
    Write-Host "[run_unity] 環境変数 UNITY_EDITOR_PATH に Unity.exe の絶対パスを設定してください。"
    Write-Host "[run_unity] 例: `$env:UNITY_EDITOR_PATH = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe'"
    exit 1
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
    Write-Host "[run_unity] ERROR: Unity プロジェクトが見つかりません:" -ForegroundColor Red
    Write-Host "[run_unity]        $projectPath"
    Write-Host "[run_unity] Unity Hub で Universal 3D テンプレートのプロジェクトを unity/ として作成してください。"
    Write-Host "[run_unity] (存在しないパスを渡すと Unity が空プロジェクトを勝手に作るため、ここで止めます)"
    exit 1
}

if (-not (Test-Path -LiteralPath $logDir -PathType Container)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$stamp   = Get-Date -Format 'yyyyMMdd_HHmmss'
$logFile = Join-Path $logDir "unity_$stamp.log"

# ---- 引数の組み立て -----------------------------------------------------------
# -nographics は絶対に付けない (スクリーンショットによる目視検証で必要)。
$unityArgs = @('-batchmode', '-quit')
$unityArgs += @('-projectPath', (Format-Arg $projectPath))
$unityArgs += @('-logFile',     (Format-Arg $logFile))
if ($Method) {
    $unityArgs += @('-executeMethod', (Format-Arg $Method))
} else {
    Write-Host "[run_unity] -Method 省略: -executeMethod を付けずに起動します。"
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $unityArgs += @($ExtraArgs | ForEach-Object { Format-Arg $_ })
}

Write-Host "[run_unity] editor  : $unity"
Write-Host "[run_unity] project : $projectPath"
Write-Host "[run_unity] logfile : $logFile"
Write-Host "[run_unity] args    : $($unityArgs -join ' ')"
Write-Host "[run_unity] timeout : $TimeoutMinutes 分"
Write-Host "[run_unity] 起動中... (数十秒〜数分かかります)"

# -Wait は使わない (子プロセスの終了まで待ってしまう)。直接の子だけを待つ。
$sw   = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -NoNewWindow -PassThru
# PowerShell 5.1 では .Handle に一度触れてキャッシュさせておかないと、
# プロセス終了後に .ExitCode が $null になることがある。必須。
$null = $proc.Handle
$timeoutMs = [int][Math]::Round($TimeoutMinutes * 60000)
$exited    = $proc.WaitForExit($timeoutMs)
$sw.Stop()

if (-not $exited) {
    Write-Host ''
    Write-Host ("[run_unity] ERROR: タイムアウト ({0} 分 / 実測 {1:N1} 秒)。Unity を強制終了します。" -f $TimeoutMinutes, $sw.Elapsed.TotalSeconds) -ForegroundColor Red
    Stop-ProcessTree -Process $proc
    Write-Host "[run_unity] タイムアウト時点のログ最終行 (最大 20 行):"
    if (Test-Path -LiteralPath $logFile -PathType Leaf) {
        foreach ($l in @(Get-Content -LiteralPath $logFile -Encoding UTF8 -Tail 20)) {
            Write-Host ("[run_unity] | {0}" -f $l)
        }
    } else {
        Write-Host "[run_unity] | (ログファイルがまだ生成されていません: $logFile)"
    }
    Write-Host "[run_unity] log: $logFile"
    Write-Host "[run_unity] 判定: TIMEOUT (終了コード 124)" -ForegroundColor Red
    exit 124
}

$exitCode = $proc.ExitCode
if ($null -eq $exitCode) { $exitCode = 1 }
Write-Host ("[run_unity] 終了: ExitCode={0} / 所要 {1:N1} 秒" -f $exitCode, $sw.Elapsed.TotalSeconds)

# ---- ログ走査 -----------------------------------------------------------------
# $KnownBenignLogNoise に載っている行は検出から外し、「既知ノイズ」として別に数える。
$patterns    = @('error CS', 'Failed to', 'Aborting batchmode')
$counts      = [ordered]@{}
$noiseCounts = [ordered]@{}
foreach ($p in $patterns) { $counts[$p] = 0; $noiseCounts[$p] = 0 }

Write-Host ''
Write-Host '[run_unity] ---- ログ走査 ----'
if (-not (Test-Path -LiteralPath $logFile -PathType Leaf)) {
    Write-Host "[run_unity] WARNING: ログファイルが生成されていません: $logFile"
} else {
    $lines = @(Get-Content -LiteralPath $logFile -Encoding UTF8)
    foreach ($p in $patterns) {
        $hits = @($lines | Select-String -SimpleMatch -Pattern $p)
        foreach ($h in $hits) {
            if (Test-BenignNoise -Line $h.Line) {
                $noiseCounts[$p]++
                Write-Host ("[既知ノイズ/{0}] line {1}: {2}" -f $p, $h.LineNumber, $h.Line.Trim())
            } else {
                $counts[$p]++
                Write-Host ("[{0}] line {1}: {2}" -f $p, $h.LineNumber, $h.Line.Trim())
            }
        }
    }
}

$total      = 0
$totalNoise = 0
foreach ($p in $patterns) { $total += $counts[$p]; $totalNoise += $noiseCounts[$p] }

Write-Host '[run_unity] ---- サマリ ----'
foreach ($p in $patterns) {
    if ($noiseCounts[$p] -gt 0) {
        Write-Host ("[run_unity]   {0,-20} : {1} 件   (既知ノイズ: {2} 件（無視）)" -f $p, $counts[$p], $noiseCounts[$p])
    } else {
        Write-Host ("[run_unity]   {0,-20} : {1} 件" -f $p, $counts[$p])
    }
}
Write-Host ("[run_unity]   {0,-20} : {1} 件" -f 'TOTAL', $total)
Write-Host ("[run_unity]   {0,-20} : {1} 件（無視）" -f '既知ノイズ', $totalNoise)
Write-Host "[run_unity] log: $logFile"

exit $exitCode
