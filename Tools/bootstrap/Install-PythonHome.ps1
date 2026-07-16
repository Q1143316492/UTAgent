# 下载官方 Windows embeddable CPython 到 Assets/UTAgent/PythonHome/
# 用法（项目根）：
#   ./Assets/UTAgent/Tools/bootstrap/Install-PythonHome.ps1
#   ./Assets/UTAgent/Tools/bootstrap/Install-PythonHome.ps1 -Force

param(
    [switch]$Force,
    [string]$Version = "3.12.10"
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
$UtagentRoot = (Resolve-Path (Join-Path $ScriptDir "../..")).Path
$PythonHome = Join-Path $UtagentRoot "PythonHome"
$DllName = "python312.dll"
$ZipName = "python-$Version-embed-amd64.zip"
$Url = "https://www.python.org/ftp/python/$Version/$ZipName"

$dllPath = Join-Path $PythonHome $DllName
if ((Test-Path $dllPath) -and -not $Force) {
    Write-Host "PythonHome already has $DllName — skip. Use -Force to reinstall."
    Write-Host "  $PythonHome"
    exit 0
}

$tmp = Join-Path $env:TEMP $ZipName
Write-Host "Downloading $Url ..."
Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
Write-Host ("Downloaded {0:N1} MB" -f ((Get-Item $tmp).Length / 1MB))

if (Test-Path $PythonHome) {
    if ($Force) {
        Write-Host "Removing existing PythonHome ..."
        Remove-Item $PythonHome -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $PythonHome -Force | Out-Null
Expand-Archive -Path $tmp -DestinationPath $PythonHome -Force

# embeddable: enable site for PYTHONPATH injection
$pth = Join-Path $PythonHome "python312._pth"
if (Test-Path $pth) {
    @"
python312.zip
.

# Uncomment to run site.main() automatically
import site
"@ | Set-Content -Path $pth -Encoding ASCII
}

if (-not (Test-Path $dllPath)) {
    Write-Error "Install failed: $DllName not found under $PythonHome"
    exit 1
}

$total = (Get-ChildItem $PythonHome -Recurse | Measure-Object Length -Sum).Sum
Write-Host ("OK: PythonHome ready ({0:N1} MB)" -f ($total / 1MB))
Write-Host "  $PythonHome"
Write-Host "Next: Window/UT Agent/Settings → ① Python → 初始化 Python 环境"
exit 0
