# UTF-8. 多样化复杂 REPL：complex-01 .. complex-10（仓库根）；可选加 -IncludeLines04 跑四轮链式脚本
param(
  [switch]$IncludeLines04
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $root

$exDir = Join-Path $root "OpenLum.Console\e2e-repl-examples"
$proj = Join-Path $root "OpenLum.Console\OpenLum.Console.csproj"

$batch = @(
  "complex-01-grep-sealed-tools.example.txt",
  "complex-02-readmany-header-parity.example.txt",
  "complex-03-exec-dotnet-sln-list.example.txt",
  "complex-04-listdir-grep-dual-metric.example.txt",
  "complex-05-glob-read-application-register.example.txt",
  "complex-06-todo-merge-two-rounds.example.txt",
  "complex-07-grep-json-agent-maxturns.example.txt",
  "complex-08-fixture-md-three-nouns.example.txt",
  "complex-09-memory-search-none.example.txt",
  "complex-10-plan-then-grep-read.example.txt"
)

$n = 0
foreach ($t in $batch) {
  $n++
  $p = Join-Path $exDir $t
  Write-Host "`n========== COMPLEX $n / $($batch.Count) : $t =========="
  & dotnet run --project $proj -c Debug --no-build -- --repl-file $p --repl-file-entire 2>&1 | Select-Object -Last 24
  if ($LASTEXITCODE -ne 0) {
    Write-Host "EXIT $LASTEXITCODE for $t" -ForegroundColor Red
    exit $LASTEXITCODE
  }
}

if ($IncludeLines04) {
  Write-Host "`n========== LINE : lines-04-complex-four-turn =========="
  $p = Join-Path $exDir "lines-04-complex-four-turn.example.txt"
  & dotnet run --project $proj -c Debug --no-build -- --repl-file $p 2>&1 | Select-Object -Last 32
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "`n=== done $($batch.Count) complex$(if ($IncludeLines04) { ' + lines-04' }). Run scripts\analyze-e2e-logs.ps1 -LogDate (Get-Date -Format yyyy-MM-dd) ==="
