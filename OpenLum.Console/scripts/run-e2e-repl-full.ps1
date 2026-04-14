# UTF-8. 全量 REPL 回归：entire-01..13 + lines-02 + lines-03（仓库根 = OpenLum.Console\scripts -> ..\..）
param(
  # 跳过 entire-10（桌面大目录分析，耗 API 与时長）；其余仍含 11-13（桌面路径）
  [switch]$SkipHeavy
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $root

$exDir = Join-Path $root "OpenLum.Console\e2e-repl-examples"
$proj = Join-Path $root "OpenLum.Console\OpenLum.Console.csproj"

$entire = @(
  "entire-01-read-not-list.example.txt",
  "entire-02-grep-then-read.example.txt",
  "entire-03-listdir-glob.example.txt",
  "entire-04-readonly-batch.example.txt",
  "entire-05-workflow-exec.example.txt",
  "entire-06-accuracy-namespace.example.txt",
  "entire-07-count-files-grep.example.txt",
  "entire-08-glob-read-efficiency.example.txt",
  "entire-09-parallel-folder-md.example.txt"
)
if (-not $SkipHeavy) {
  $entire += "entire-10-fly-support-desktop.example.txt"
}
$entire += @(
  "entire-11-read-docx-sample-math.example.txt",
  "entire-12-fly-search-craes.example.txt",
  "entire-13-fly-multi-search.example.txt"
)

$n = 0
foreach ($t in $entire) {
  $n++
  $p = Join-Path $exDir $t
  Write-Host "`n========== ENTIRE $n / $($entire.Count) : $t =========="
  & dotnet run --project $proj -c Debug --no-build -- --repl-file $p --repl-file-entire 2>&1 | Select-Object -Last 20
  if ($LASTEXITCODE -ne 0) {
    Write-Host "EXIT $LASTEXITCODE for $t" -ForegroundColor Red
    exit $LASTEXITCODE
  }
}

Write-Host "`n========== LINE : lines-02-two-turn =========="
$p = Join-Path $exDir "lines-02-two-turn.example.txt"
& dotnet run --project $proj -c Debug --no-build -- --repl-file $p 2>&1 | Select-Object -Last 26
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n========== LINE : lines-03-three-turn-accuracy =========="
$p = Join-Path $exDir "lines-03-three-turn-accuracy.example.txt"
& dotnet run --project $proj -c Debug --no-build -- --repl-file $p 2>&1 | Select-Object -Last 30
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== done $($entire.Count) entire + 2 line (full). Run scripts\analyze-e2e-logs.ps1 ==="
