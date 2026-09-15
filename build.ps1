# Builds the standalone "file comparator" desktop exe:
#   1. rewrites the source HTML so its two CDN scripts come from local embedded copies
#   2. generates the multi-size app icon
#   3. compiles a single-file WinForms+WebView2 host exe with everything embedded
# ASCII-only on purpose (Windows PowerShell 5.1 reads BOM-less files as GBK).
param(
    [string]$Root = "$PSScriptRoot",
    [Parameter(Mandatory = $true)][string]$SourceHtml,
    [string]$OutName = "文件比较器(Win10-11版).exe"
)
$ErrorActionPreference = 'Stop'

$assetsDir = Join-Path $Root 'assets'
$buildDir  = Join-Path $Root 'build'
$webDir    = Join-Path $buildDir 'web'
$srcDir    = Join-Path $Root 'src'
New-Item -ItemType Directory -Force -Path $buildDir, $webDir | Out-Null

# ---- 1) web payload ------------------------------------------------------
$html = [System.IO.File]::ReadAllText($SourceHtml, [System.Text.Encoding]::UTF8)
$swaps = @{
    'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js'      = 'jszip.min.js'
    'https://cdnjs.cloudflare.com/ajax/libs/FileSaver.js/2.0.5/FileSaver.min.js' = 'FileSaver.min.js'
}
foreach ($k in $swaps.Keys) {
    if ($html.Contains($k)) { $html = $html.Replace($k, $swaps[$k]) }
    else { Write-Warning "CDN url not found in source (already local?): $k" }
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $webDir 'index.html'), $html, $utf8NoBom)
Copy-Item (Join-Path $assetsDir 'jszip.min.js')     (Join-Path $webDir 'jszip.min.js')     -Force
Copy-Item (Join-Path $assetsDir 'FileSaver.min.js') (Join-Path $webDir 'FileSaver.min.js') -Force
Write-Output "web payload ready: $webDir"

# ---- 2) icon -------------------------------------------------------------
$icon = Join-Path $assetsDir 'app.ico'
& (Join-Path $Root 'make-icon.ps1') -Out $icon | Write-Output

# ---- 3) compile ----------------------------------------------------------
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = (Get-ChildItem "C:\Windows\Microsoft.NET\Framework64" -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1).FullName
}
if (-not $csc) { throw "csc.exe not found" }

$refRoot = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
if (-not (Test-Path $refRoot)) { $refRoot = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319" }
$fw = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xml.dll') |
      ForEach-Object { Join-Path $refRoot $_ }

$wvRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2'
$wvVer = Get-ChildItem $wvRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $wvVer) { throw "WebView2 nuget package not found under $wvRoot" }
$wvCore  = Join-Path $wvVer.FullName 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$wvForms = Join-Path $wvVer.FullName 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
$wvLoad  = Join-Path $wvVer.FullName 'runtimes\win-x64\native\WebView2Loader.dll'
foreach ($f in @($wvCore, $wvForms, $wvLoad)) { if (-not (Test-Path $f)) { throw "missing: $f" } }

$outExe = Join-Path $buildDir $OutName
if (Test-Path $outExe) { Remove-Item $outExe -Force }

$cscArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/langversion:5', '/codepage:65001',
    '/warn:4',
    "/out:$outExe",
    "/win32icon:$icon"
)
$fw       | ForEach-Object { $cscArgs += "/r:$_" }
$cscArgs += "/r:$wvCore"
$cscArgs += "/r:$wvForms"
$cscArgs += "/resource:$(Join-Path $webDir 'index.html'),web.index.html"
$cscArgs += "/resource:$(Join-Path $webDir 'jszip.min.js'),web.jszip.min.js"
$cscArgs += "/resource:$(Join-Path $webDir 'FileSaver.min.js'),web.FileSaver.min.js"
$cscArgs += "/resource:$wvCore,lib.Microsoft.Web.WebView2.Core.dll"
$cscArgs += "/resource:$wvForms,lib.Microsoft.Web.WebView2.WinForms.dll"
$cscArgs += "/resource:$wvLoad,lib.WebView2Loader.dll"
$cscArgs += "/resource:$icon,app.ico"
$cscArgs += (Join-Path $srcDir 'Program.cs')

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compilation failed (exit $LASTEXITCODE)" }

$fi = Get-Item $outExe
Write-Output ("built: {0}  ({1:N0} bytes)" -f $fi.FullName, $fi.Length)
