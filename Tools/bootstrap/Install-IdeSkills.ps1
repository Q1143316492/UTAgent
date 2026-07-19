# 将 Assets/UTAgent/agent-skills/* 复制到工作区技能目录
# 默认目标：.cursor/skills/（Cursor）；其它工具请按各自约定改 -DestRel
# 用法（项目根）：
#   ./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1
#   ./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1 -Force
#   ./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1 -DestRel ".claude/skills"

param(
    [switch]$Force,
    [string]$WorkspaceRoot = "",
    [string]$DestRel = ".cursor/skills"
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
$UtagentRoot = (Resolve-Path (Join-Path $ScriptDir "../..")).Path
$AgentSkills = Join-Path $UtagentRoot "agent-skills"
if (-not (Test-Path $AgentSkills)) {
    $AgentSkills = Join-Path $UtagentRoot "ide-skills"
}
if (-not (Test-Path $AgentSkills)) {
    Write-Error "agent-skills (or legacy ide-skills) not found under: $UtagentRoot"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $UtagentRoot "../..")).Path
}

$DestRoot = Join-Path $WorkspaceRoot $DestRel
New-Item -ItemType Directory -Path $DestRoot -Force | Out-Null

$copied = 0
Get-ChildItem $AgentSkills -Directory | ForEach-Object {
    $name = $_.Name
    $dest = Join-Path $DestRoot $name
    if ((Test-Path $dest) -and -not $Force) {
        Write-Host "Skip existing: $dest (use -Force to overwrite)"
        return
    }
    if (Test-Path $dest) {
        Remove-Item $dest -Recurse -Force
    }
    Copy-Item $_.FullName $dest -Recurse -Force
    Write-Host "Copied: $name → $dest"
    $copied++
}

Write-Host "Done. Copied $copied skill(s) into $DestRoot"
exit 0
