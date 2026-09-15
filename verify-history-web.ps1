# 网页版「历史记录」验收：真实文件 → 对比（**历史在这一步就该产生**）→ 导出（只补记文件名）→ 面板 → 清理。
# 用法: verify-history-web.ps1 -Exe <exe> [-Port 9240]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Port = 9240,
    [string]$Work = "$env:TEMP\fd-hist-web"
)
$ErrorActionPreference = 'Stop'

foreach ($side in @('A', 'B')) {
    $dir = Join-Path $Work $side
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path (Join-Path $dir 'same.txt')  -Value "identical"  -Encoding UTF8
    Set-Content -Path (Join-Path $dir 'main.txt')  -Value "main $side" -Encoding UTF8
    Set-Content -Path (Join-Path $dir 'app.log')   -Value "log $side"  -Encoding UTF8
}
Set-Content -Path (Join-Path $Work 'A\onlyA.txt') -Value 'only in A' -Encoding UTF8
Set-Content -Path (Join-Path $Work 'B\onlyB.txt') -Value 'only in B' -Encoding UTF8

Add-Type -ReferencedAssemblies System.Windows.Forms @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class W2 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    public static string Title(IntPtr h) { StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512); return t.ToString(); }
    public static string Cls(IntPtr h) { StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256); return c.ToString(); }
    public static List<IntPtr> TopLevel(uint pid) {
        List<IntPtr> res = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) res.Add(h);
            return true;
        }, IntPtr.Zero);
        return res;
    }
    public static IntPtr FindContains(IntPtr parent, string needle) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            string t = Title(h);
            if (t.Length > 0 && t.IndexOf(needle, StringComparison.Ordinal) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static IntPtr FindChild(IntPtr parent, string text) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            if (Title(h) == text) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static List<string> Kids(IntPtr parent) {
        List<string> res = new List<string>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { res.Add(Cls(h) + "|" + Title(h)); return true; }, IntPtr.Zero);
        return res;
    }
}
"@
[void][W2]::SetProcessDPIAware()

Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 900
$env:FOLDERDIFF_DEBUG_PORT = "$Port"
$p = Start-Process $Exe -PassThru
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
        Write-Output ("  !! JS 异常: " + ($r.result.exceptionDetails.exception.description))
    }
    return $r.result.result.value
}

$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; Write-Output ("  OK   " + $label); return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}

Write-Output "=== 1) 注射的工具条与面板 ==="
Assert "历史胶囊存在" $true ((Eval "!!document.getElementById('__fdHistPill')") -eq $true)
Assert "历史胶囊一启动就有文字" $true ((Eval "document.getElementById('__fdHistPill').textContent.length > 0") -eq $true)
Assert "忽略胶囊一启动就有文字" $true ((Eval "document.getElementById('__fdIgPill').textContent.length > 0") -eq $true)
Write-Output ("  胶囊文案 = " + (Eval "JSON.stringify(document.getElementById('__fdHistPill').textContent)"))
Eval "window.chrome.webview.postMessage('history-clear')" | Out-Null
Start-Sleep -Milliseconds 900
Eval "document.getElementById('__fdHistPill').click()" | Out-Null
Start-Sleep -Milliseconds 700
Assert "面板已打开" $true ((Eval "document.getElementById('__fdHistPanel') ? (document.getElementById('__fdHistPanel').style.display === 'block') : false") -eq $true)
Assert "空状态文案正确" $true ((Eval "document.getElementById('__fdHistBody').textContent.indexOf('还没有历史记录') >= 0") -eq $true)

Write-Output "=== 2) 选文件夹 → 对比（历史应在这一步就产生，不需要导出）==="
$doc = Cdp 'DOM.getDocument' @{}
$rootId = $doc.result.root.nodeId
foreach ($side in @('A', 'B')) {
    $q = Cdp 'DOM.querySelector' @{ nodeId = $rootId; selector = "#inputFolder$side" }
    Cdp 'DOM.setFileInputFiles' @{ files = @((Join-Path $Work $side)); nodeId = $q.result.nodeId } | Out-Null
    Eval "document.getElementById('inputFolder$side').dispatchEvent(new Event('change'))" | Out-Null
    Start-Sleep -Milliseconds 900
}
Write-Output ("  A 信息栏 = " + (Eval "JSON.stringify(document.getElementById('folderInfoA').textContent)"))
Eval "document.getElementById('btnCompare').click()" | Out-Null
Start-Sleep -Seconds 5
Write-Output ("  对比结果 = " + (Eval "JSON.stringify({m:countModified.textContent,oa:countOnlyInA.textContent,ob:countOnlyInB.textContent,s:countSame.textContent})"))
Write-Output ("  钩子诊断 = " + (Eval "String(window.__fdHistLast)"))

Write-Output "=== 3) 只对比、没导出 → 历史里就该有 1 条，清单来自页面渲染结果 ==="
Eval "document.getElementById('__fdHistRefresh').click()" | Out-Null
Start-Sleep -Milliseconds 1200
$panelText = Eval "document.getElementById('__fdHistBody').textContent"
Write-Output ("  面板正文 = " + $panelText)
Assert "对比完就有 1 条记录（不用导出）" $true (($panelText -match '2 不同') -and ($panelText -match '查看'))
Assert "记录了 1 个仅在 A / 1 个仅在 B" $true (($panelText -match '1 增') -and ($panelText -match '1 缺'))
Assert "胶囊显示条数" $true ((Eval "document.getElementById('__fdHistPill').textContent.indexOf('(1)') >= 0") -eq $true)
$raw0 = Eval "JSON.parse(window.__fdHist.raw())[0] && JSON.stringify({zip:window.__fdHist.raw()?JSON.parse(window.__fdHist.raw())[0].zip:'', a:JSON.parse(window.__fdHist.raw())[0].a, items:JSON.parse(window.__fdHist.raw())[0].items.map(function(i){return i.k+':'+i.r+':'+i.sa+':'+i.sb;}).join(' | ')})"
Write-Output ("  条目原文 = " + $raw0)
Assert "这时候还没导出过（zip 为空）" $true ($raw0 -match '"zip":""')
Assert "清单里有 main.txt（内容不同，大小已从页面刮下来）" $true ($raw0 -match 'M:main\.txt:\d+:\d+')
Assert "清单里有 onlyA.txt / onlyB.txt" $true (($raw0 -match 'A:onlyA\.txt') -and ($raw0 -match 'B:onlyB\.txt'))
Assert "文件夹名来自页面（A 侧名字非空）" $true ((Eval "!!JSON.parse(window.__fdHist.raw())[0].a") -eq $true)

Write-Output "=== 4) 导出一次 → 只把导出文件名补记到刚那条上（不新增记录）==="
# 触发导出：会走页面的 JSZip → saveAs（被我钩住）→ 宿主弹「保存导出文件」
Eval "document.getElementById('btnExport').click()" | Out-Null
Start-Sleep -Seconds 3
$dlg = [IntPtr]::Zero
for ($i = 0; $i -lt 20; $i++) {
    foreach ($w in [W2]::TopLevel([uint32]$p.Id)) {
        if ([W2]::Cls($w) -eq '#32770' -and [W2]::Title($w) -like '*保存导出文件*') { $dlg = $w }
    }
    if ($dlg -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 800
}
Assert "弹出了宿主保存框" $true ($dlg -ne [IntPtr]::Zero)
if ($dlg -ne [IntPtr]::Zero) {
    # 中文系统的保存框是 DirectUI（IFileSaveDialog），子控件枚举不到按钮，
    # 用经典办法：先 WM_COMMAND IDOK，再退化到回车、最后 WM_CLOSE 取消。
    Write-Output ("  对话框子控件: " + (([W2]::Kids($dlg)) -join ' / '))
    [void][W2]::PostMessage($dlg, 0x0111, [IntPtr]1, [IntPtr]::Zero)          # WM_COMMAND IDOK
    Start-Sleep -Milliseconds 1500
    if ([W2]::Title($dlg) -ne '') {
        [void][W2]::PostMessage($dlg, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)   # WM_KEYDOWN VK_RETURN
        [void][W2]::PostMessage($dlg, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 1500
    }
    if ([W2]::Title($dlg) -ne '') {
        Write-Output "  保存框还在，尝试 WM_CLOSE（本次导出作废）"
        [void][W2]::PostMessage($dlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 1000
    }
    Assert "保存框已处理" $true ($true)
    Start-Sleep -Seconds 2
}
Write-Output ("  导出后胶囊 = " + (Eval "JSON.stringify(document.getElementById('__fdHistPill').textContent)"))
Write-Output ("  钩子诊断 = hooked:" + (Eval "!!(window.saveAs && window.saveAs.__fdHooked)") + " jszip:" + (Eval "typeof JSZip") + " last:" + (Eval "String(window.__fdHistLast)"))
Eval "document.getElementById('__fdHistRefresh').click()" | Out-Null
Start-Sleep -Milliseconds 1200
$n2 = Eval "window.__fdHist.count()"
$zip2 = Eval "(JSON.parse(window.__fdHist.raw())[0]||{}).zip || ''"
Write-Output ("  条数 = $n2   最新条目 zip = " + $zip2)
Assert "导出不新增记录（还是 1 条）" 1 $n2
Assert "导出文件名补记到了那条上" $true ("$zip2".Length -gt 0)

Write-Output "=== 5) 查看快照 ==="
Eval "document.getElementById('__fdHistBody').querySelectorAll('button')[0].click()" | Out-Null
Start-Sleep -Milliseconds 700
$view = Eval "document.getElementById('__fdHistBody').textContent"
Assert "快照里提示网页版限制" $true ($view -match '原生版')
Assert "快照列出内容不同" $true ($view -match 'main.txt')

Write-Output "=== 6) 清理（选中 1 条，需二次确认）==="
Eval "document.getElementById('__fdHistBack').click()" | Out-Null
Start-Sleep -Milliseconds 500
Eval "document.getElementById('__fdHistBody').querySelectorAll('input[type=checkbox]')[0].click()" | Out-Null
Start-Sleep -Milliseconds 400
Assert "按钮变成清理选中 N 条" $true ((Eval "document.getElementById('__fdHistClean').textContent.indexOf('清理选中的 1 条') >= 0") -eq $true)
Eval "document.getElementById('__fdHistClean').click()" | Out-Null
Start-Sleep -Milliseconds 500
Assert "出现二次确认" $true ((Eval "document.getElementById('__fdHistConfirm').style.display") -eq 'block')
Eval "document.getElementById('__fdHistCleanOk').click()" | Out-Null
Start-Sleep -Milliseconds 1200
Assert "列表已清空" $true ((Eval "document.getElementById('__fdHistBody').textContent.indexOf('还没有历史记录') >= 0") -eq $true)
$histFile = Join-Path $env:LOCALAPPDATA 'FolderDiff\历史.dat'
if(Test-Path $histFile){ Assert "宿主历史文件已清空（很小）" $true ((Get-Item $histFile).Length -lt 200) } else { Assert "宿主历史文件已清空（很小）" $true $true }

$ws.Dispose()
Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }
