# 网页版「灵动岛动效」验收：4 个胶囊的悬浮/按下/高光/文字翻牌 + 两个面板从胶囊里长出来。
# 用法: verify-anim-web.ps1 -Exe <exe> [-Port 9234] [-Shots <目录>]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Port = 9234,
    [string]$Shots = "$PSScriptRoot\动效截图"
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Shots | Out-Null

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
    for ($i = 0; $i -lt 400; $i++) {
        $sb = New-Object System.Text.StringBuilder
        do {
            $buf = New-Object byte[] 65536
            $res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf, 0, $buf.Length), $ct).Result
            [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
        } while (-not $res.EndOfMessage)
        $obj = $null
        try { $obj = $sb.ToString() | ConvertFrom-Json } catch { continue }
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

function Shot([string]$name, [int]$delayMs) {
    if ($delayMs -gt 0) { Start-Sleep -Milliseconds $delayMs }
    $r = Cdp 'Page.captureScreenshot' @{ format = 'png' }
    $b64 = $r.result.data
    if (-not $b64) { Write-Output "  (截图 $name 失败)"; return }
    $path = Join-Path $Shots ($name + '.png')
    [System.IO.File]::WriteAllBytes($path, [System.Convert]::FromBase64String($b64))
    Write-Output ("  截图 -> " + $path)
}

function Hover([string]$id) {
    $rect = Eval ("(function(){var r=document.getElementById('" + $id + "').getBoundingClientRect();" +
                  "return JSON.stringify({x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)})})()") | ConvertFrom-Json
    Cdp 'Input.dispatchMouseEvent' @{ type = 'mouseMoved'; x = $rect.x; y = $rect.y; buttons = 0 } | Out-Null
}
function Unhover() {
    Cdp 'Input.dispatchMouseEvent' @{ type = 'mouseMoved'; x = 5; y = 5; buttons = 0 } | Out-Null
}

$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; Write-Output ("  OK   " + $label); return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}

Cdp 'Page.enable' @{} | Out-Null

Write-Output "=== 1) 动效样式已注入 ==="
$hasCss = Eval "document.getElementById('__fdThemeCss').textContent.indexOf('fdIslandIn') > 0"
Assert "关键帧 fdIslandIn 存在" $true $hasCss
Assert "关键帧 fdRoll 存在" $true (Eval "document.getElementById('__fdThemeCss').textContent.indexOf('fdRoll') > 0")
Assert "关键帧 fdSweep 存在" $true (Eval "document.getElementById('__fdThemeCss').textContent.indexOf('fdSweep') > 0")
Assert "关键帧 fdPop 存在" $true (Eval "document.getElementById('__fdThemeCss').textContent.indexOf('fdPop') > 0")

Write-Output "=== 2) 4 个胶囊都拿到了动画基类 ==="
foreach ($id in @('__fdIgPill', '__fdHistPill', '__fdTheme', '__fdTopMost')) {
    $cls = Eval ("document.getElementById('" + $id + "').className")
    Assert ("$id 有 fd-pill 类") $true ($cls -like '*fd-pill*')
    $tr = Eval ("getComputedStyle(document.getElementById('" + $id + "')).transitionProperty")
    Assert ("$id 有 transform 过渡") $true ($tr -like '*transform*')
    Assert ("$id 有文字容器 .fd-wrap") $true ((Eval ("document.querySelectorAll('#" + $id + " .fd-wrap').length")) -eq 1)
}
Write-Output ("胶囊文案 = " + (Eval "JSON.stringify([].map.call(document.querySelectorAll('#__fdBar button'),function(b){return b.textContent}))"))

Write-Output "=== 3) 悬浮微抬（真实鼠标 hover）==="
Unhover; Start-Sleep -Milliseconds 400
$t0 = Eval "getComputedStyle(document.getElementById('__fdIgPill')).transform"
Hover '__fdIgPill'; Start-Sleep -Milliseconds 500
$t1 = Eval "getComputedStyle(document.getElementById('__fdIgPill')).transform"
Write-Output ("  未悬浮 transform = " + $t0 + "   悬浮 transform = " + $t1)
Assert "hover 后不再是 none" $true ($t1 -ne 'none' -and $t1 -ne $t0)
Shot 'hover-ignore' 0
Unhover; Start-Sleep -Milliseconds 400

Write-Output "=== 4) 点「忽略规则」：胶囊回弹 + 面板从胶囊里长出来 ==="
Eval "document.getElementById('__fdIgPill').click()" | Out-Null
$cls = Eval "document.getElementById('__fdIgPill').className"
Assert "点后胶囊有高光 fd-sweep" $true ($cls -like '*fd-sweep*')
Assert "点后胶囊有回弹 fd-pop" $true ($cls -like '*fd-pop*')
$pcls = Eval "document.getElementById('__fdIgnorePanel').className"
Assert "面板在播 fd-island-in" $true ($pcls -like '*fd-island-in*')
$origin = Eval "document.getElementById('__fdIgnorePanel').style.transformOrigin"
Write-Output ("  面板 transform-origin = " + $origin + "  （应指向胶囊中心，不是默认 100% 0）")
Assert "transform-origin 已按胶囊位置计算" $true ($origin -like '*px*')
Shot 'island-open-ignore' 90
Start-Sleep -Milliseconds 700
Assert "动画结束后面板仍显示" "block" (Eval "document.getElementById('__fdIgnorePanel').style.display")
Assert "打开时胶囊带 fd-open 环" $true ((Eval "document.getElementById('__fdIgPill').className") -like '*fd-open*')
Shot 'island-open-ignore-done' 0

Write-Output "=== 5) 再点一次：面板缩回胶囊 ==="
Eval "document.getElementById('__fdIgPill').click()" | Out-Null
Assert "收起时在播 fd-island-out" $true ((Eval "document.getElementById('__fdIgnorePanel').className") -like '*fd-island-out*')
Shot 'island-close-ignore' 100
Start-Sleep -Milliseconds 600
Assert "收起后面板 display=none" "none" (Eval "document.getElementById('__fdIgnorePanel').style.display")
Assert "收起后拆掉 fd-open 环" $false ((Eval "document.getElementById('__fdIgPill').className") -like '*fd-open*')

Write-Output "=== 6) 点「历史记录」：同样从胶囊长出来 + 互斥 ==="
Eval "document.getElementById('__fdHistPill').click()" | Out-Null
Assert "历史面板在播 fd-island-in" $true ((Eval "document.getElementById('__fdHistPanel').className") -like '*fd-island-in*')
Shot 'island-open-hist' 120
Start-Sleep -Milliseconds 700

Write-Output "=== 7) 点「深色主题」：文字翻牌 + 主题真的换了 ==="
$before = Eval "document.getElementById('__fdTheme').textContent"
Eval "document.getElementById('__fdTheme').click()" | Out-Null
Start-Sleep -Milliseconds 60
Assert "旧值残影 .fd-ghost 出现" $true ((Eval "document.querySelectorAll('#__fdTheme .fd-ghost').length") -eq 1)
Assert "新值在播 fd-roll" $true ((Eval "document.querySelector('#__fdTheme .fd-label').className") -like '*fd-roll*')
Shot 'morph-theme' 0
Start-Sleep -Milliseconds 600
$after = Eval "document.getElementById('__fdTheme').textContent"
Write-Output ("  主题胶囊文案：" + $before + " -> " + $after)
Assert "文案确实变了" $true ($before -ne $after)
Assert "残影已清理" 0 (Eval "document.querySelectorAll('#__fdTheme .fd-ghost').length")

Write-Output "=== 8) 点「窗口置顶」：等宿主回包后文字翻牌 ==="
$pin0 = Eval "document.getElementById('__fdTopMost').textContent"
Eval "document.getElementById('__fdTopMost').click()" | Out-Null
Start-Sleep -Milliseconds 900
$pin1 = Eval "document.getElementById('__fdTopMost').textContent"
Write-Output ("  置顶胶囊文案：" + $pin0 + " -> " + $pin1)
Assert "置顶文案变「已置顶」" "已置顶" $pin1
Eval "document.getElementById('__fdTopMost').click()" | Out-Null
Start-Sleep -Milliseconds 900
Assert "再点回到「窗口置顶」" "窗口置顶" (Eval "document.getElementById('__fdTopMost').textContent")

Write-Output "=== 9) 收尾：主题还原 ==="
Eval "document.getElementById('__fdTheme').click()" | Out-Null
Start-Sleep -Milliseconds 500
Eval "document.getElementById('__fdHistPill').click()" | Out-Null
Start-Sleep -Milliseconds 500
Shot 'final' 0

Write-Output "=== 10) 定格抓帧：把动画暂停在关键时刻，逐帧看「从胶囊里长出来」 ==="
# Page.captureScreenshot 自己要花 200ms 上下，正常播放时抓到的都是终态；
# 这里用 Web Animations API 直接 pause + currentTime 定格，拿到的中间态才可靠。
function PState([string]$tag) {
    $s = Eval ("JSON.stringify({ig:((document.getElementById('__fdIgnorePanel')||{}).style||{}).display," +
               "igc:(document.getElementById('__fdIgnorePanel')||{}).className," +
               "hi:((document.getElementById('__fdHistPanel')||{}).style||{}).display," +
               "hic:(document.getElementById('__fdHistPanel')||{}).className})")
    Write-Output ("  状态[" + $tag + "] " + $s)
}
function Freeze([string]$sel, [int]$ms) {
    return (Eval ("(function(){var a=document.querySelector('" + $sel + "').getAnimations();" +
                  "for(var i=0;i<a.length;i++){try{a[i].pause();a[i].currentTime=" + $ms + ";}catch(e){}}" +
                  "return a.length})()"))
}
function Unfreeze([string]$sel) {
    Eval ("(function(){var a=document.querySelector('" + $sel + "').getAnimations();" +
          "for(var i=0;i<a.length;i++){try{a[i].play();}catch(e){}}})()") | Out-Null
}

Eval "document.getElementById('__fdIgPill').click()" | Out-Null
foreach ($ms in @(30, 120, 240, 360)) {
    $n = Freeze '#__fdIgnorePanel' $ms
    Write-Output ("  忽略面板定格在 " + $ms + "ms / 460ms（子树动画 " + $n + " 条）")
    Shot ("freeze-ignore-{0:d3}ms" -f $ms) 0
}
Unfreeze '#__fdIgnorePanel'
Start-Sleep -Milliseconds 700
Eval "document.getElementById('__fdIgPill').click()" | Out-Null
Start-Sleep -Milliseconds 800
PState '忽略已收起'

Eval "document.getElementById('__fdHistPill').click()" | Out-Null
foreach ($ms in @(120, 300)) {
    $n = Freeze '#__fdHistPanel' $ms
    Write-Output ("  历史面板定格在 " + $ms + "ms / 460ms（子树动画 " + $n + " 条）")
    Shot ("freeze-hist-{0:d3}ms" -f $ms) 0
}
Unfreeze '#__fdHistPanel'
Start-Sleep -Milliseconds 700
PState '历史已展开'

# 主题胶囊的文字翻牌：定格在换字那一瞬
Eval "document.getElementById('__fdTheme').click()" | Out-Null
$n = Freeze '#__fdTheme' 130
Write-Output ("  主题胶囊定格在 130ms / 420ms（子树动画 " + $n + " 条）")
Shot 'freeze-theme-130ms' 0
Unfreeze '#__fdTheme'
Start-Sleep -Milliseconds 700
Eval "document.getElementById('__fdTheme').click()" | Out-Null
Start-Sleep -Milliseconds 700
Eval "document.getElementById('__fdHistPill').click()" | Out-Null
Start-Sleep -Milliseconds 800
PState '收尾全关'
Shot 'freeze-final' 0

$ws.Dispose()
Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }
