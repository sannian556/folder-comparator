# Verifies the "always on top" toggle in both builds.
#   native : finds the BS_AUTOCHECKBOX child, clicks it with BM_CLICK, checks WS_EX_TOPMOST
#   -Web   : requires the WebView2 build; drives the injected page button over CDP
# ASCII-only source (Windows PowerShell 5.1 reads BOM-less files as GBK).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [switch]$Web,
    [int]$Port = 9223,
    [string]$Shot = "",
    [string]$CheckText = ""
)
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class TM {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const uint BM_CLICK = 0x00F5;

    public static bool IsTopMost(IntPtr h) { return (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0; }

    public static List<string> Buttons(IntPtr parent, string wantText) {
        List<string> res = new List<string>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256);
            if (c.ToString().IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) < 0) return true;
            StringBuilder t = new StringBuilder(256); GetWindowText(h, t, 256);
            if (wantText.Length > 0 && t.ToString() != wantText) return true;
            RECT r; GetWindowRect(h, out r);
            res.Add(h.ToInt64() + "|" + t.ToString() + "|" + r.Left + "," + r.Top + "," + (r.Right-r.Left) + "," + (r.Bottom-r.Top));
            return true;
        }, IntPtr.Zero);
        return res;
    }

    public static void Shoot(IntPtr hwnd, string path) {
        RECT r; GetWindowRect(hwnd, out r);
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        using (Bitmap bmp = new Bitmap(w, hh))
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, hh));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@

[void][TM]::SetProcessDPIAware()

$name = [System.IO.Path]::GetFileNameWithoutExtension($Exe)
Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

if ($Web) { $env:FOLDERDIFF_DEBUG_PORT = "$Port" }
$p = Start-Process $Exe -PassThru
Start-Sleep -Seconds $(if ($Web) { 14 } else { 5 })
$p.Refresh()
if ($p.HasExited) { throw "process exited early" }
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw "no main window" }
[void][TM]::ShowWindow($h, 9)
[void][TM]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 900
Write-Output ("exe    : " + $name)
Write-Output ("hWnd   : " + $h)
Write-Output ("topmost before : " + [TM]::IsTopMost($h))

if ($Shot -ne "") { [TM]::Shoot($h, $Shot); Write-Output ("shot   : " + $Shot) }

if (-not $Web) {
    $boxes = [TM]::Buttons($h, $CheckText)
    if ($boxes.Count -eq 0) { throw "no BUTTON child matching '$CheckText' found" }
    foreach ($b in $boxes) {
        $f = $b.Split('|')
        Write-Output ("checkbox: " + $f[1] + "  rect=" + $f[2])
        [void][TM]::SendMessage([IntPtr][int64]$f[0], [TM]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 700
        Write-Output ("topmost after  : " + [TM]::IsTopMost($h) + "   (clicked once)")
        [void][TM]::SendMessage([IntPtr][int64]$f[0], [TM]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 700
        Write-Output ("topmost after2 : " + [TM]::IsTopMost($h) + "   (clicked twice = back off)")
    }
} else {
    $targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 15
    $page = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
    if (-not $page) { throw "no page target on debug port" }
    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $ct = [System.Threading.CancellationToken]::None
    $ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait()

    function Eval([string]$expr) {
        $msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true } } |
               ConvertTo-Json -Depth 8 -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
        $ws.SendAsync([System.ArraySegment[byte]]::new($bytes, 0, $bytes.Length),
                      [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
        $buf = New-Object byte[] 262144
        $res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf, 0, $buf.Length), $ct).Result
        return [System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count)
    }

    Write-Output ("probe  : " + (Eval "JSON.stringify({exists: !!document.getElementById('__fdTopMost'), text: (document.getElementById('__fdTopMost')||{}).textContent})"))
    Write-Output ("rect   : " + (Eval "JSON.stringify((function(){var b=document.getElementById('__fdTopMost');var r=b.getBoundingClientRect();var hd=document.querySelector('.header').getBoundingClientRect();var s=getComputedStyle(b);return {btn:[Math.round(r.left),Math.round(r.top),Math.round(r.width),Math.round(r.height)],header:[Math.round(hd.left),Math.round(hd.top),Math.round(hd.width),Math.round(hd.height)],bg:s.backgroundColor,fg:s.color,radius:s.borderRadius,font:s.fontSize};})())"))
    Write-Output ("click  : " + (Eval "document.getElementById('__fdTopMost').click(); 'clicked'"))
    Start-Sleep -Milliseconds 900
    Write-Output ("topmost after  : " + [TM]::IsTopMost($h))
    Write-Output ("state  : " + (Eval "JSON.stringify({text: document.getElementById('__fdTopMost').textContent})"))
    Write-Output ("click2 : " + (Eval "document.getElementById('__fdTopMost').click(); 'clicked'"))
    Start-Sleep -Milliseconds 900
    Write-Output ("topmost after2 : " + [TM]::IsTopMost($h))
    Write-Output ("state2 : " + (Eval "JSON.stringify({text: document.getElementById('__fdTopMost').textContent})"))

    $themeProbe = "JSON.stringify({attr: document.documentElement.getAttribute('data-fd-theme'), bg: getComputedStyle(document.body).backgroundColor, card: getComputedStyle(document.querySelector('.folder-card')).backgroundColor, text: document.getElementById('__fdTheme').textContent})"
    Write-Output ("theme  before : " + (Eval $themeProbe))
    Write-Output ("theme  click1 : " + (Eval "document.getElementById('__fdTheme').click(); 'clicked'"))
    Start-Sleep -Milliseconds 900
    Write-Output ("theme  after1 : " + (Eval $themeProbe))
    Write-Output ("theme  click2 : " + (Eval "document.getElementById('__fdTheme').click(); 'clicked'"))
    Start-Sleep -Milliseconds 900
    Write-Output ("theme  after2 : " + (Eval $themeProbe))
    $ws.Dispose()
}

Start-Sleep -Milliseconds 400
Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
