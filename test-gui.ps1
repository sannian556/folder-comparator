# Launches the native tool with two folders, screenshots the result, then drills
# into the "content differs" list to open the line-diff window. ASCII-only.
param(
    [string]$Exe = "$PSScriptRoot\build-native\文件比较器(Win8-11版).exe",
    [string]$DirA = "$PSScriptRoot\testdata\A",
    [string]$DirB = "$PSScriptRoot\testdata\B",
    [string]$ShotDir = "$PSScriptRoot\build-native"
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class NativeWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);

    public static IntPtr FindWindowOfPid(uint pid, string className) {
        bool wantAny = (className == null || className.Length == 0);
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256);
            if (wantAny || c.ToString() == className) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindWindowByTitlePrefix(uint pid, string prefix) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (!IsWindowVisible(h)) return true;
            string t = WinTitle(h);
            if (t.StartsWith(prefix, StringComparison.Ordinal)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindWindowContaining(uint pid, string needle) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (!IsWindowVisible(h)) return true;
            string t = WinTitle(h);
            if (t.IndexOf(needle, StringComparison.Ordinal) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static List<string> AllOfPid(uint pid) {
        List<string> r = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (!IsWindowVisible(h)) return true;
            r.Add("cls=" + GetClassNameStr(h) + " title=" + WinTitle(h));
            return true;
        }, IntPtr.Zero);
        return r;
    }

    public static string GetClassNameStr(IntPtr h) {
        StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256); return c.ToString();
    }

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

    public static string WinTitle(IntPtr h) {
        StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512); return t.ToString();
    }
    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
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

Get-Process | Where-Object { $_.ProcessName -like "*文件比较器*" -or $_.ProcessName -like "FileDiffTool*" } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

Write-Output "launching with two folders..."
$p = Start-Process $Exe -ArgumentList @("`"$DirA`"", "`"$DirB`"") -PassThru
Start-Sleep -Seconds 5

$p.Refresh()
if ($p.HasExited) { throw "process exited early, code $($p.ExitCode)" }
$main = [NativeWin]::FindWindowOfPid([uint32]$p.Id, "")
if ($main -eq [IntPtr]::Zero) { throw "no visible window found for pid $($p.Id)" }
Write-Output ("main window: " + [NativeWin]::WinTitle($main))

[NativeWin]::ShowWindow($main, 9) | Out-Null
[NativeWin]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 1200

$shot1 = Join-Path $ShotDir 'shot-native-main.png'
[NativeWin]::Shoot($main, $shot1)
Write-Output ("screenshot 1: " + $shot1)

# find the ListView of the active tab
$lv = [IntPtr]::Zero
$children = [NativeWin]::Children($main)
foreach ($c in $children) {
    $parts = $c.Split('|')
    if ($parts[1] -like '*SysListView32*' -and $lv -eq [IntPtr]::Zero) {
        $r = $parts[3].Split(',')
        Write-Output ("  listview: " + $c)
        if ([int]$r[2] -gt 200) { $lv = [IntPtr][int64]$parts[0] }
    }
}

if ($lv -ne [IntPtr]::Zero) {
    $r = [NativeWin]::Rect($lv)
    $x = $r.Left + 120
    $y = $r.Top + 34
    $itemCount = [NativeWin]::SendMessage($lv, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)   # LVM_GETITEMCOUNT
    Write-Output ("listview item count = " + $itemCount)
    Write-Output ("clicking list at $x,$y")
    [NativeWin]::Click($x, $y)
    Start-Sleep -Milliseconds 400

    # select the first row with the keyboard, then press Enter (host handles Enter)
    [NativeWin]::PostMessage($lv, 0x0100, [IntPtr]0x28, [IntPtr]::Zero) | Out-Null   # WM_KEYDOWN VK_DOWN
    [NativeWin]::PostMessage($lv, 0x0101, [IntPtr]0x28, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 300
    [NativeWin]::PostMessage($lv, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero) | Out-Null   # WM_KEYDOWN VK_RETURN
    [NativeWin]::PostMessage($lv, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 3

    $diffWin = [NativeWin]::FindWindowContaining([uint32]$p.Id, "modified")
    if ($diffWin -eq [IntPtr]::Zero) {
        Write-Output "diff window not found; all visible windows of the process:"
        foreach ($w in [NativeWin]::AllOfPid([uint32]$p.Id)) { Write-Output ("  " + $w) }
    } else {
        Write-Output ("diff window: " + [NativeWin]::WinTitle($diffWin))
        [NativeWin]::ShowWindow($diffWin, 9) | Out-Null
        [NativeWin]::SetForegroundWindow($diffWin) | Out-Null
        Start-Sleep -Milliseconds 900
        $shot2 = Join-Path $ShotDir 'shot-native-diff.png'
        [NativeWin]::Shoot($diffWin, $shot2)
        Write-Output ("screenshot 2: " + $shot2)
        foreach ($c in [NativeWin]::Children($diffWin)) { Write-Output ("  diffchild: " + $c) }
    }
} else {
    Write-Output "no ListView found"
}
