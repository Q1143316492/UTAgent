# UI 拼装验收 — 两档入口（无全量）
#   ./run_benchmark.ps1                 # 日常 L1：E16/E17 门禁冒烟（无面板 golden）
#   ./run_benchmark.ps1 -L2Only         # 日常 L2：C02+C14+C15（chat→health→FAIL则打回AI→export）
#   ./run_benchmark.ps1 -L2             # 日常 L1 + L2
#   ./run_benchmark.ps1 -L2Only -Cases C15
#   ./run_benchmark.ps1 -L1Only -Cases E12
#   ./run_benchmark.ps1 -L2Only -RemediationMax 2
# 正式拼 UI = L2；面板 golden 已归档。退出码：全绿 0，任一失败非 0。
# 加测约定见 README.md / suite_map.json

param(
    [switch]$L2,
    [switch]$L1Only,
    [switch]$L2Only,
    [switch]$FullDev,
    [string]$Cases = "",
    [int]$RemediationMax = -1
)

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING = "utf-8"

if ($FullDev) {
    Write-Host "ERROR: -FullDev 已废除（无全量流程）。日常用默认入口；按需用 -Cases <ID>" -ForegroundColor Red
    Write-Host "  见 Assets/UTAgent/Tools/ui-benchmark/README.md" -ForegroundColor Yellow
    exit 2
}

if ($L1Only -and $L2Only) {
    Write-Host "ERROR: -L1Only 与 -L2Only 互斥" -ForegroundColor Red
    exit 2
}

# 纠偏次数：参数 > 环境变量 > 默认 1；硬上限 2
$remediationMax = 1
if ($RemediationMax -ge 0) {
    $remediationMax = $RemediationMax
} elseif (-not [string]::IsNullOrWhiteSpace($env:UTAGENT_HEALTH_REMEDIATION_MAX)) {
    $parsed = 0
    if ([int]::TryParse($env:UTAGENT_HEALTH_REMEDIATION_MAX, [ref]$parsed)) {
        $remediationMax = $parsed
    }
}
if ($remediationMax -lt 0) { $remediationMax = 0 }
if ($remediationMax -gt 2) { $remediationMax = 2 }

$runL1 = -not $L2Only
$runL2 = $L2Only -or ($L2 -and -not $L1Only)

$Root = (Resolve-Path "$PSScriptRoot/../../../..").Path
$Cli = Join-Path $Root "Assets/UTAgent/Tools/utagent-cli/utagent.py"
$BenchDir = $PSScriptRoot
$LogDir = Join-Path $Root "Assets/UTAgent/LOG"
$TmpDir = Join-Path $BenchDir ".tmp"
$ExportReq = Join-Path $TmpDir "_export_root.txt"
$MapPath = Join-Path $BenchDir "suite_map.json"

$map = Get-Content -Raw -Path $MapPath -Encoding UTF8 | ConvertFrom-Json
$DailyL1 = @($map.daily_l1)
$DailyL2 = @($map.daily_l2)

$caseList = @()
if (-not [string]::IsNullOrWhiteSpace($Cases)) {
    $caseList = $Cases -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}
$extraL1 = @($caseList | Where-Object { $_ -like "E*" })
$l2Cases = @($caseList | Where-Object { $_ -like "C*" })
if ($runL2 -and $l2Cases.Count -eq 0 -and [string]::IsNullOrWhiteSpace($Cases)) {
    $l2Cases = $DailyL2
}
# -Cases 只含 E* 时不跑 L2；只含 C* 且未 -L1Only 时仍可跑日常 L1
if ($runL2 -and $l2Cases.Count -eq 0 -and $extraL1.Count -gt 0) {
    $runL2 = $false
}

function Invoke-Utagent {
    param([string[]]$CmdArgs)
    & python $Cli @CmdArgs 2>&1 | Out-String
}

function Get-LatestLog {
    Get-ChildItem $LogDir -Filter "agent_*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}

function Get-JsonLine {
    param([string]$Text)
    $line = ($Text -split "`n" | Where-Object { $_ -match '^\{' } | Select-Object -Last 1)
    if ($line) { $line | ConvertFrom-Json } else { $null }
}

function Clear-UiRoots {
    param([string[]]$Names)
    $namesLit = ($Names | ForEach-Object { "'$_'" }) -join ", "
    $code = @"
import os, sys
_bench = os.path.join('Assets', 'UTAgent', 'Tools', 'ui-benchmark')
for p in (os.path.abspath(_bench),):
    if p not in sys.path: sys.path.insert(0, p)
import ui_panel_scope as scope
print(scope.destroy_named_roots($namesLit))
"@
    Invoke-Utagent -CmdArgs @("exec", "--code", $code) | Out-Null
}

function Invoke-UiHealth {
    param([string]$Roots)
    # CLI 环境变量进不了 Unity 内 Python；写请求文件（与 export 同模式）
    if (-not (Test-Path $TmpDir)) { New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null }
    $healthRootsFile = Join-Path $TmpDir "_health_roots.txt"
    [System.IO.File]::WriteAllText($healthRootsFile, $Roots + "`n", [System.Text.UTF8Encoding]::new($false))
    $out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_ui_scene_health.py"))
    return (Get-JsonLine -Text $out)
}

function Resolve-L1Script {
    param([string]$Id)
    $rel = $map.l1_scripts.$Id
    if (-not $rel) { return $null }
    return (Join-Path $BenchDir $rel)
}

$results = [System.Collections.Generic.List[pscustomobject]]::new()

function Add-Result {
    param([string]$Id, [bool]$Ok, [string]$Detail)
    $results.Add([pscustomobject]@{ ID = $Id; Ok = $Ok; Detail = $Detail })
    $mark = if ($Ok) { "PASS" } else { "FAIL" }
    Write-Host "  [$mark] $Id : $Detail"
}

function Export-L1Fixture {
    param([string]$Id)
    $root = $null
    if ($map.l1_export_roots -and $map.l1_export_roots.PSObject.Properties.Name -contains $Id) {
        $root = [string]$map.l1_export_roots.$Id
    }
    if ([string]::IsNullOrWhiteSpace($root)) { return }
    if (-not (Test-Path $TmpDir)) { New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null }
    [System.IO.File]::WriteAllText($ExportReq, $root + "`n", [System.Text.UTF8Encoding]::new($false))
    $eout = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "export_ui_panel_prefab.py"))
    $ej = Get-JsonLine -Text $eout
    if ($ej) {
        Add-Result -Id "$Id-export" -Ok ($ej.ok -eq $true) -Detail "path=$($ej.path) overwritten=$($ej.overwritten)"
    } else {
        Add-Result -Id "$Id-export" -Ok $false -Detail "parse fail"
    }
}

function Invoke-L1Case {
    param([string]$Id)
    $script = Resolve-L1Script -Id $Id
    if (-not $script -or -not (Test-Path $script)) {
        Add-Result -Id $Id -Ok $false -Detail "no script in suite_map"
        return
    }
    $out = Invoke-Utagent -CmdArgs @("exec", "--file", $script)
    $j = Get-JsonLine -Text $out
    $ok = $false
    $detail = "parse fail"
    switch ($Id) {
        "E01" {
            if ($j) { $ok = $j.has_button -eq $true -and $j.has_tmp_on_label -eq $true; $detail = "btn=$($j.btn_name)" }
        }
        "E02" {
            if ($j) { $ok = $j.has_vertical_layout_group -eq $true; $detail = "vlg=$($j.has_vertical_layout_group)" }
        }
        "E03" {
            if ($j) { $ok = $j.count -eq 1; $detail = "count=$($j.count)" }
        }
        "E04" {
            if ($j) { $ok = @($j.bad).Count -eq 0; $detail = "bad=$($j.bad -join ',')" }
        }
        "E09" {
            if ($j) { $ok = $j.ok -eq $true; $detail = "reminder=$($j.reminder_count)" }
        }
        "E10" {
            if ($j) { $ok = $j.ok -eq $true; $detail = "hist=$($j.history_len_after_load)" }
        }
        "E11" {
            if ($j) { $ok = $j.ok -eq $true; $detail = "kind=$($j.compaction_kind_present)" }
        }
        "E12" {
            if ($j) { $ok = $j.ok -eq $true; $detail = "violations=$(@($j.zero_width_violations).Count)" }
        }
        "E16" {
            if ($j) { $ok = $j.ok -eq $true; $detail = "non_ascii=$($j.non_ascii_name_fails)" }
        }
        "E17" {
            if ($j) { $ok = $j.ok -eq $true; $detail = "bad_fails=$($j.bad_btn_fails)" }
        }
        default {
            if ($j -and ($null -ne $j.ok)) { $ok = $j.ok -eq $true; $detail = "ok=$($j.ok)" }
            elseif ($j) { $ok = $true; $detail = "ran" }
        }
    }
    Add-Result -Id $Id -Ok $ok -Detail $detail
    # 若仍登记在 l1_export_roots（一般为空；面板 fixtures 改由 L2 export）
    if ($ok) { Export-L1Fixture -Id $Id }
}

Write-Host "[1/4] ping utagent..."
$ping = Invoke-Utagent -CmdArgs @("ping")
if ($ping -notmatch "engine_available: True") {
    Write-Host "  engine 不可用，尝试 init..."
    Invoke-Utagent -CmdArgs @("init") | Out-Null
    $ping = Invoke-Utagent -CmdArgs @("ping")
}
if ($ping -notmatch "engine_available: True") {
    Write-Host "ERROR: engine 仍不可用，请确认 Unity 已打开且 bridge 已应用" -ForegroundColor Red
    exit 1
}
Write-Host "  engine_available: True"
Write-Host "  suite tier: daily (no FullDev)"

# ---- L1 ----
if ($runL1) {
    $l1Ids = [System.Collections.Generic.List[string]]::new()
    # 若 -Cases 带了 E*：只跑这些 E；否则日常门禁
    if ($extraL1.Count -gt 0) {
        Write-Host "[2/4] L1 按需 ($($extraL1 -join ','))..."
        foreach ($id in $extraL1) { [void]$l1Ids.Add($id) }
    } else {
        Write-Host "[2/4] L1 日常 ($($DailyL1 -join ','))..."
        foreach ($id in $DailyL1) { [void]$l1Ids.Add($id) }
    }
    foreach ($id in $l1Ids) { Invoke-L1Case -Id $id }
} else {
    Write-Host "[2/4] L1 跳过（-L2Only）"
}

# ---- L2 ----
if ($runL2) {
    Write-Host "[3/4] L2 Cases=$($l2Cases -join ',')..."
    $prompts = @{
        C01 = "Canvas 下创建一个 TMP 按钮 BtnStart，文案 Start"
        C02 = "创建设置面板 WndSettings，标题设置，两个 row（音乐/音效），底部保存/取消按钮"
        C03 = "WndDemo 里的 BtnSubmit 点不了，帮我排查"
        C04 = "创建一个 Cube 到场景"
        C06 = "在 Canvas 下 AddComponent Image 加个 BtnTest 按钮"
        C07 = "把 WndSettings 里的保存按钮颜色改成红色，其他不要动"
        C08 = "一次性写一个超长脚本检查 WndSettings 所有组件的所有属性"
        C09 = "用 GetComponents(CS.UnityEngine.Component) 列出 BtnSave 的所有组件"
        C10 = "创建设置面板 WndSettings，标题设置，两个 row（音乐/音效），底部保存/取消按钮，若守卫触发请拆步"
        C11 = "禁止 loadSkill。必须实际调用一次 execPython（不要凭记忆回答），code 一字不差：`nfrom unity_bind import CS`ng=CS.UnityEngine.GameObject('PanelLayoutC11')`nc=CS.UnityEngine.GameObject.Find('Canvas')`ng.transform.SetParent(c.transform, False)`ng.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)`nprint('c11')`n若被 before-exec 拦截请停止并说明守卫名。"
        C12 = "禁止 loadSkill。必须实际调用一次 execPython（不要凭记忆回答），code 一字不差：`nprint('A' * 12000)`n然后用一句话说明已执行。不要做别的。"
        C13 = "禁止 loadSkill、禁止创建任何对象。必须实际连续至少 3 次 execPython（不要凭记忆），每次 code 仅：`nimport unity`nprint(unity.find_objects('BtnDoesNotExistC13', echo=False))`n找不存在的 BtnDoesNotExistC13。若 after-tool 注入无进展提醒可停止并说明。"
        C14 = "创建登录面板 WndLogin，标题登录，账号与密码两个输入行，底部登录/取消按钮。节点名用英文前缀 PascalCase，中文只写在文案里。"
        C15 = "创建角色面板 WndCharacter（不是新建角色流程）：标题角色；头像区+姓名/等级/职业；属性行生命/魔力/攻击/防御；三个装备槽按钮；底部关闭。节点名英文前缀 PascalCase，中文只写在文案里。"
    }
    $uiHealthRoots = @{
        C01 = "BtnStart"; C02 = "WndSettings"; C06 = "BtnTest"; C07 = "WndSettings"
        C10 = "WndSettings"; C14 = "WndLogin"; C15 = "WndCharacter"
    }
    $uiExportRoots = @{ C02 = "WndSettings"; C14 = "WndLogin"; C15 = "WndCharacter" }
    $uiClearRoots = @{
        C01 = @("BtnStart"); C02 = @("WndSettings", "WndLogin", "WndCharacter"); C06 = @("BtnTest")
        C07 = @("WndSettings"); C10 = @("WndSettings", "WndLogin", "WndCharacter")
        C14 = @("WndLogin", "WndSettings", "WndCharacter"); C15 = @("WndCharacter", "WndLogin", "WndSettings")
    }
    foreach ($c in $l2Cases) {
        if (-not $prompts.ContainsKey($c)) { Add-Result -Id $c -Ok $false -Detail "未知用例"; continue }
        Invoke-Utagent -CmdArgs @("exec", "--code", "import agent; agent.clear_history()") | Out-Null
        $clearNames = $uiClearRoots[$c]
        if ($null -ne $clearNames) { Clear-UiRoots -Names $clearNames }
        Write-Host "  chat $c ..."
        Invoke-Utagent -CmdArgs @("chat", $prompts[$c], "--compact") | Out-Null
        $log = Get-LatestLog
        $aout = & python (Join-Path $BenchDir "parse_agent_log.py") $log --assert $c 2>&1 | Out-String
        try {
            $full = $aout | ConvertFrom-Json
            $a = $full._assert
            Add-Result -Id $c -Ok $a.ok -Detail $a.detail
        } catch {
            $ok = $aout -match '"ok":\s*true'
            Add-Result -Id $c -Ok $ok -Detail "assert parse fallback"
        }

        $healthRoots = $uiHealthRoots[$c]
        if ($null -ne $healthRoots) {
            $hj = Invoke-UiHealth -Roots $healthRoots
            $hOk = $false
            if ($hj) { $hOk = $hj.ok -eq $true }

            $remediationStatus = "skipped"
            $remediationAttempts = 0
            while ((-not $hOk) -and ($remediationAttempts -lt $remediationMax)) {
                if (-not (Test-Path $TmpDir)) { New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null }
                $healthJsonPath = Join-Path $TmpDir "_health_$c.json"
                if ($hj) {
                    $jsonText = ($hj | ConvertTo-Json -Depth 12 -Compress)
                    [System.IO.File]::WriteAllText($healthJsonPath, $jsonText + "`n", [System.Text.UTF8Encoding]::new($false))
                } else {
                    [System.IO.File]::WriteAllText($healthJsonPath, '{"ok":false,"error":"health parse fail"}' + "`n", [System.Text.UTF8Encoding]::new($false))
                }
                $promptOut = & python (Join-Path $BenchDir "format_health_remediation_prompt.py") $healthJsonPath $healthRoots 2>&1 | Out-String
                if ([string]::IsNullOrWhiteSpace($promptOut) -or $LASTEXITCODE -ne 0) {
                    $remediationStatus = "exhausted"
                    break
                }
                $remediationAttempts++
                Write-Host "  remediation $c attempt=$remediationAttempts/$remediationMax ..."
                # 不清 history：在同会话打回 AI
                Invoke-Utagent -CmdArgs @("chat", $promptOut.TrimEnd(), "--compact") | Out-Null
                $hj = Invoke-UiHealth -Roots $healthRoots
                $hOk = $false
                if ($hj) { $hOk = $hj.ok -eq $true }
                if ($hOk) {
                    $remediationStatus = "fixed"
                } else {
                    $remediationStatus = "exhausted"
                }
            }

            $remOk = $remediationStatus -ne "exhausted"
            Add-Result -Id "$c-remediation" -Ok $remOk -Detail "status=$remediationStatus attempts=$remediationAttempts max=$remediationMax"

            $integ = ""
            if ($hj -and ($null -ne $hj.integrity_ok)) {
                $integ = " integ=$($hj.integrity_ok)"
            }
            if ($hj) {
                Add-Result -Id "$c-health" -Ok $hOk -Detail "outside=$($hj.outside_canvas_count) zero=$($hj.zero_size_count) miss_pref=$($hj.missing_preferred_count)$integ"
            } else {
                Add-Result -Id "$c-health" -Ok $false -Detail "parse fail"
            }

            $exportRoot = $uiExportRoots[$c]
            if ($hOk -and ($null -ne $exportRoot)) {
                if (-not (Test-Path $TmpDir)) { New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null }
                [System.IO.File]::WriteAllText($ExportReq, $exportRoot + "`n", [System.Text.UTF8Encoding]::new($false))
                $eout = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "export_ui_panel_prefab.py"))
                $ej = Get-JsonLine -Text $eout
                if ($ej) {
                    Add-Result -Id "$c-export" -Ok ($ej.ok -eq $true) -Detail "path=$($ej.path)"
                } else {
                    Add-Result -Id "$c-export" -Ok $false -Detail "parse fail"
                }
            }
        }
    }
} else {
    Write-Host "[3/4] L2 跳过（用 -L2 或 -L2Only；按需 -Cases C*）"
}

Write-Host "[4/4] 解析最近 log..."
$log = Get-LatestLog
if ($log) {
    $pout = & python (Join-Path $BenchDir "parse_agent_log.py") $log 2>&1 | Out-String
    try {
        $p = $pout | ConvertFrom-Json
        Write-Host "  log: $log"
        Write-Host "  turns=$($p.turns.Count) loadSkill=$($p.loadSkill_calls.Count) exec_steps=$($p.exec_steps)"
    } catch { Write-Host "  log 解析失败: $_" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "===== 摘要 ====="
$pass = @($results | Where-Object { $_.Ok -eq $true }).Count
$fail = @($results | Where-Object { $_.Ok -eq $false }).Count
$total = $pass + $fail
$rate = if ($total -gt 0) { [math]::Round(100.0 * $pass / $total, 1) } else { 0 }
$results | Format-Table -AutoSize
Write-Host ("PASS={0} FAIL={1} TOTAL={2} RATE={3}% tier=daily" -f $pass, $fail, $total, $rate)
if ($fail -gt 0) {
    $failedIds = @($results | Where-Object { $_.Ok -eq $false } | ForEach-Object { $_.ID }) -join ", "
    Write-Host "FAILED IDs: $failedIds" -ForegroundColor Red
    Write-Host "BENCHMARK FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "BENCHMARK PASSED" -ForegroundColor Green
exit 0
