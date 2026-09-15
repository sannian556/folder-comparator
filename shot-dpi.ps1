# DPI-aware screenshot helper for the native tool (ASCII-only script).
# Calls SetProcessDPIAware() BEFORE any window/DC query so that GetWindowRect
# returns PHYSICAL pixels and CopyFromScreen grabs a crisp 125%-scaled image.
param(
    [string]$Exe,
    [string]$DirA = "$PSScriptRoot\testdata\A",
    [string]$DirB = "$PSScriptRoot\testdata\B",
    [string]$Out  = "$PSScriptRoot\build-native\shot-dpi.png",
    [int]$WaitSec = 6,
    [switch]$NoArgs
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class DpiWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);

    public static IntPtr FindVisible(uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (!IsWindowVisible(h)) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }
    public static string Title(IntPtr h) { StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512); return t.ToString(); }
    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }

    public static List<string> Children(IntPtr parent) {
        List<string> res = new List<string>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256);
            StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512);
            RECT r; GetWindowRect(h, out r);
            res.Add(h.ToInt64() + "|" + c.ToString() + "|" + t.ToString() + "|" +
                    r.Left + "," + r.Top + "," + (r.Right-r.Left) + "," + (r.Bottom-r.Top));
            return true;
        }, IntPtr.Zero);
        return res;
    }

    public static void Shoot(IntPtr hwnd, string path) {
        RECT r = Rect(hwnd);
        int w = r.Right - r.Left, hh = r.Bottom - r.Top;
        using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, hh))
        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp)) {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, hh));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@

$aware = [DpiWin]::SetProcessDPIAware()
Write-Output "SetProcessDPIAware = $aware"

Get-Process | Where-Object { $_.ProcessName -like "*文件比较器*" } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700

if (-not $Exe) {
    $Exe = (Get-ChildItem "\docs","\build-native" -Filter '*XP-Win7*.exe' | Select-Object -First 1).FullName
}
Write-Output "exe: $Exe"

if ($NoArgs) {
    $p = Start-Process $Exe -PassThru
} else {
    $p = Start-Process $Exe -ArgumentList @("`"$DirA`"", "`"$DirB`"") -PassThru
}
Start-Sleep -Seconds $WaitSec
$p.Refresh()
if ($p.HasExited) { throw "process exited early, code $($p.ExitCode)" }

$main = [DpiWin]::FindVisible([uint32]$p.Id)
if ($main -eq [IntPtr]::Zero) { throw "no visible window" }
Write-Output ("title: " + [DpiWin]::Title($main))

[void][DpiWin]::ShowWindow($main, 9)
[void][DpiWin]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
[void][DpiWin]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 1500

$r = [DpiWin]::Rect($main)
Write-Output ("window rect (physical): " + $r.Left + "," + $r.Top + " " + ($r.Right-$r.Left) + "x" + ($r.Bottom-$r.Top))

[DpiWin]::Shoot($main, $Out)
Write-Output ("screenshot: " + $Out)

foreach ($c in [DpiWin]::Children($main)) {
    if ($c -like '*Static*' -or $c -like '*Progress*') { Write-Output ("  child: " + $c) }
}
