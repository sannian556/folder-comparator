# R2 历史记录的原生版验收：导出生成历史 → 打开面板 → 还原快照 → 文件夹缺失时打包置灰 → 一键清理。
# 用法: verify-history.ps1 -Exe <exe> -DirA <A> -DirB <B>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$DirA,
    [Parameter(Mandatory = $true)][string]$DirB,
    [string]$Work = "$PSScriptRoot\build-native"
)
$ErrorActionPreference = 'Stop'
$hist = Join-Path $env:APPDATA '文件比较器\历史.dat'

Add-Type -ReferencedAssemblies System.Drawing, System.Windows.Forms @"
using System;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class HW {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
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
            res.Add(h.ToInt64() + "|" + Cls(h) + "|" + Title(h));
            return true;
        }, IntPtr.Zero);
        return res;
    }
    public static List<IntPtr> KidsRaw(IntPtr parent) {
        List<IntPtr> res = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { res.Add(h); return true; }, IntPtr.Zero);
        return res;
    }
    /// <summary>历史面板里的 DataGridView（行不是子窗口，只能整块点）。</summary>
    public static IntPtr FindGrid(IntPtr dlg) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(dlg, delegate(IntPtr h, IntPtr l) {
            if (Cls(h).IndexOf("WindowsForms10.Window.8") == 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    /// <summary>往格子里发一次左键单击（坐标是控件客户区像素）。</summary>
    public static void ClickCell(IntPtr h, int x, int y) {
        IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
        SendMessage(h, 0x0201, (IntPtr)1, lp);
        System.Threading.Thread.Sleep(50);
        SendMessage(h, 0x0202, IntPtr.Zero, lp);
    }
    public static IntPtr FindExact(IntPtr parent, string text) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            if (Title(h) == text) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
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

[void][HW]::SetProcessDPIAware()
$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; Write-Output ("  OK   " + $label); return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}
function Kill-App { Get-Process | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 700 }

# 单实例互斥体：别的副本（例如桌面/解压目录里那个）开着时，本脚本起的实例会立刻退出，
# 或卡在"已经在运行了"的提示框上 —— 直接失败退出，别让脚本挂到超时（踩过一次 5 分钟）。
$others = Get-Process | Where-Object { $_.Path -like '*文件比较器*' -and $_.Path -ne $Exe }
if ($others) {
    foreach ($o in $others) { Write-Output ("  另一个副本在运行: " + $o.Path) }
    throw "请先关掉上面那些「文件比较器」窗口再跑验收（单实例互斥体会让测试进程起不来）"
}

# ── 0) 干净起手：删历史，先跑三次交互式导出生成三条 ──────────
Kill-App
foreach ($f in @($hist, "$hist.bak", "$hist.bad")) { if (Test-Path $f) { Remove-Item $f -Force } }

Write-Output "=== 1) 交互式导出三次 → 应生成 3 条历史 ==="
foreach ($i in 1..3) {
    $z = Join-Path $Work ("hist-ui$i.zip")
    if (Test-Path $z) { Remove-Item $z -Force }
    $p = Start-Process $Exe -ArgumentList @("`"$DirA`"", "`"$DirB`"", "`"$z`"") -PassThru
    Start-Sleep -Seconds 12
    $p | Stop-Process -Force
    Start-Sleep -Milliseconds 900
}
$zip1 = Join-Path $Work 'hist-ui3.zip'
Assert "历史文件已生成" $true (Test-Path $hist)
Assert "zip 已生成" $true (Test-Path $zip1)
$dump = Join-Path $Work 'hist-ui-dump.txt'
Start-Process $Exe -ArgumentList @('--dump-history', "`"$dump`"") -Wait
$dumped = Get-Content $dump -Encoding UTF8 -Raw
Assert "历史里有 3 条" $true ($dumped -match '条数 = 3')
Assert "记录了 A 路径" $true ($dumped.IndexOf($DirA) -ge 0)
Assert "记录了导出路径" $true ($dumped.IndexOf($zip1) -ge 0)
Assert "清单有 7 条（3 不同 + 2 仅A + 2 仅B）" $true ($dumped -match '清单 = 7 条')

# ── 2) 打开历史面板 ─────────────────────────────────────────
Write-Output "=== 2) 主窗口 →「历史记录…」面板 ==="
Kill-App
$p = Start-Process $Exe -PassThru
Start-Sleep -Seconds 5
$main = [HW]::TopLevel([uint32]$p.Id)[0]
[void][HW]::ShowWindow($main, 9)
[void][HW]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
[void][HW]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 800
$btnHist = [HW]::FindExact($main, '历史记录…')
Assert "找得到「历史记录…」按钮" $true ($btnHist -ne [IntPtr]::Zero)
[void][HW]::PostMessage($btnHist, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2

$dlg = [IntPtr]::Zero
foreach ($w in [HW]::TopLevel([uint32]$p.Id)) { if ([HW]::Title($w) -eq '历史记录') { $dlg = $w } }
Assert "历史面板已打开" $true ($dlg -ne [IntPtr]::Zero)
$kids = [HW]::Kids($dlg)
Assert "面板显示条数与占用" $true (($kids | Where-Object { $_ -match '共 3 条 · 占用' }).Count -eq 1)
Assert "未勾选时按钮是「一键清理历史记录」" $true (($kids | Where-Object { $_ -match '\|一键清理历史记录$' }).Count -eq 1)
$shot1 = Join-Path $Work 'hist-panel.png'
[HW]::Shoot($dlg, $shot1)
Write-Output ("  截图: " + $shot1)

# ── 3) 还原快照 ─────────────────────────────────────────────
Write-Output "=== 3) 点「还原」→ 主界面应显示历史快照 ==="
$btnRestore = [HW]::FindExact($dlg, '还原')
Assert "找得到「还原」按钮" $true ($btnRestore -ne [IntPtr]::Zero)
[void][HW]::PostMessage($btnRestore, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2
$mainKids = [HW]::Kids($main)
$status = ($mainKids | Where-Object { $_ -match '\|历史记录 ' } | Select-Object -First 1)
Assert "状态栏显示历史记录说明" $true ($status -ne $null)
if ($status) { Write-Output ("  状态栏: " + ($status -split '\|')[2]) }
$nums = ($mainKids | Where-Object { $_ -match '\|WindowsForms10.STATIC.*\|[0-9]+$' } | ForEach-Object { ($_ -split '\|')[2] })
Assert "计数还原为 3/2/2/3" $true (($nums -contains '3') -and ($nums -contains '2'))
$btnExp = [HW]::FindExact($main, '导出差异文件 (ZIP)')
Assert "打包按钮可用（文件夹都在）" $true ([HW]::IsWindowEnabled($btnExp))

# ── 4) 文件夹缺失 → 提示 + 打包置灰，清单仍在 ────────────────
Write-Output "=== 4) 移走文件夹 B 后还原 → 应提示并禁用打包 ==="
Kill-App
$bakDir = $DirB + '-moved'
if (Test-Path $bakDir) { Remove-Item $bakDir -Recurse -Force }
Move-Item $DirB $bakDir
try {
    $p = Start-Process $Exe -PassThru
    Start-Sleep -Seconds 5
    $main = [HW]::TopLevel([uint32]$p.Id)[0]
    [void][HW]::ShowWindow($main, 9)
    [void][HW]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
    [void][HW]::SetForegroundWindow($main)
    Start-Sleep -Milliseconds 800
    $btnHist = [HW]::FindExact($main, '历史记录…')
    [void][HW]::PostMessage($btnHist, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Seconds 2
    $dlg = [IntPtr]::Zero
    foreach ($w in [HW]::TopLevel([uint32]$p.Id)) { if ([HW]::Title($w) -eq '历史记录') { $dlg = $w } }
    $btnRestore = [HW]::FindExact($dlg, '还原')
    [void][HW]::PostMessage($btnRestore, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Seconds 2

    $mainKids = [HW]::Kids($main)
    $status = (($mainKids | Where-Object { $_ -match '\|历史记录 ' } | Select-Object -First 1))
    $statusText = if ($status) { ($status -split '\|')[2] } else { '' }
    Assert "状态栏提示文件夹已被删除或已被移除" $true ($statusText -match '已被删除或已被移除')
    if ($statusText) { Write-Output ("  状态栏: " + $statusText) }
    $btnExp = [HW]::FindExact($main, '导出差异文件 (ZIP)')
    Assert "打包按钮已置灰" $false ([HW]::IsWindowEnabled($btnExp))
    # ListView 的行不是子窗口，改问 LVM_GETITEMCOUNT：能列出条目才算"清单仍可看"
    $lv = [IntPtr]::Zero
    foreach ($k in $mainKids) {
        if ($k -match '\|WindowsForms10\.SysListView32') { $lv = [IntPtr][int64](($k -split '\|')[0]); break }
    }
    $lvCount = if ($lv -ne [IntPtr]::Zero) { [HW]::SendMessage($lv, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64() } else { -1 }
    Assert "差异清单仍然看得见（内容不同 3 行）" 3 $lvCount
    $shot2 = Join-Path $Work 'hist-missing.png'
    [HW]::Shoot($main, $shot2)
    Write-Output ("  截图: " + $shot2)
} finally {
    Kill-App
    if (Test-Path $bakDir) { Move-Item $bakDir $DirB }
    Start-Sleep -Milliseconds 500
}

# ── 5) 勾选清理：整格都能打勾，只删勾上的（回归「打勾与一键清理逻辑错」） ──
Write-Output "=== 5) 打勾 2 条 → 只清理这 2 条（打勾整格可点）==="
$p = Start-Process $Exe -PassThru
Start-Sleep -Seconds 5
$main = [HW]::TopLevel([uint32]$p.Id)[0]
[void][HW]::ShowWindow($main, 9)
[void][HW]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
[void][HW]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 800
$btnHist = [HW]::FindExact($main, '历史记录…')
[void][HW]::PostMessage($btnHist, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2
$dlg = [IntPtr]::Zero
foreach ($w in [HW]::TopLevel([uint32]$p.Id)) { if ([HW]::Title($w) -eq '历史记录') { $dlg = $w } }
$grid = [HW]::FindGrid($dlg)
Assert "找得到历史列表控件" $true ($grid -ne [IntPtr]::Zero)

# 列宽 165/298/222/98/70（125% 缩放）→ 清理列 x 783..853；行高 32、表头 35
# 故意点在 x=795（离小方框很远，修复前这里完全没反应）
[HW]::ClickCell($grid, 795, 51)
Start-Sleep -Milliseconds 350
$t1 = ([HW]::Kids($dlg) | Where-Object { $_ -match '\|清理选中的 (\d+) 条$' })
Assert "点格子左侧也能打上勾（按钮变「清理选中的 1 条」）" $true ($t1.Count -eq 1)
[HW]::ClickCell($grid, 800, 117)
Start-Sleep -Milliseconds 350
$t2 = ([HW]::Kids($dlg) | Where-Object { $_ -match '\|清理选中的 2 条$' })
Assert "第 3 行也勾上（按钮变「清理选中的 2 条」）" $true ($t2.Count -eq 1)
$shot3 = Join-Path $Work 'hist-checked.png'
[HW]::Shoot($dlg, $shot3)
Write-Output ("  截图: " + $shot3)

$btnClean = [HW]::FindExact($dlg, '清理选中的 2 条')
[void][HW]::PostMessage($btnClean, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2
$confirm = [IntPtr]::Zero
foreach ($w in [HW]::TopLevel([uint32]$p.Id)) {
    if ([HW]::Cls($w) -eq '#32770' -and $w -ne $dlg) { $confirm = $w }
}
Assert "弹出了二次确认框" $true ($confirm -ne [IntPtr]::Zero)
if ($confirm -ne [IntPtr]::Zero) {
    $txt = ([HW]::Kids($confirm) | Where-Object { $_ -match '清理选中的 2 条' })
    Assert "确认框说的是「清理选中的 2 条」而不是清空全部" $true ($txt.Count -ge 1)
    $ok = [HW]::FindExact($confirm, '确定')
    if ($ok -eq [IntPtr]::Zero) { $ok = [HW]::FindExact($confirm, 'OK') }
    [void][HW]::PostMessage($ok, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Seconds 2
}
$kids = [HW]::Kids($dlg)
Assert "面板只剩 1 条" $true (($kids | Where-Object { $_ -match '共 1 条 · 占用' }).Count -eq 1)
Assert "按钮回到「一键清理历史记录」" $true (($kids | Where-Object { $_ -match '\|一键清理历史记录$' }).Count -eq 1)
Kill-App
$dump2 = Join-Path $Work 'hist-ui-dump2.txt'
Start-Process $Exe -ArgumentList @('--dump-history', "`"$dump2`"") -Wait
$d2 = Get-Content $dump2 -Encoding UTF8 -Raw
# 三条从新到旧 = [0] ui3、[1] ui2、[2] ui1；勾的是第 1、3 行 → 留下的应该是中间那条 ui2
Assert "留下的正好是没勾的那条（hist-ui2）" $true ($d2.IndexOf('hist-ui2.zip') -ge 0)
Assert "勾掉的 ui3 已被删除" $false ($d2.IndexOf('hist-ui3.zip') -ge 0)
Assert "勾掉的 ui1 已被删除" $false ($d2.IndexOf('hist-ui1.zip') -ge 0)

# ── 6) 列头全选 + 一键清空（未勾选 → 清空全部，需二次确认） ────
Write-Output "=== 6) 点列头全选 / 一键清理历史记录（未勾选 → 清空全部）==="
$p = Start-Process $Exe -PassThru
Start-Sleep -Seconds 5
$main = [HW]::TopLevel([uint32]$p.Id)[0]
[void][HW]::ShowWindow($main, 9)
[void][HW]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
[void][HW]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 800
$btnHist = [HW]::FindExact($main, '历史记录…')
[void][HW]::PostMessage($btnHist, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2
$dlg = [IntPtr]::Zero
foreach ($w in [HW]::TopLevel([uint32]$p.Id)) { if ([HW]::Title($w) -eq '历史记录') { $dlg = $w } }
$grid = [HW]::FindGrid($dlg)
[HW]::ClickCell($grid, 818, 17)          # 表头那一行的「清理」列
Start-Sleep -Milliseconds 400
Assert "点列头 = 全选（按钮变「清理选中的 1 条」）" $true ((([HW]::Kids($dlg)) | Where-Object { $_ -match '\|清理选中的 1 条$' }).Count -eq 1)
[HW]::ClickCell($grid, 818, 17)
Start-Sleep -Milliseconds 400
Assert "再点列头 = 全不选（按钮回到一键清理）" $true ((([HW]::Kids($dlg)) | Where-Object { $_ -match '\|一键清理历史记录$' }).Count -eq 1)

$btnClean = [HW]::FindExact($dlg, '一键清理历史记录')
[void][HW]::PostMessage($btnClean, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 2

$confirm = [IntPtr]::Zero
foreach ($w in [HW]::TopLevel([uint32]$p.Id)) {
    if ([HW]::Cls($w) -eq '#32770' -and $w -ne $dlg) { $confirm = $w }
}
Assert "弹出了二次确认框" $true ($confirm -ne [IntPtr]::Zero)
if ($confirm -ne [IntPtr]::Zero) {
    $txt = ([HW]::Kids($confirm) | Where-Object { $_ -match '清空全部历史记录' })
    Assert "确认框提到清空全部" $true ($txt.Count -ge 1)
    $ok = [HW]::FindExact($confirm, '确定')
    if ($ok -eq [IntPtr]::Zero) { $ok = [HW]::FindExact($confirm, 'OK') }
    [void][HW]::PostMessage($ok, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Seconds 2
}
$kids = [HW]::Kids($dlg)
Assert "面板变成空状态" $true (($kids | Where-Object { $_ -match '还没有历史记录' }).Count -eq 1)
Kill-App
Assert "历史文件里的条目已清空" $true ((Get-Item $hist).Length -lt 200)

# ── 7) 只比对、不导出 → 也要记一条（用户要求：历史在「开始比对」后产生） ──
Write-Output "=== 7) 只对比不导出：历史照样产生（ZIP 显示未导出）==="
$p = Start-Process $Exe -ArgumentList @("`"$DirA`"", "`"$DirB`"") -PassThru   # 两个目录、不给导出路径
Start-Sleep -Seconds 12
Kill-App
$dump3 = Join-Path $Work 'hist-ui-dump3.txt'
Start-Process $Exe -ArgumentList @('--dump-history', "`"$dump3`"") -Wait
$d3 = Get-Content $dump3 -Encoding UTF8 -Raw
Assert "一次纯对比就记了 1 条" $true ($d3 -match '条数 = 1')
Assert "这条没有导出路径（显示未导出）" $true ($d3 -match 'ZIP = \(未导出\)')
Assert "清单完整（3 不同 + 2 仅A + 2 仅B）" $true ($d3 -match '清单 = 7 条')
Assert "记的还是那两个文件夹" $true (($d3.IndexOf($DirA) -ge 0) -and ($d3.IndexOf($DirB) -ge 0))

Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }
