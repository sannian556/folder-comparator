# 验收「联系作者 + 官网下载区 + 分发包」：
#   原生版 —— 启动真窗口，枚举子控件找到「联系作者」按钮，核对它在窗口右下角、可点击，
#             并确认 exe 里确实带着作者链接（.NET 字符串是 UTF-16，按两种对齐都查一遍）
#   官网版 —— 核对生成出来的 html：下载链接指向**压缩包**、联系作者指向 B 站短链、
#             两个区块都插在正确位置，且原始 html 一个字没改（大小仍是 76023）
#   分发包 —— 打开 文件比较器.zip，核对里面正好三个文件，且「请先读我.txt」讲了误报与白名单
# 用法: verify-contact.ps1 [-NativeExe <exe>] [-SiteHtml <html>] ...
param(
    [string]$NativeExe    = "$PSScriptRoot\build-native\文件比较器(XP-Win7版).exe",
    [string]$SiteHtml     = "$PSScriptRoot\build\文件比较器-官网.html",
    [string]$OrigHtml     = "$PSScriptRoot\web\文件比较器.html",
    [string]$ZipPath      = "$PSScriptRoot\dist\文件比较器.zip",
    [string]$AuthorUrl    = "https://b23.tv/7ojZmWb",
    [string]$DownloadName = "文件比较器.zip",
    [string]$SaveAsName   = "文件比较器.zip",
    [string]$ContactText  = "联系作者"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class CT {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);

    public static List<IntPtr> Kids(IntPtr root) {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Text(IntPtr h) { StringBuilder s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    public static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    public static int[] Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return new int[] { r.Left, r.Top, r.Right, r.Bottom }; }
}
"@
[void][CT]::SetProcessDPIAware()

$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; Write-Output ("  OK   " + $label); return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}

Write-Output "=== 1) 原生版：exe 里带着作者链接 ==="
$bytes = [System.IO.File]::ReadAllBytes($NativeExe)
$uniEven = [System.Text.Encoding]::Unicode.GetString($bytes)
$uniOdd = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
$hasUrl = $uniEven.Contains($AuthorUrl) -or $uniOdd.Contains($AuthorUrl)
Write-Output ("  exe = " + $NativeExe + "  (" + (Get-Item -LiteralPath $NativeExe).Length + " 字节)")
Assert "exe 内含作者链接（UTF-16）" $true $hasUrl

Write-Output "=== 2) 原生版：真窗口右下角有「联系作者」按钮 ==="
Get-Process | Where-Object { $_.Path -eq $NativeExe } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
$proc = Start-Process $NativeExe -PassThru
Start-Sleep -Seconds 4
$proc.Refresh()
$hwnd = $proc.MainWindowHandle
Assert "主窗口已出现" $true ($hwnd -ne [IntPtr]::Zero)

$wr = [CT]::Rect($hwnd)
Write-Output ("  窗口矩形 = " + ($wr -join ',') + "   （" + ($wr[2]-$wr[0]) + "x" + ($wr[3]-$wr[1]) + "）")

$hit = [IntPtr]::Zero
$hitRect = $null
foreach ($h in [CT]::Kids($hwnd)) {
    if ([CT]::Text($h) -eq $ContactText) { $hit = $h; $hitRect = [CT]::Rect($h); break }
}
Assert "找到「$ContactText」按钮" $true ($hit -ne [IntPtr]::Zero)
if ($hit -ne [IntPtr]::Zero) {
    $bw = $hitRect[2] - $hitRect[0]
    $bh = $hitRect[3] - $hitRect[1]
    Write-Output ("  按钮矩形 = " + ($hitRect -join ',') + "   （" + $bw + "x" + $bh + "）")
    $rightGap = $wr[2] - $hitRect[2]
    $bottomGap = $wr[3] - $hitRect[3]
    Write-Output ("  距窗口右边缘 = " + $rightGap + "px   距下边缘 = " + $bottomGap + "px")
    Assert "按钮在窗口右半边" $true ($hitRect[0] -gt ($wr[0] + ($wr[2] - $wr[0]) / 2))
    Assert "按钮在窗口下半部" $true ($hitRect[1] -gt ($wr[1] + ($wr[3] - $wr[1]) / 2))
    Assert "贴着右边（< 60px）" $true ($rightGap -lt 60)
    Assert "贴着下边（< 60px）" $true ($bottomGap -lt 60)
    Assert "按钮可见且可点" $true (([CT]::IsWindowVisible($hit)) -and ([CT]::IsWindowEnabled($hit)))
}
Get-Process | Where-Object { $_.Path -eq $NativeExe } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Output "=== 3) 官网版：下载 / 联系作者 两个链接 ==="
$site = [System.IO.File]::ReadAllText($SiteHtml, [System.Text.Encoding]::UTF8)
Assert "有官网区块 #siteFooter" $true ($site.Contains('id="siteFooter"'))
Assert "下载按钮存在" $true ($site.Contains('id="btnDownloadFull"'))
Assert "联系作者按钮存在" $true ($site.Contains('id="btnContactAuthor"'))
Assert "下载指向压缩包" $true ($site.Contains('href="' + $DownloadName + '"'))
Assert "下载带 download 属性" $true ($site.Contains('href="' + $DownloadName + '" download'))
Assert "存盘名 = $SaveAsName" $true ($site.Contains('download="' + $SaveAsName + '"'))
Assert "存盘名里不含 (XP-Win7版)" $false ($site.Contains('(XP-Win7版)'))
Assert "链接不再直指 exe" $false ($site.Contains('href="文件比较器(XP-Win7版).exe"'))
Assert "联系作者指向 $AuthorUrl" $true ($site.Contains('href="' + $AuthorUrl + '"'))
Assert "联系作者新窗口打开" $true ($site.Contains('target="_blank"'))
Assert "区块在工具之后" $true ($site.IndexOf('id="siteFooter"') -gt $site.IndexOf('id="resultsDetail"'))
Assert "区块在 Diff Modal 之前" $true ($site.IndexOf('id="siteFooter"') -lt $site.IndexOf('<!-- Diff Modal -->'))
Assert "样式插在 </head> 之前" $true ($site.IndexOf('id="fdSiteCss"') -lt $site.IndexOf('</head>'))

Write-Output "=== 4) 分发包：文件比较器.zip ==="
Assert "压缩包存在" $true (Test-Path -LiteralPath $ZipPath)
if (Test-Path -LiteralPath $ZipPath) {
    Write-Output ("  {0}  ({1:N0} KB)" -f $ZipPath, ((Get-Item -LiteralPath $ZipPath).Length / 1KB))
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        Write-Output ("  包内文件: " + ($names -join ' / '))
        foreach ($want in @('文件比较器.exe', '请先读我.txt', '使用说明.txt')) {
            Assert "包里包含 $want" $true ($names -contains $want)
        }
        Assert "包内只有这三个文件" 3 $names.Count

        $entry = $zip.Entries | Where-Object { $_.FullName -eq '请先读我.txt' } | Select-Object -First 1
        if ($entry) {
            $sr = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
            $readme = $sr.ReadToEnd()
            $sr.Close()
            Write-Output ("  请先读我.txt = " + $readme.Length + " 字符")
            foreach ($kw in @('误报', '白名单', '信任区', '360', 'Defender', '不联网', '运行环境')) {
                Assert ("说明里提到「" + $kw + "」") $true ($readme.Contains($kw))
            }
        }

        # 只发这一个版本：手册里不许再出现 Win10-11 / WebView2 / 两版对比的字样
        $manEntry = $zip.Entries | Where-Object { $_.FullName -eq '使用说明.txt' } | Select-Object -First 1
        if ($manEntry) {
            $sr = New-Object System.IO.StreamReader($manEntry.Open(), [System.Text.Encoding]::UTF8)
            $man = $sr.ReadToEnd()
            $sr.Close()
            Write-Output ("  使用说明.txt = " + $man.Length + " 字符")
            foreach ($kw in @('Win10-11', 'Win10', 'WebView2', 'XP-Win7版', '两个版本', '网页版')) {
                Assert ("手册里没有「" + $kw + "」") $false ($man.Contains($kw))
            }
            Assert "手册里讲了忽略规则语法（**/ 没被吃字）" $true ($man.Contains('**/'))
            Assert "手册里讲了 build/** 示例" $true ($man.Contains('build/**'))
        }
        $exeEntry = $zip.Entries | Where-Object { $_.FullName -eq '文件比较器.exe' } | Select-Object -First 1
        if ($exeEntry) {
            Assert "包里的 exe 与成品目录里的 exe 同大小" (Get-Item -LiteralPath $NativeExe).Length $exeEntry.Length
        }
    }
    finally { $zip.Dispose() }
}

Write-Output "=== 5) 原始 html 没被改动 ==="
$origLen = (Get-Item -LiteralPath $OrigHtml).Length
Write-Output ("  " + $OrigHtml + " = " + $origLen + " 字节")
Assert "原始 html 仍是 76023 字节" 76023 $origLen

Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }
