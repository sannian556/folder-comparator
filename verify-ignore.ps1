# 忽略规则（R1）的原生版验收：启动 → 看按钮/信息栏 → 打开对话框 → 取消勾选一个预设 → 保存
# → 确认自动重扫重比、已忽略计数变化。ASCII 无关，用中文匹配控件文本（文件需 UTF-8 BOM）。
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$DirA,
    [Parameter(Mandatory = $true)][string]$DirB,
    [string]$ShotDir = "$PSScriptRoot\build-native"
)
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class IW {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

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
    public static List<string> Kids(IntPtr parent) {
        List<string> res = new List<string>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            res.Add(Cls(h) + "|" + Title(h));
            return true;
        }, IntPtr.Zero);
        return res;
    }
    public static IntPtr FindChild(IntPtr parent, string needle) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            string t = Title(h);
            if (t.Length > 0 && t.IndexOf(needle, StringComparison.Ordinal) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static IntPtr FindExact(IntPtr parent, string text) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            if (Title(h) == text) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
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

[void][IW]::SetProcessDPIAware()
Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

$p = Start-Process $Exe -ArgumentList @("`"$DirA`"", "`"$DirB`"") -PassThru
Start-Sleep -Seconds 6
$p.Refresh()
if ($p.HasExited) { throw "process exited early" }

$main = [IW]::TopLevel([uint32]$p.Id)[0]
[void][IW]::ShowWindow($main, 9)
[void][IW]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
[void][IW]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 800

Write-Output "=== 主窗口（初始，规则：三个预设全开 + node_modules/ + !keep.log）==="
foreach ($k in [IW]::Kids($main)) {
    $f = $k.Split('|')
    if ($f[1] -like '*个文件*' -or $f[1] -like '*忽略规则*' -or $f[1] -like '*对比完成*' -or $f[1] -like '*不一致*') {
        Write-Output ("  " + $f[1])
    }
}
$shot1 = Join-Path $ShotDir 'ignore-main.png'
[IW]::Shoot($main, $shot1)
Write-Output ("  截图: " + $shot1)

# 点「忽略规则…」
$btn = [IW]::FindChild($main, '忽略规则')
if ($btn -eq [IntPtr]::Zero) { throw "没找到「忽略规则」按钮" }
[void][IW]::PostMessage($btn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK（异步：模态窗口会阻塞同步发送）
Start-Sleep -Seconds 2

$dlg = [IntPtr]::Zero
foreach ($w in [IW]::TopLevel([uint32]$p.Id)) { if ([IW]::Title($w) -eq '忽略规则') { $dlg = $w } }
if ($dlg -eq [IntPtr]::Zero) { throw "没找到「忽略规则」对话框" }

Write-Output "=== 对话框内容 ==="
foreach ($k in [IW]::Kids($dlg)) {
    $f = $k.Split('|')
    if ($f[1].Length -gt 0) { Write-Output ("  [" + $f[0] + "] " + $f[1]) }
}
$shot2 = Join-Path $ShotDir 'ignore-dialog.png'
[IW]::Shoot($dlg, $shot2)
Write-Output ("  截图: " + $shot2)

# 取消勾选「系统与临时文件」→ 保存（应触发重扫重比，已忽略的文件数下降）
$temp = [IW]::FindChild($dlg, '系统与临时文件')
if ($temp -eq [IntPtr]::Zero) { throw "没找到「系统与临时文件」勾选框" }
[void][IW]::PostMessage($temp, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Milliseconds 600
Write-Output "  （已取消勾选「系统与临时文件」）"
$save = [IW]::FindExact($dlg, '保存')
if ($save -eq [IntPtr]::Zero) { throw "没找到「保存」按钮" }
[void][IW]::PostMessage($save, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 6

Write-Output "=== 保存后（应自动重扫重比）==="
foreach ($k in [IW]::Kids($main)) {
    $f = $k.Split('|')
    if ($f[1] -like '*个文件*' -or $f[1] -like '*忽略规则*' -or $f[1] -like '*对比完成*') {
        Write-Output ("  " + $f[1])
    }
}
$shot3 = Join-Path $ShotDir 'ignore-after.png'
[IW]::Shoot($main, $shot3)
Write-Output ("  截图: " + $shot3)

Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
