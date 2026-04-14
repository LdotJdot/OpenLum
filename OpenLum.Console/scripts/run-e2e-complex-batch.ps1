# UTF-8. 复杂任务：多步推理、计数、glob+read、多轮记忆 — 用于准确率与效率回归
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $root

$exDir = Join-Path $root "OpenLum.Console\e2e-repl-examples"
$proj = Join-Path $root "OpenLum.Console\OpenLum.Console.csproj"

$entire = @(
  "entire-06-accuracy-namespace.example.txt",
  "entire-07-count-files-grep.example.txt",
  "entire-08-glob-read-efficiency.example.txt",
  "entire-09-parallel-folder-md.example.txt"
)

$n = 0
foreach ($t in $entire) {
  $n++
  $p = Join-Path $exDir $t
  Write-Host "`n========== COMPLEX ENTIRE $n : $t =========="
  & dotnet run --project $proj -c Debug --no-build -- --repl-file $p --repl-file-entire 2>&1 | Select-Object -Last 24
}

Write-Host "`n========== COMPLEX LINE : lines-03-three-turn-accuracy =========="
$p = Join-Path $exDir "lines-03-three-turn-accuracy.example.txt"
& dotnet run --project $proj -c Debug --no-build -- --repl-file $p 2>&1 | Select-Object -Last 32

Write-Host "`n=== done $($entire.Count) entire + 1 line (complex) ==="
