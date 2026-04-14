# 扫描最近会话日志：是否出现 sessions_spawn、模型轮次与含工具轮次（粗粒度效率）
param(
  [string]$LogDate = (Get-Date -Format "yyyy-MM-dd"),
  [int]$MaxFiles = 48
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$logDir = Join-Path $root ".openlum\logs\$LogDate"

if (-not (Test-Path $logDir)) {
  Write-Warning "目录不存在: $logDir"
  exit 0
}

$rows = Get-ChildItem $logDir -Filter *.log |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First $MaxFiles |
  ForEach-Object {
    $lines = Get-Content -LiteralPath $_.FullName -ErrorAction SilentlyContinue
    if (-not $lines) { return }
    $spawn = $false
    $toolCalls = 0
    $modelTurns = 0
    foreach ($line in $lines) {
      if ($line -match "assistant_tool_calls:\s+.*sessions_spawn") { $spawn = $true }
      if ($line -match "^\[.*\]\s+assistant_tool_calls:\s+" -and $line -notmatch "\(none\)\s*$") { $toolCalls++ }
      if ($line -match "model_turn=") { $modelTurns++ }
    }
    [PSCustomObject]@{
      Name         = $_.Name
      LastWrite    = $_.LastWriteTime.ToString("HH:mm:ss")
      Spawn        = $spawn
      ToolRounds   = $toolCalls
      ModelTurns   = $modelTurns
    }
  }

$rows | Format-Table -AutoSize
$spawnCount = ($rows | Where-Object { $_.Spawn }).Count
if ($spawnCount -gt 0) {
  Write-Host "WARNING: $spawnCount log(s) contain assistant_tool_calls with sessions_spawn (review prompts if unintended)." -ForegroundColor Yellow
}
