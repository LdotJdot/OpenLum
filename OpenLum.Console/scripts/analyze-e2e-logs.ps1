# 扫描近期会话日志：sessions_spawn、Error:、assistant 轮次粗算（回归对比用）
param(
  [string]$LogDate = (Get-Date -Format "yyyy-MM-dd"),
  [int]$MaxFiles = 80
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$logDir = Join-Path $root ".openlum\logs\$LogDate"

if (-not (Test-Path $logDir)) {
  Write-Warning "No logs: $logDir"
  exit 0
}

$rows = Get-ChildItem $logDir -Filter *.log |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First $MaxFiles |
  ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    $spawn = $false
    $toolRounds = 0
    $modelTurns = 0
    $hasError = $false
    foreach ($line in $lines) {
      if ($line -match "assistant_tool_calls:\s+.*sessions_spawn") { $spawn = $true }
      if ($line -match "^\[.*\]\s+assistant_tool_calls:\s+" -and $line -notmatch "\(none\)\s*$") { $toolRounds++ }
      if ($line -match "model_turn=") { $modelTurns++ }
      if ($line -match "\[OpenLum\].*failed|Error:\s|assistant_tool_calls:\s+.*error") { $hasError = $true }
    }
    [PSCustomObject]@{
      Name       = $_.Name
      LastWrite  = $_.LastWriteTime.ToString("HH:mm:ss")
      Spawn      = $spawn
      ToolRounds = $toolRounds
      ModelTurns = $modelTurns
      Flag       = if ($spawn) { "SPAWN" } elseif ($hasError) { "ERR?" } else { "" }
    }
  }

$rows | Format-Table -AutoSize
$spawnN = ($rows | Where-Object { $_.Spawn }).Count
if ($spawnN -gt 0) {
  Write-Host "NOTE: $spawnN log(s) mention sessions_spawn in assistant_tool_calls (expected only if case requires)." -ForegroundColor Yellow
}
