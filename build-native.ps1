# Builds the native (no browser engine) folder-compare tool from one source tree.
# Produces two single-file executables:
#   文件比较器(Win8-11版).exe  -> CLR 4.0 (Windows 8 / 10 / 11 ship .NET 4.x; Win7 needs .NET 4.0)
#   文件比较器(XP-Win7版).exe  -> CLR 2.0 (Windows XP SP3 / Vista / Win7: .NET 2.0 or 3.5)
# ASCII-only source on purpose.
param(
    [string]$Root = "$PSScriptRoot",
    [string]$OutDir = "$PSScriptRoot\build-native"
)
$ErrorActionPreference = 'Stop'

$native = Join-Path $Root 'native'
$assets = Join-Path $Root 'assets'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$icon = Join-Path $assets 'app.ico'
if (-not (Test-Path $icon)) {
    & (Join-Path $Root 'make-icon.ps1') -Out $icon | Write-Output
}

$sources = Get-ChildItem (Join-Path $native '*.cs') | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "no source files found in $native" }
Write-Output ("sources: " + ($sources.Count) + " files")

$manifest = Join-Path $native 'app.manifest'

function Build-Target {
    param(
        [string]$Compiler,
        [string]$RefDir,
        [string]$OutName
    )
    if (-not (Test-Path $Compiler)) { throw "compiler not found: $Compiler" }
    if (-not (Test-Path $RefDir)) { throw "reference dir not found: $RefDir" }

    $out = Join-Path $OutDir $OutName
    if (Test-Path $out) { Remove-Item $out -Force }

    $cscArgs = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/warn:4',
        "/out:$out",
        "/win32icon:$icon",
        "/win32manifest:$manifest",
        "/r:$(Join-Path $RefDir 'System.dll')",
        "/r:$(Join-Path $RefDir 'System.Drawing.dll')",
        "/r:$(Join-Path $RefDir 'System.Windows.Forms.dll')",
        "/resource:$icon,app.ico"
    )
    $cscArgs += $sources

    Write-Output ("--- compiling " + $OutName + " with " + (Split-Path $Compiler -Leaf) + " (" + $RefDir + ")")
    & $Compiler @cscArgs
    if ($LASTEXITCODE -ne 0) { throw ("compilation failed for " + $OutName + " (exit " + $LASTEXITCODE + ")") }

    $fi = Get-Item $out
    Write-Output ("    built: " + $fi.Name + "  " + $fi.Length.ToString("N0") + " bytes")
}

# CLR 4.0 build
Build-Target -Compiler "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" `
             -RefDir   "C:\Windows\Microsoft.NET\Framework\v4.0.30319" `
             -OutName  "文件比较器(Win8-11版).exe"

# CLR 2.0 build (works on XP SP3 and up)
$csc35 = "C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe"
if (-not (Test-Path $csc35)) { $csc35 = "C:\Windows\Microsoft.NET\Framework\v2.0.50727\csc.exe" }
Build-Target -Compiler $csc35 `
             -RefDir   "C:\Windows\Microsoft.NET\Framework\v2.0.50727" `
             -OutName  "文件比较器(XP-Win7版).exe"

Write-Output "done."
