# UI 拼装验收基准一键脚本
# 用法：
#   ./run_benchmark.ps1              # 仅 L1（exec --file），省 LLM API
#   ./run_benchmark.ps1 -L2           # L1 + L2（chat 全用例）
#   ./run_benchmark.ps1 -L2 -Cases C02,C09   # L1 + 指定 L2 用例
# 退出码：全绿 0，任一失败非 0。

param(
    [switch]$L2,
    [switch]$L1Only,
    [string]$Cases = "C01,C02,C03,C04,C06,C07,C08,C09,C10,C11,C12,C13"
)

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING = "utf-8"

$Root = (Resolve-Path "$PSScriptRoot/../../../..").Path
$Cli = Join-Path $Root "Assets/UTAgent/Tools/utagent-cli/utagent.py"
$BenchDir = Join-Path $Root "Assets/UTAgent/Tools/ui-benchmark"
$LogDir = Join-Path $Root "Assets/UTAgent/LOG"

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

$results = [System.Collections.Generic.List[pscustomobject]]::new()

function Add-Result {
    param([string]$Id, [bool]$Ok, [string]$Detail)
    $results.Add([pscustomobject]@{ ID = $Id; Ok = $Ok; Detail = $Detail })
    $mark = if ($Ok) { "PASS" } else { "FAIL" }
    Write-Host "  [$mark] $Id : $Detail"
}

# ---- Step 1: ping / init ----
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

# ---- Step 2: L1 结构用例 ----
Write-Host "[2/4] L1 结构用例 (utagent exec --file)..."

# E01 TMP 按钮
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "golden_path_tmp_button.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    $ok = $j.has_button -eq $true -and $j.has_tmp_on_label -eq $true
    Add-Result -Id "E01" -Ok $ok -Detail "btn=$($j.btn_name) has_button=$($j.has_button) has_tmp=$($j.has_tmp_on_label)"
} else { Add-Result -Id "E01" -Ok $false -Detail "parse fail" }

# E02 面板
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "golden_path_layout_panel.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    $ok = $j.has_vertical_layout_group -eq $true -and $j.save_scene_ok -eq $true
    Add-Result -Id "E02" -Ok $ok -Detail "vlg=$($j.has_vertical_layout_group) save=$($j.save_scene_ok)"
} else { Add-Result -Id "E02" -Ok $false -Detail "parse fail" }

# E03 幂等：再跑一次 layout_panel，查 WndDemo count
Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "golden_path_layout_panel.py")) | Out-Null
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_count_wnddemo.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    Add-Result -Id "E03" -Ok ($j.count -eq 1) -Detail "WndDemo count=$($j.count)"
} else { Add-Result -Id "E03" -Ok $false -Detail "parse fail" }

# E04 命名
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_naming_wnddemo.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    $badCount = @($j.bad).Count
    Add-Result -Id "E04" -Ok ($badCount -eq 0) -Detail "bad names=$($j.bad -join ',')"
} else { Add-Result -Id "E04" -Ok $false -Detail "parse fail" }

# E08 settings form
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "golden_path_settings_form.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    $ok = $j.row_count -ge 2 -and $j.button_count -eq 2 -and $j.has_vlg -eq $true
    Add-Result -Id "E08" -Ok $ok -Detail "row=$($j.row_count) btn=$($j.button_count) vlg=$($j.has_vlg)"
} else { Add-Result -Id "E08" -Ok $false -Detail "parse fail" }

# E08 幂等
Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "golden_path_settings_form.py")) | Out-Null
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_count_wndsettings.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    Add-Result -Id "E08-idem" -Ok ($j.count -eq 1) -Detail "WndSettings count=$($j.count)"
} else { Add-Result -Id "E08-idem" -Ok $false -Detail "parse fail" }

# E05/E06/E07：无独立脚本，标记 skip
Add-Result -Id "E05" -Ok $true -Detail "SKIP (describe_go 无独立脚本，见 align change verification)"
Add-Result -Id "E06" -Ok $true -Detail "SKIP (add_to_layout 无独立脚本，见 editor-ui-layout-primitives verification)"
Add-Result -Id "E07" -Ok $true -Detail "SKIP (add_free_child 无独立脚本，见 editor-ui-layout-primitives verification)"

# E09 convert_to_llm reminder 过滤
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_convert_to_llm_e09.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    Add-Result -Id "E09" -Ok ($j.ok -eq $true) -Detail "reminder_count=$($j.reminder_count) last=$($j.last_reminder)"
} else { Add-Result -Id "E09" -Ok $false -Detail "parse fail" }

# E10 loadSkill/emit_progress 不进 history
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_history_no_progress_e10.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    Add-Result -Id "E10" -Ok ($j.ok -eq $true) -Detail "history_after_load=$($j.history_len_after_load) status_in_history=$($j.status_in_history)"
} else { Add-Result -Id "E10" -Ok $false -Detail "parse fail" }

# E11 LLM compaction kind + needs_compaction 信号
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_compaction_e11.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    Add-Result -Id "E11" -Ok ($j.ok -eq $true) -Detail "kind=$($j.compaction_kind_present) llm=$($j.compaction_in_llm) needs=$($j.needs_compaction)"
} else { Add-Result -Id "E11" -Ok $false -Detail "parse fail" }

# E12 Layout 零宽（面板 + 输入框）
$out = Invoke-Utagent -CmdArgs @("exec", "--file", (Join-Path $BenchDir "assert_layout_zero_width_e12.py"))
$j = Get-JsonLine -Text $out
if ($j) {
    $v = $j.zero_width_violations
    $vc = if ($null -eq $v) { 0 } else { @($v).Count }
    Add-Result -Id "E12" -Ok ($j.ok -eq $true) -Detail "violations=$vc root=$($j.root_name)"
} else { Add-Result -Id "E12" -Ok $false -Detail "parse fail" }

# ---- Step 3: L2 行为用例（可选） ----
if ($L2 -and -not $L1Only) {
    Write-Host "[3/4] L2 行为用例 (utagent chat)..."
    $caseList = $Cases -split "," | ForEach-Object { $_.Trim() }
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
        C11 = "禁止 loadSkill。必须实际调用一次 execPython（不要凭记忆回答），code 一字不差：`nfrom unity_bind import CS`ng=CS.UnityEngine.GameObject('GrpLayoutC11')`nc=CS.UnityEngine.GameObject.Find('Canvas')`ng.transform.SetParent(c.transform, False)`ng.AddComponent(CS.UnityEngine.UI.VerticalLayoutGroup)`nprint('c11')`n若被 before-exec 拦截请停止并说明守卫名。"
        C12 = "禁止 loadSkill。必须实际调用一次 execPython（不要凭记忆回答），code 一字不差：`nprint('A' * 12000)`n然后用一句话说明已执行。不要做别的。"
        C13 = "禁止 loadSkill、禁止创建任何对象。必须实际连续至少 3 次 execPython（不要凭记忆），每次 code 仅：`nimport unity`nprint(unity.find_objects('BtnDoesNotExistC13', echo=False))`n找不存在的 BtnDoesNotExistC13。若 after-tool 注入无进展提醒可停止并说明。"
    }
    foreach ($c in $caseList) {
        if (-not $prompts.ContainsKey($c)) { Add-Result -Id $c -Ok $false -Detail "未知用例"; continue }
        # 清空 history，避免跨用例记忆污染（C11 等依赖真实 before-exec）
        Invoke-Utagent -CmdArgs @("exec", "--code", "import agent; agent.clear_history()") | Out-Null
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
    }
} else {
    Write-Host "[3/4] L2 跳过（用 -L2 启用 chat 用例）"
}

# ---- Step 4: 解析最近 log + 摘要 ----
Write-Host "[4/4] 解析最近 log..."
$log = Get-LatestLog
if ($log) {
    $pout = & python (Join-Path $BenchDir "parse_agent_log.py") $log 2>&1 | Out-String
    try {
        $p = $pout | ConvertFrom-Json
        Write-Host "  log: $log"
        Write-Host "  turns=$($p.turns.Count) loadSkill=$($p.loadSkill_calls.Count) exec_steps=$($p.exec_steps) before_exec=$($p.before_exec_decisions.Count) warnings=$($p.parse_warnings.Count)"
        if ($p.parse_warnings.Count -gt 0) {
            Write-Host "  parse_warnings:" -ForegroundColor Yellow
            $p.parse_warnings | ForEach-Object { Write-Host "    $_" }
        }
    } catch { Write-Host "  log 解析失败: $_" -ForegroundColor Yellow }
}

# ---- 摘要 ----
Write-Host ""
Write-Host "===== 摘要 ====="
$pass = @($results | Where-Object { $_.Ok -eq $true }).Count
$fail = @($results | Where-Object { $_.Ok -eq $false }).Count
$results | Format-Table -AutoSize
Write-Host "PASS=$pass FAIL=$fail"
if ($fail -gt 0) {
    Write-Host "BENCHMARK FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "BENCHMARK PASSED" -ForegroundColor Green
exit 0
