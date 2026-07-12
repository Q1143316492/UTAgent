# UTAgent Editor Bridge CLI（PowerShell 入口）
param(
    [int]$Port = 0,
    [switch]$Json,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PyScript = Join-Path $ScriptDir "utagent.py"

if (-not (Test-Path $PyScript)) {
    Write-Error "找不到 utagent.py: $PyScript"
    exit 1
}

$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) {
    $py = Get-Command python3 -ErrorAction SilentlyContinue
}
if (-not $py) {
    Write-Error "未找到 python / python3，请安装 Python 3 并加入 PATH"
    exit 1
}

$cmd = @($PyScript.FullName)
if ($Port -gt 0) {
    $cmd += "--port"
    $cmd += $Port
}
if ($Json) {
    $cmd += "--json"
}
if ($Args) {
    $cmd += $Args
}

& $py.Source @cmd
exit $LASTEXITCODE
