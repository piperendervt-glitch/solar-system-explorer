<#
.SYNOPSIS
    Unity の EditMode テストを batchmode で実行するラッパースクリプト。

.DESCRIPTION
    リポジトリ直下の unity/ を -projectPath として Unity を
    -batchmode -runTests -testPlatform EditMode で起動し、
      logs/unity_tests_yyyyMMdd_HHmmss.log   Unity のログ
      logs/testresults_yyyyMMdd_HHmmss.xml   NUnit3 形式のテスト結果
    を出力する。実行後に XML を読んで passed / failed / skipped の件数を出し、
    失敗したテストの完全名とメッセージを標準出力へ抜き出す。
    あわせて run_unity.ps1 と同じ "error CS" / "Failed to" / "Aborting batchmode"
    のログ走査も行う。

    設計上の注意:
      * -nographics は付けない。スクリーンショットによる目視検証で必要。
      * -runTests と -quit を併用しない。テスト完了前に Editor が終了してしまう。
        -runTests は完了後に自分で終了する。
      * Unity.exe は GUI サブシステムのアプリなので & 演算子で呼ぶと即座に戻り
        $LASTEXITCODE が取れない。Start-Process -PassThru の .ExitCode を使う。
      * Start-Process に -Wait を付けない。-Wait は Unity が起動した子プロセス
        (LicensingClient デーモン等) の終了まで待つため、Unity 本体が終わっても
        10 分近く戻ってこない。実測: Unity 本体 66 秒に対し -Wait の復帰は 668.8 秒。
        直接の子プロセスだけを待つために .WaitForExit() を明示的に呼ぶ。
      * Start-Process の直後に $null = $proc.Handle を実行してから待つ。
        PowerShell 5.1 では Handle に触れていないと後で .ExitCode が $null に
        なることがある。これは必須。
      * 待ちは WaitForExit() の無引数版ではなくミリ秒指定のオーバーロードを使う。
        -TimeoutMinutes を超えたらプロセスツリーごと落として 124 で抜ける。
      * 結果 XML は実行ごとにタイムスタンプ付きの新しいファイル名にする。
        固定パスだと、Unity が起動に失敗して XML を書けなかったときに
        前回の成功 XML を読んで OK と誤判定する。
      * 判定は XML を正とする。XML が無い / 壊れている場合は「不明」ではなく
        FAIL 扱いで非ゼロ終了する (Unity が起動すらできていない可能性が高いため)。
        Unity の ExitCode を見るのは「XML はあるが total=0」のときだけで、
        この分岐では 0 と 1 の両方を成功として扱う。
        2026-08-26 実測: $proc.Handle をキャッシュするようにしてからは
        0 件実行でも ExitCode=0 で安定している (3/3)。Handle 未取得だった
        ときだけ 1 と読めていた (2/2)。つまり以前 "0 件なら 1" と見えたのは
        Unity の挙動ではなく PowerShell 側の読み取りアーティファクト。
        再発しても落とさないよう 1 も許容値として残してある。
      * -projectPath はスクリプト自身の位置から解決するので、カレントディレクトリ
        に依存しない。

    終了コード:
        0     テストが 1 件も失敗しなかった (0 件実行を含む)
        1     テスト失敗 / コンパイルエラー / XML が無い・壊れている /
              Unity Editor・unity/ が見つからない等
        124   -TimeoutMinutes を超えたので強制終了した

.PARAMETER TestPlatform
    -testPlatform に渡す値。既定は EditMode。

.PARAMETER Filter
    -testFilter に渡す値 (省略可)。テスト完全名の前方一致 / 正規表現。

.PARAMETER ExtraArgs
    Unity にそのまま渡す追加引数の配列。

.PARAMETER TimeoutMinutes
    Unity プロセスの待ち時間の上限 (分)。既定 15。
    超えたらプロセスツリーごと強制終了し、終了コード 124 を返す。

.EXAMPLE
    .\tools\run_tests.ps1
    .\tools\run_tests.ps1 -Filter 'SolarSystem.Core.Tests'
    .\tools\run_tests.ps1 -TimeoutMinutes 30
#>
[CmdletBinding()]
param(
    [Parameter()][string]   $TestPlatform = 'EditMode',
    [Parameter()][string]   $Filter,
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

# NUnit3 の集計属性を安全に読む (属性が無ければ 0)。
function Get-RunCount {
    param($Node, [string]$Name)
    $v = $Node.GetAttribute($Name)
    if ([string]::IsNullOrEmpty($v)) { return 0 }
    return [int]$v
}

# ---- 既知の良性ログノイズ -----------------------------------------------------
# 本環境で正常終了時にも必ず出る行。ここに載せた文字列を「含む」行は
# "Failed to" 等の検出から除外し、「既知ノイズ」として別カウントする。
# **除外を足すときは必ずここに 1 行足す。判定ロジック側は触らない。**
# run_unity.ps1 にも同じ配列がある。両方に足すこと。
# 足す前に必ずログの前後を読み、本当に良性か確認すること。
$KnownBenignLogNoise = @(
    # Unity Hub 常駐の LicensingClient 由来。直後に
    # "[Licensing::Client] Successfully resolved entitlement details" が続き、
    # Unity Personal / Expiration: Unlimited でライセンス自体は解決している。
    '[Licensing::Module] Error: Access token is unavailable; failed to update'
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
    Write-Host "[run_tests] ERROR: Unity Editor が見つかりません:" -ForegroundColor Red
    Write-Host "[run_tests]        $unity"
    Write-Host "[run_tests] 環境変数 UNITY_EDITOR_PATH に Unity.exe の絶対パスを設定してください。"
    Write-Host "[run_tests] 例: `$env:UNITY_EDITOR_PATH = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe'"
    exit 1
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
    Write-Host "[run_tests] ERROR: Unity プロジェクトが見つかりません:" -ForegroundColor Red
    Write-Host "[run_tests]        $projectPath"
    Write-Host "[run_tests] Unity Hub で Universal 3D テンプレートのプロジェクトを unity/ として作成してください。"
    Write-Host "[run_tests] (存在しないパスを渡すと Unity が空プロジェクトを勝手に作るため、ここで止めます)"
    exit 1
}

if (-not (Test-Path -LiteralPath $logDir -PathType Container)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

# 結果 XML は実行ごとに新しいファイル名にする。固定パスにすると、Unity が
# 起動に失敗して XML を書けなかったときに前回の成功 XML をそのまま読んで
# 「OK」と誤判定する。念のため同名ファイルが既にあれば消してから走らせる。
$stamp       = Get-Date -Format 'yyyyMMdd_HHmmss'
$logFile     = Join-Path $logDir "unity_tests_$stamp.log"
$resultsFile = Join-Path $logDir "testresults_$stamp.xml"
if (Test-Path -LiteralPath $resultsFile) {
    Remove-Item -LiteralPath $resultsFile -Force
}

# ---- 引数の組み立て -----------------------------------------------------------
# -nographics は絶対に付けない (スクリーンショットによる目視検証で必要)。
# -quit も付けない (-runTests がテスト完了後に自分で終了する)。
$unityArgs = @('-batchmode', '-runTests')
$unityArgs += @('-testPlatform', (Format-Arg $TestPlatform))
$unityArgs += @('-projectPath',  (Format-Arg $projectPath))
$unityArgs += @('-logFile',      (Format-Arg $logFile))
$unityArgs += @('-testResults',  (Format-Arg $resultsFile))
if ($Filter) {
    $unityArgs += @('-testFilter', (Format-Arg $Filter))
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $unityArgs += @($ExtraArgs | ForEach-Object { Format-Arg $_ })
}

Write-Host "[run_tests] editor   : $unity"
Write-Host "[run_tests] project  : $projectPath"
Write-Host "[run_tests] platform : $TestPlatform"
Write-Host "[run_tests] logfile  : $logFile"
Write-Host "[run_tests] results  : $resultsFile"
Write-Host "[run_tests] args     : $($unityArgs -join ' ')"
Write-Host "[run_tests] timeout  : $TimeoutMinutes 分"
Write-Host "[run_tests] 起動中... (数十秒〜数分かかります)"

$sw   = [System.Diagnostics.Stopwatch]::StartNew()
# -Wait は使わない。Unity が起動した子プロセス (LicensingClient デーモン等) の
# 終了まで待ってしまい、Unity 本体が終わっても 10 分近く戻ってこない。
# 実測: Unity 本体 66 秒 / -Wait の復帰 668.8 秒。WaitForExit() は直接の子だけを待つ。
$proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -NoNewWindow -PassThru
# PowerShell 5.1 では .Handle に一度触れてキャッシュさせておかないと、
# プロセス終了後に .ExitCode が $null になることがある。必須。
$null = $proc.Handle
$timeoutMs = [int][Math]::Round($TimeoutMinutes * 60000)
$exited    = $proc.WaitForExit($timeoutMs)
$sw.Stop()

if (-not $exited) {
    Write-Host ''
    Write-Host ("[run_tests] ERROR: タイムアウト ({0} 分 / 実測 {1:N1} 秒)。Unity を強制終了します。" -f $TimeoutMinutes, $sw.Elapsed.TotalSeconds) -ForegroundColor Red
    Stop-ProcessTree -Process $proc
    Write-Host "[run_tests] タイムアウト時点のログ最終行 (最大 20 行):"
    if (Test-Path -LiteralPath $logFile -PathType Leaf) {
        foreach ($l in @(Get-Content -LiteralPath $logFile -Encoding UTF8 -Tail 20)) {
            Write-Host ("[run_tests] | {0}" -f $l)
        }
    } else {
        Write-Host "[run_tests] | (ログファイルがまだ生成されていません: $logFile)"
    }
    Write-Host "[run_tests] log     : $logFile"
    Write-Host "[run_tests] results : $resultsFile"
    Write-Host "[run_tests] 判定: TIMEOUT (終了コード 124)" -ForegroundColor Red
    exit 124
}

$unityExit = $proc.ExitCode
if ($null -eq $unityExit) { $unityExit = 1 }
Write-Host ("[run_tests] 終了: Unity ExitCode={0} / 所要 {1:N1} 秒" -f $unityExit, $sw.Elapsed.TotalSeconds)

# ---- テスト結果 XML の解析 ----------------------------------------------------
# Unity のテストランナーは NUnit3 形式で書き出す。
#   <test-run total= passed= failed= skipped= inconclusive= result= >
#   <test-case fullname= result= >   result: Passed / Failed / Skipped / Inconclusive
Write-Host ''
Write-Host '[run_tests] ---- テスト結果 ----'

$xmlOk       = $false
$xmlProblem  = ''
$failedCount = 0
$totalCount  = 0

if (-not (Test-Path -LiteralPath $resultsFile -PathType Leaf)) {
    $xmlProblem = "テスト結果 XML が生成されていません: $resultsFile"
    Write-Host "[run_tests] ERROR: $xmlProblem" -ForegroundColor Red
    Write-Host "[run_tests]        Unity がテスト実行前に落ちた可能性が高い。ログを読むこと。"
} else {
    $xmlDoc = $null
    try {
        $xmlDoc = [xml](Get-Content -LiteralPath $resultsFile -Encoding UTF8 -Raw)
    } catch {
        $xmlProblem = "テスト結果 XML を解析できません: $($_.Exception.Message)"
        Write-Host "[run_tests] ERROR: $xmlProblem" -ForegroundColor Red
    }

    if ($null -ne $xmlDoc) {
        $run = $xmlDoc.SelectSingleNode('/test-run')
        if ($null -eq $run) {
            $xmlProblem = "XML に test-run 要素がありません: $resultsFile"
            Write-Host "[run_tests] ERROR: $xmlProblem" -ForegroundColor Red
        } else {
            $xmlOk        = $true
            $totalCount   = Get-RunCount $run 'total'
            $passedCount  = Get-RunCount $run 'passed'
            $failedCount  = Get-RunCount $run 'failed'
            $skippedCount = Get-RunCount $run 'skipped'
            $inconclCount = Get-RunCount $run 'inconclusive'

            Write-Host ("[run_tests]   {0,-14} : {1} 件" -f 'total',        $totalCount)
            Write-Host ("[run_tests]   {0,-14} : {1} 件" -f 'passed',       $passedCount)
            Write-Host ("[run_tests]   {0,-14} : {1} 件" -f 'failed',       $failedCount)
            Write-Host ("[run_tests]   {0,-14} : {1} 件" -f 'skipped',      $skippedCount)
            Write-Host ("[run_tests]   {0,-14} : {1} 件" -f 'inconclusive', $inconclCount)

            if ($totalCount -eq 0) {
                Write-Host "[run_tests] テストは 0 件です (テストアセンブリが空)。Step 0 ではこれが正常。"
            }

            # ---- 失敗テストの列挙 ----
            $failures = @($xmlDoc.SelectNodes("//test-case[@result='Failed']"))
            foreach ($t in $failures) {
                Write-Host ("[FAILED] {0}" -f $t.GetAttribute('fullname')) -ForegroundColor Red
                $msg = $t.SelectSingleNode('failure/message')
                if ($null -ne $msg) {
                    foreach ($line in ($msg.InnerText -split "`r?`n")) {
                        if ($line.Trim()) { Write-Host ("           {0}" -f $line.Trim()) }
                    }
                }
                $trace = $t.SelectSingleNode('failure/stack-trace')
                if ($null -ne $trace) {
                    foreach ($line in ($trace.InnerText -split "`r?`n")) {
                        if ($line.Trim()) { Write-Host ("           at {0}" -f $line.Trim()) }
                    }
                }
            }
            if ($failures.Count -eq 0 -and $failedCount -gt 0) {
                Write-Host "[run_tests] WARNING: failed=$failedCount だが Failed な test-case が見つかりません。XML を直接確認してください。"
            }
        }
    }
}

# ---- ログ走査 (run_unity.ps1 と同じパターン) ----------------------------------
# $KnownBenignLogNoise に載っている行は検出から外し、「既知ノイズ」として別に数える。
$patterns    = @('error CS', 'Failed to', 'Aborting batchmode')
$counts      = [ordered]@{}
$noiseCounts = [ordered]@{}
foreach ($p in $patterns) { $counts[$p] = 0; $noiseCounts[$p] = 0 }

Write-Host ''
Write-Host '[run_tests] ---- ログ走査 ----'
if (-not (Test-Path -LiteralPath $logFile -PathType Leaf)) {
    Write-Host "[run_tests] WARNING: ログファイルが生成されていません: $logFile"
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

$totalHits  = 0
$totalNoise = 0
foreach ($p in $patterns) { $totalHits += $counts[$p]; $totalNoise += $noiseCounts[$p] }

Write-Host '[run_tests] ---- サマリ ----'
foreach ($p in $patterns) {
    if ($noiseCounts[$p] -gt 0) {
        Write-Host ("[run_tests]   {0,-20} : {1} 件   (既知ノイズ: {2} 件（無視）)" -f $p, $counts[$p], $noiseCounts[$p])
    } else {
        Write-Host ("[run_tests]   {0,-20} : {1} 件" -f $p, $counts[$p])
    }
}
Write-Host ("[run_tests]   {0,-20} : {1} 件" -f 'TOTAL', $totalHits)
Write-Host ("[run_tests]   {0,-20} : {1} 件（無視）" -f '既知ノイズ', $totalNoise)
Write-Host "[run_tests] log     : $logFile"
Write-Host "[run_tests] results : $resultsFile"

# ---- 終了コード ---------------------------------------------------------------
# 判定は XML を正とする。XML が無い / 壊れている場合は「不明」ではなく FAIL。
# Unity の ExitCode を見るのは「XML はあるが total=0」のときだけ。
if (-not $xmlOk) {
    Write-Host "[run_tests] 判定: FAIL (テスト結果 XML から判定できない)" -ForegroundColor Red
    Write-Host "[run_tests]        理由: $xmlProblem"
    Write-Host "[run_tests]        Unity ExitCode=$unityExit だが、XML が無い以上これは信用しない。"
    exit 1
}

if ($failedCount -gt 0) {
    Write-Host "[run_tests] 判定: FAIL (失敗 $failedCount 件)" -ForegroundColor Red
    exit 1
}

if ($counts['error CS'] -gt 0) {
    Write-Host ("[run_tests] 判定: FAIL (コンパイルエラー {0} 件)" -f $counts['error CS']) -ForegroundColor Red
    exit 1
}

if ($totalCount -eq 0) {
    # テストが 1 件も走らなかったケースだけ Unity の ExitCode も見る。
    # Handle キャッシュ後の実測は 0 で安定 (3/3)。1 は Handle 未取得時に
    # 読めていた値で Unity の挙動ではないが、再発時に落とさないよう許容する。
    # 3 (run failed) などはここで拾う。
    if ($unityExit -eq 0 -or $unityExit -eq 1) {
        Write-Host "[run_tests] 判定: OK (total=0 / failed=0 / Unity ExitCode=$unityExit は 0 件時の許容値)"
        exit 0
    }
    Write-Host "[run_tests] 判定: FAIL (total=0 かつ Unity ExitCode=$unityExit は許容値 0/1 以外)" -ForegroundColor Red
    exit 1
}

Write-Host "[run_tests] 判定: OK (total=$totalCount / failed=0)"
exit 0
