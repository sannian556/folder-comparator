# 验收「官网网页版」：用真浏览器（Edge/Chrome，CDP 驱动）打开生成的官网 html，确认它就是
# Win10-11 版 —— 三个胶囊（忽略规则/历史记录/深色主题）、面板动效、忽略规则引擎、
# 历史记录（浏览器模式下存 localStorage）、以及完整版下载 + 联系作者两个入口都在。
#   还会断言「窗口置顶」在网页版里**不出现**（浏览器里没有窗口可置顶）；
#   并把压缩包真的下下来，核对里面正好三个文件、且说明里讲了误报怎么处理。
# 用法: verify-site.ps1 [-SiteHtml <桌面的官网html>] [-Zip <分发包>] [-Browser <msedge.exe>]
param(
    [string]$SiteHtml = "$PSScriptRoot\build\文件比较器-官网.html",
    [string]$ZipPath  = "$PSScriptRoot\dist\文件比较器.zip",
    [string]$ServeDir = "$PSScriptRoot\build",
    [int]$HttpPort = 8793,
    [int]$CdpPort  = 9333,
    [string]$Browser = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    [string]$AuthorUrl = "https://b23.tv/7ojZmWb",
    [string]$DownloadName = "文件比较器.zip"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$serveName = '官网验收.html'
$servePath = Join-Path $ServeDir $serveName
Copy-Item -LiteralPath $SiteHtml -Destination $servePath -Force
Copy-Item -LiteralPath $ZipPath  -Destination (Join-Path $ServeDir $DownloadName) -Force
$pageUrl = "http://127.0.0.1:$HttpPort/" + [uri]::EscapeDataString($serveName)

# ── 静态文件服务器（node 一行脚本，写到临时目录，不污染工程）──────────────
$srvJs = Join-Path $env:TEMP 'fd-serve.js'
$code = @'
const http=require('http'),fs=require('fs'),path=require('path');
const root=process.argv[2];
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript','.png':'image/png','.exe':'application/octet-stream'};
http.createServer((q,s)=>{
  let p=decodeURIComponent(q.url.split('?')[0]);
  if(p==='/')p='/index.html';
  const f=path.join(root,p);
  fs.readFile(f,(e,d)=>{
    if(e){s.writeHead(404);s.end('404');return;}
    s.writeHead(200,{'Content-Type':types[path.extname(f).toLowerCase()]||'application/octet-stream'});
    s.end(d);
  });
}).listen(parseInt(process.argv[3],10),'127.0.0.1',()=>console.log('up'));
'@
[System.IO.File]::WriteAllText($srvJs, $code, (New-Object System.Text.UTF8Encoding($false)))

$job = Start-Job -ScriptBlock {
    param($js, $root, $port)
    & 'C:\Program Files\nodejs\node.exe' $js $root $port
} -ArgumentList $srvJs, $ServeDir, $HttpPort

$ready = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    try { $r = Invoke-WebRequest $pageUrl -UseBasicParsing -TimeoutSec 2; if ($r.StatusCode -eq 200) { $ready = $true; break } }
    catch { }
}
if (-not $ready) { Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force -ErrorAction SilentlyContinue; throw "静态服务器没起来" }
Write-Output ("静态服务器就绪: " + $pageUrl)

$profileDir = Join-Path $env:TEMP 'fd-edge-profile'
if (Test-Path $profileDir) { Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue }
$proc = Start-Process $Browser -PassThru -ArgumentList @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    "--user-data-dir=$profileDir", "--remote-debugging-port=$CdpPort", $pageUrl
)

$target = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 400
    try {
        $list = Invoke-RestMethod "http://127.0.0.1:$CdpPort/json/list" -TimeoutSec 3
        $target = $list | Where-Object { $_.type -eq 'page' -and $_.url -like "*$HttpPort*" } | Select-Object -First 1
        if ($target) { break }
    } catch { }
}
if (-not $target) { throw "连不上浏览器调试端口 $CdpPort" }

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$target.webSocketDebuggerUrl, $ct).Wait()
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
function Reload() {
    Cdp 'Page.reload' @{} | Out-Null
    Start-Sleep -Seconds 2
    WaitFor "!!document.getElementById('btnCompare')" 20000 | Out-Null
    WaitFor "!!document.getElementById('__fdBar')" 20000 | Out-Null
}

# 页面还在解析时断言会全部落空，所以先等关键元素出现
function WaitFor([string]$expr, [int]$timeoutMs) {
    $n = [int]($timeoutMs / 250)
    for ($i = 0; $i -lt $n; $i++) {
        if ((Eval $expr) -eq $true) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; Write-Output ("  OK   " + $label); return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}

Cdp 'Page.enable' @{} | Out-Null
if (-not (WaitFor "!!document.getElementById('btnCompare')" 25000)) { throw "页面 25 秒内没加载出工具本体" }
if (-not (WaitFor "!!document.getElementById('__fdBar')" 15000)) { throw "页面加载完了但注入脚本没跑起来（没有 #__fdBar）" }
Write-Output "页面与注入脚本都就绪"

Write-Output "=== 1) 页面就是 Win10-11 版：右上角三个胶囊 ==="
Assert "工具条 #__fdBar 已注入" $true ((Eval "!!document.getElementById('__fdBar')") -eq $true)
$pillCount = Eval "document.querySelectorAll('#__fdBar button').length"
Write-Output ("  胶囊数量 = " + $pillCount + "（网页版没有「窗口置顶」，应为 3）")
Assert "胶囊数量 3" 3 $pillCount
Assert "没有「窗口置顶」胶囊" $false ((Eval "!!document.getElementById('__fdTopMost')") -eq $true)
Assert "忽略规则胶囊有文字" "忽略规则" (Eval "document.getElementById('__fdIgPill').textContent")
Assert "历史记录胶囊有文字" "历史记录" (Eval "document.getElementById('__fdHistPill').textContent")
Assert "主题胶囊有文字" $true ((Eval "document.getElementById('__fdTheme').textContent.length") -gt 0)
Assert "动效样式已注入" $true ((Eval "document.getElementById('__fdThemeCss').textContent.indexOf('fdIslandIn') > 0") -eq $true)

Write-Output "=== 2) 工具本体还在（原页面没被动过）==="
Assert "开始对比按钮在" $true ((Eval "!!document.getElementById('btnCompare')") -eq $true)
Assert "两个文件夹输入框在" 2 (Eval "document.querySelectorAll('input[webkitdirectory]').length")

Write-Output "=== 3) 深色 / 浅色主题切换 ==="
$before = Eval "document.documentElement.getAttribute('data-fd-theme') || 'none'"
Eval "document.getElementById('__fdTheme').click()" | Out-Null
Start-Sleep -Milliseconds 700
$after = Eval "document.documentElement.getAttribute('data-fd-theme') || 'none'"
$saved = Eval "localStorage.getItem('fd-theme') || 'none'"
Write-Output ("  主题: " + $before + " -> " + $after + "  localStorage=" + $saved)
Assert "主题属性已翻转" $true ($before -ne $after)
Assert "选择已存进 localStorage" $after $saved
Assert "主题胶囊文案跟着变" $true ((Eval "document.getElementById('__fdTheme').textContent.length") -gt 0)

Write-Output "=== 4) 忽略规则面板：从胶囊里长出来 ==="
Eval "document.getElementById('__fdIgPill').click()" | Out-Null
$cls = Eval "document.getElementById('__fdIgnorePanel').className"
$origin = Eval "document.getElementById('__fdIgnorePanel').style.transformOrigin"
Write-Output ("  面板 class = " + $cls + "   transform-origin = " + $origin)
Assert "面板在播 fd-island-in" $true ($cls -like '*fd-island-in*')
Assert "transform-origin 按胶囊位置算" $true ($origin -like '*px*')
Start-Sleep -Milliseconds 600
Assert "面板已显示" "block" (Eval "document.getElementById('__fdIgnorePanel').style.display")

Write-Output "=== 5) 忽略规则引擎在网页上照样跑 ==="
$n = Eval "window.__fdIgnore.set(JSON.stringify({vcs:true,temp:true,logs:true,custom:''}))"
Write-Output ("  三档预设的规则条数 = " + $n + "（与 exe 一致应为 19 = 6+9+4）")
Assert "规则条数 19" 19 $n
Assert "命中 .git/config" $true ((Eval "window.__fdIgnore.isIgnored('.git/config',false)") -eq $true)
Assert "命中 Thumbs.db" $true ((Eval "window.__fdIgnore.isIgnored('Thumbs.db',false)") -eq $true)
Assert "普通文件不误伤" $false ((Eval "window.__fdIgnore.isIgnored('readme.md',false)") -eq $true)
$n2 = Eval "window.__fdIgnore.set(JSON.stringify({vcs:true,temp:true,logs:true,custom:'!keep.log'}))"
Write-Output ("  加一条例外后的规则条数 = " + $n2)
Assert "例外 !keep.log 放行" $false ((Eval "window.__fdIgnore.isIgnored('keep.log',false)") -eq $true)
Eval "window.__fdIgnore.set(JSON.stringify({vcs:false,temp:false,logs:false,custom:''}))" | Out-Null
Assert "清空后无规则" 0 (Eval "JSON.parse(window.__fdIgnore.state()).count")
Eval "document.getElementById('__fdIgPill').click()" | Out-Null
Start-Sleep -Milliseconds 600
Assert "面板已收起" "none" (Eval "document.getElementById('__fdIgnorePanel').style.display")

Write-Output "=== 6) 历史记录：浏览器模式下存 localStorage ==="
$seed = "JSON.stringify([btoa(unescape(encodeURIComponent(JSON.stringify({t:'2026-09-15 12:00:00',a:'A',b:'B',zip:'p.zip',m:2,oa:1,ob:1,same:3,ign:'',items:[{k:'M',r:'x.txt',sa:1,sb:2}]}))))])"
Eval ("localStorage.setItem('fd-history', " + $seed + ")") | Out-Null
Assert "localStorage 里有 1 条" 1 (Eval "JSON.parse(localStorage.getItem('fd-history')).length")
Reload
Assert "胶囊显示条数" "历史记录 (1)" (Eval "document.getElementById('__fdHistPill').textContent")
Eval "document.getElementById('__fdHistPill').click()" | Out-Null
Start-Sleep -Milliseconds 800
$body = Eval "document.getElementById('__fdHistBody').textContent"
$info = Eval "document.getElementById('__fdHistInfo').textContent"
Write-Output ("  面板正文 = " + $body)
Write-Output ("  面板信息 = " + $info)
Assert "列表里有 A ⇄ B" $true ($body -like '*A*B*')
Assert "四类计数在" $true ($body -like '*2 不同*')

Write-Output "=== 7) 历史清理（二次确认）==="
Eval "document.getElementById('__fdHistClean').click()" | Out-Null
Start-Sleep -Milliseconds 300
Assert "弹出二次确认" "block" (Eval "document.getElementById('__fdHistConfirm').style.display")
Eval "document.getElementById('__fdHistCleanOk').click()" | Out-Null
Start-Sleep -Milliseconds 800
Assert "localStorage 已清空" 0 (Eval "JSON.parse(localStorage.getItem('fd-history')).length")
Assert "胶囊恢复为「历史记录」" "历史记录" (Eval "document.getElementById('__fdHistPill').textContent")

Write-Output "=== 8) 完整版下载 + 联系作者 ==="
Assert "下载按钮存在" $true ((Eval "!!document.getElementById('btnDownloadFull')") -eq $true)
Assert "下载指向压缩包" $true ((Eval "document.getElementById('btnDownloadFull').getAttribute('href')") -eq $DownloadName)
Assert "下载带 download 属性" $true ((Eval "document.getElementById('btnDownloadFull').hasAttribute('download')") -eq $true)
Assert "存盘名就是压缩包名" $DownloadName (Eval "document.getElementById('btnDownloadFull').getAttribute('download')")
Assert "联系作者指向 $AuthorUrl" $AuthorUrl (Eval "document.getElementById('btnContactAuthor').href")
Assert "联系作者新窗口打开" "_blank" (Eval "document.getElementById('btnContactAuthor').target")

Write-Output "=== 9) 真的把压缩包下下来看看（走 HTTP，和用户点下载一样）==="
$dl = Join-Path $env:TEMP 'fd-downloaded.zip'
Remove-Item $dl -Force -ErrorAction SilentlyContinue
try {
    Invoke-WebRequest ("http://127.0.0.1:$HttpPort/" + [uri]::EscapeDataString($DownloadName)) `
        -OutFile $dl -UseBasicParsing -TimeoutSec 30
    Assert "压缩包下载成功" $true (Test-Path -LiteralPath $dl)
    Assert "下载到的字节数与本地一致" (Get-Item -LiteralPath $ZipPath).Length (Get-Item -LiteralPath $dl).Length
    $z = [System.IO.Compression.ZipFile]::OpenRead($dl)
    try {
        $names = @($z.Entries | ForEach-Object { $_.FullName })
        Write-Output ("  包内: " + ($names -join ' / '))
        Assert "包内正好三个文件" 3 $names.Count
        Assert "含 文件比较器.exe" $true ($names -contains '文件比较器.exe')
        Assert "含 请先读我.txt" $true ($names -contains '请先读我.txt')
        Assert "含 使用说明.txt" $true ($names -contains '使用说明.txt')
        $rd = $z.Entries | Where-Object { $_.FullName -eq '请先读我.txt' } | Select-Object -First 1
        $sr = New-Object System.IO.StreamReader($rd.Open(), [System.Text.Encoding]::UTF8)
        $txt = $sr.ReadToEnd(); $sr.Close()
        Assert "「请先读我」讲了误报与白名单" $true (($txt.Contains('误报')) -and ($txt.Contains('白名单')))
    }
    finally { $z.Dispose() }
}
finally { Remove-Item $dl -Force -ErrorAction SilentlyContinue }

$ws.Dispose()
# 收尾只关掉"自己启动的那个"浏览器：按本次专用的 user-data-dir 匹配命令行。
# 千万别按进程名或 exe 路径去杀 msedge —— 那会把用户自己开着的 Edge 一起关掉（踩过一次）。
try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
for ($i = 0; $i -lt 10; $i++) {
    $mine = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue |
              Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profileDir) })
    if ($mine.Count -eq 0) { break }
    foreach ($m in $mine) { try { Stop-Process -Id $m.ProcessId -Force -ErrorAction SilentlyContinue } catch { } }
    Start-Sleep -Milliseconds 300
}
Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -Force -ErrorAction SilentlyContinue
Remove-Item $srvJs -Force -ErrorAction SilentlyContinue
Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $servePath -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $ServeDir $DownloadName) -Force -ErrorAction SilentlyContinue

Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }