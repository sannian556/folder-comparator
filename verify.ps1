# Automated smoke test: launches the built exe with a temporary remote-debugging
# port, then evaluates JS inside the WebView2 page to confirm the embedded
# scripts loaded and folder-picking support is present. ASCII-only source.
param(
    [string]$Exe = "$PSScriptRoot\build\FolderDiff.exe",
    [int]$Port = 9222
)
$ErrorActionPreference = 'Stop'

Get-Process -Name FolderDiff -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$env:FOLDERDIFF_DEBUG_PORT = "$Port"
Start-Process $Exe | Out-Null
Start-Sleep -Seconds 14

$proc = Get-Process -Name FolderDiff -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "app process is not running" }
Write-Output ("process alive: yes   window: {0}   title: {1}" -f $proc.MainWindowHandle, $proc.MainWindowTitle)

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$page = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
if (-not $page) { throw "no page target on debug port" }
Write-Output ("page url: {0}" -f $page.url)

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait()

$expr = @'
(function () {
  var i = document.createElement('input');
  var btns = [];
  Array.prototype.forEach.call(document.querySelectorAll('button'), function (b) { btns.push(b.textContent.trim()); });
  return JSON.stringify({
    title: document.title,
    readyState: document.readyState,
    url: location.href,
    jszip: typeof JSZip,
    jszipVersion: (typeof JSZip !== 'undefined' && JSZip.version) ? JSZip.version : null,
    saveAs: typeof saveAs,
    webkitdirectorySupported: ('webkitdirectory' in i),
    fileInputs: document.querySelectorAll('input[type=file]').length,
    folderCards: document.querySelectorAll('.folder-card').length,
    buttons: btns
  });
})()
'@

$msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
$seg = [System.ArraySegment[byte]]::new($bytes, 0, $bytes.Length)
$ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()

$buf = New-Object byte[] 262144
$seg2 = [System.ArraySegment[byte]]::new($buf, 0, $buf.Length)
$res = $ws.ReceiveAsync($seg2, $ct).Result
$txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count)
$ws.Dispose()

Write-Output "=== JS probe result ==="
Write-Output $txt
