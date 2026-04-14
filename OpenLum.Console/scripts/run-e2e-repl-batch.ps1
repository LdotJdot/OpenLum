# UTF-8. 默认回归：全量除 entire-10（耗时长）。完整含 10：.\run-e2e-repl-full.ps1
$ErrorActionPreference = "Stop"
$full = Join-Path $PSScriptRoot "run-e2e-repl-full.ps1"
& $full -SkipHeavy
