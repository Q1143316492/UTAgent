# 将 Assets/UTAgent/ide-skills/* 复制到工作区 .cursor/skills/（Cursor）
# 用法（项目根）：
#   ./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1
#   ./Assets/UTAgent/Tools/bootstrap/Install-IdeSkills.ps1 -Force

param(
    [switch]$Force,
    [string]$WorkspaceRoot = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
$UtagentRoot = (Resolve-Path (Join-Path $ScriptDir "../..")).Path
$IdeSkills = Join-Path $UtagentRoot "ide-skills"

if (-not (Test-Path $IdeSkills)) {
    Write-Error "ide-skills not found: $IdeSkills"
    exit 1
}

# 工作区根 = 含 Assets/UTAgent 的项目根（UTAgent 的上两级：UTAgent → Assets → 项目根）
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $UtagentRoot "../..")).Path
}

$DestRoot = Join-Path $WorkspaceRoot ".cursor/skills"
New-Item -ItemType Directory -Path $DestRoot -Force | Out-Null

$copied = 0
Get-ChildItem $IdeSkills -Directory | ForEach-Object {
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
