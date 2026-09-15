# 网页版「忽略规则」验收：CDP 跑规则引擎语义 + 真实文件输入端到端（DOM.setFileInputFiles）。
# 用法: verify-ignore-web.ps1 -Exe <exe> [-Port 9228]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Port = 9228,
    [string]$Work = "$env:TEMP\fd-ignore-web"
)
$ErrorActionPreference = 'Stop'

# ── 准备真实文件（A/B 各四个：两个该被忽略、两个该保留）──────────
foreach ($side in @('A', 'B')) {
    $dir = Join-Path $Work $side
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path (Join-Path $dir 'keep.log') -Value "keep $side" -Encoding UTF8
    Set-Content -Path (Join-Path $dir 'app.log')  -Value "log $side"  -Encoding UTF8
    Set-Content -Path (Join-Path $dir 'Thumbs.db') -Value "junk $side" -Encoding UTF8
    Set-Content -Path (Join-Path $dir 'main.txt') -Value "main $side" -Encoding UTF8
}

Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

$env:FOLDERDIFF_DEBUG_PORT = "$Port"
Start-Process $Exe | Out-Null
Start-Sleep -Seconds 14

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 15
$page = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
if (-not $page) { throw "no page target" }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait()
$script:msgId = 0

function Cdp([string]$method, $params) {
    $script:msgId++
    $id = $script:msgId
    $payload = @{ id = $id; method = $method }
    if ($params) { $payload.params = $params }
    $json = $payload | ConvertTo-Json -Depth 10 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $ws.SendAsync([System.ArraySegment[byte]]::new($bytes, 0, $bytes.Length),
                  [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
    for ($i = 0; $i -lt 60; $i++) {
        # 一条 CDP 消息可能跨多个 WebSocket 帧，先拼完整再解析
        $sb = New-Object System.Text.StringBuilder
        do {
            $buf = New-Object byte[] 65536
            $res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf, 0, $buf.Length), $ct).Result
            [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
        } while (-not $res.EndOfMessage)
        $obj = $null
        try { $obj = $sb.ToString() | ConvertFrom-Json } catch { continue }   # 事件帧（可能很大），跳过
        if ($obj -and $obj.id -eq $id) { return $obj }
    }
    throw "no response for $method"
}

function Eval([string]$expr) {
    $r = Cdp 'Runtime.evaluate' @{ expression = $expr; returnByValue = $true }
    if ($r.result.exceptionDetails) {
        Write-Output ("  !! JS 异常: " + ($r.result.exceptionDetails | ConvertTo-Json -Depth 8 -Compress))
    }
    return $r.result.result.value
}

# 把设置对象编成"能安全塞进 JS 单引号字面量"的 JSON：
#   换行先被 ConvertTo-Json 写成 \n，再把反斜杠加倍，免得 JS 字面量把它解释成真换行
function RulesJs($obj) {
    $json = ($obj | ConvertTo-Json -Compress).Replace('\', '\\')
    return "window.__fdIgnore.set('" + $json + "')"
}

$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}

# ── 1) 初始状态 ─────────────────────────────────────────────
$empty = @{ vcs = $false; temp = $false; logs = $false; custom = "" }
Eval (RulesJs $empty) | Out-Null
Write-Output ("注入条 = " + (Eval "document.getElementById('__fdBar') ? 'ok' : 'no-bar'") +
              "  忽略按钮 = " + (Eval "JSON.stringify((document.getElementById('__fdIgPill')||{}).textContent)") +
              "  面板 = " + (Eval "document.getElementById('__fdIgnorePanel') ? '已建' : '未建'"))
Assert "忽略胶囊一启动就有文字" $true ((Eval "document.getElementById('__fdIgPill').textContent.length > 0") -eq $true)
Write-Output ("初始 state = " + (Eval "window.__fdIgnore.state()"))
Assert "初始无规则" "0" (Eval "JSON.stringify(JSON.parse(window.__fdIgnore.state()).count)")

# ── 2) 设置规则（与原生版单元测试同一批）────────────────────
$obj = @{
    vcs = $true; temp = $true; logs = $true
    custom = "node_modules/`nbuild/**`n!keep.log`n!important/app.log`n**/temp"
}
$count = Eval (RulesJs $obj)
Write-Output ("设置后规则条数 = " + $count + "（与原生版一致应为 24）")
Assert "规则条数" 24 $count
Write-Output ("忽略按钮（启用后）= " + (Eval "JSON.stringify(document.getElementById('__fdIgPill').textContent)"))

# ── 3) 规则语义逐条比对 ─────────────────────────────────────
$cases = @(
    @('.git/config', $false, $true,  'vcs 目录内文件'),
    @('.git', $true, $true,          'vcs 目录本身'),
    @('src/deep/.svn/entries', $false, $true, '嵌套 vcs'),
    @('.GIT/config', $false, $true,  '大小写不敏感'),
    @('src/.gitignore', $false, $false, '不误伤 .gitignore'),
    @('a/b/app.log', $false, $true,  '日志'),
    @('keep.log', $false, $false,    '! 例外'),
    @('sub/deep/keep.log', $false, $false, '! 例外任意层'),
    @('important/app.log', $false, $false, '! 锚定例外'),
    @('other/app.log', $false, $true, '锚定例外不影响别处'),
    @('Thumbs.db', $false, $true,    '系统文件'),
    @('~$report.docx', $false, $true, 'Office 临时文件'),
    @('node_modules/lodash/index.js', $false, $true, '自定义目录根'),
    @('src/node_modules/x.js', $false, $true, '自定义目录任意层'),
    @('build/out/app.exe', $false, $true, '锚定 + **'),
    @('rebuild/out.txt', $false, $false, '锚定不误伤 rebuild'),
    @('a/b/temp', $true, $true,      '**/temp 任意层'),
    @('a/b/tempfile', $false, $false, '**/temp 不误伤 tempfile'),
    @('readme.md', $false, $false,   '普通文件'),
    @('a\b\app.log', $false, $true,  '反斜杠路径')
)
foreach ($c in $cases) {
    $dir = if ($c[1]) { 'true' } else { 'false' }
    $got = Eval ("window.__fdIgnore.isIgnored('" + $c[0] + "'," + $dir + ")")
    Assert $c[3] $c[2] $got
}
Write-Output ("规则语义累计：通过 $pass 项，失败 $fail 项")

# ── 4) 列表过滤纯函数 ───────────────────────────────────────
$names = '["keep.log","app.log","Thumbs.db","src/main.txt",".git/config"]'
Write-Output ("filterNames = " + (Eval ("window.__fdIgnore.filterNames('" + $names + "')")))

# ── 5) 真实文件输入端到端 ───────────────────────────────────
$doc = Cdp 'DOM.getDocument' @{}
$rootId = $doc.result.root.nodeId
foreach ($side in @('A', 'B')) {
    $q = Cdp 'DOM.querySelector' @{ nodeId = $rootId; selector = "#inputFolder$side" }
    # webkitdirectory 输入框要传"目录"，Blink 自己枚举并把 webkitRelativePath 填好
    $files = @((Join-Path $Work $side))
    $sf = Cdp 'DOM.setFileInputFiles' @{ files = $files; nodeId = $q.result.nodeId }
    Eval ("document.getElementById('inputFolder$side').dispatchEvent(new Event('change'))") | Out-Null
    Start-Sleep -Milliseconds 900
    $info = Eval ("document.getElementById('folderInfo$side').textContent")
    Write-Output ("文件夹 $side 信息栏 = '" + $info + "'   期望 2 个文件（app.log / Thumbs.db 被忽略）")
    Assert "$side 过滤后文件数" "2" ($info -split ' ')[0]
}
Eval "document.getElementById('__fdIgPill').click()" | Out-Null; Start-Sleep -Milliseconds 500
Write-Output ("面板 = " + (Eval "document.getElementById('__fdIgnorePanel') ? '已建' : '未建'") + "  信息 = " + (Eval "JSON.stringify((document.getElementById('__fdIgInfo')||{}).textContent)"))

# ── 5b) 两个面板必须互斥（同时只开一个）──────────────────────
$visJs = "JSON.stringify({ig:((document.getElementById('__fdIgnorePanel')||{}).style||{}).display||'none'," +
         "hi:((document.getElementById('__fdHistPanel')||{}).style||{}).display||'none'})"
$v0 = Eval $visJs | ConvertFrom-Json
Write-Output ("面板状态(开忽略) = 忽略:" + $v0.ig + "  历史:" + $v0.hi)
Assert "开忽略面板时历史面板是关的" "block|none" ($v0.ig + "|" + $v0.hi)

Eval "document.getElementById('__fdHistPill').click()" | Out-Null; Start-Sleep -Milliseconds 800
$v1 = Eval $visJs | ConvertFrom-Json
Write-Output ("面板状态(开历史) = 忽略:" + $v1.ig + "  历史:" + $v1.hi)
Assert "开历史面板后忽略面板自动关闭" "none|block" ($v1.ig + "|" + $v1.hi)

Eval "document.getElementById('__fdIgPill').click()" | Out-Null; Start-Sleep -Milliseconds 500
$v2 = Eval $visJs | ConvertFrom-Json
Write-Output ("面板状态(再开忽略) = 忽略:" + $v2.ig + "  历史:" + $v2.hi)
Assert "再开忽略面板后历史面板自动关闭" "block|none" ($v2.ig + "|" + $v2.hi)

# ── 6) 用过滤后的列表跑一次真实对比 ─────────────────────────
Eval "document.getElementById('btnCompare').click()" | Out-Null
Start-Sleep -Seconds 4
$counts = Eval "JSON.stringify({m:document.getElementById('countModified').textContent,b:document.getElementById('countOnlyInB').textContent,a:document.getElementById('countOnlyInA').textContent,s:document.getElementById('countSame').textContent,active:document.getElementById('resultsSummary').classList.contains('active')})"
Write-Output ("对比结果 = " + $counts + "   期望 m=2，其余 0")

Write-Output ("最终 state = " + (Eval "window.__fdIgnore.state()"))
Write-Output ("面板信息(再读) = " + (Eval "JSON.stringify((document.getElementById('__fdIgInfo')||{}).textContent)"))
Eval (RulesJs $empty) | Out-Null   # 收尾：把测试写入的规则清掉，别影响用户
Write-Output "已清理测试规则（localStorage 复位为不启用）"
$ws.Dispose()
Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }
