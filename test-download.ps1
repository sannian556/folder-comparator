# End-to-end check of the export path: triggers a blob download inside the page
# via CDP and looks for the native "save as" dialog the host is supposed to raise.
# ASCII-only source.
param(
    [int]$Port = 9222,
    [string]$ShotOut = "$PSScriptRoot\build\shot-saveas.png"
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinEnum {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    public static List<string> Dump(uint wantPid) {
        List<string> res = new List<string>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != wantPid) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512);
            StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256);
            RECT r; GetWindowRect(h, out r);
            res.Add(h.ToInt64() + "|" + c.ToString() + "|" + t.ToString() + "|" + r.Left + "," + r.Top + "," + (r.Right - r.Left) + "," + (r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return res;
    }
}
"@

$proc = Get-Process -Name FolderDiff -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "app is not running" }
$pidApp = [uint32]$proc.Id

$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 10
$page = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait()

$expr = "saveAs(new Blob(['folder-diff export probe'], {type:'application/zip'}), 'diff-export-test.zip'); 'download-triggered';"
$msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true } } | ConvertTo-Json -Depth 8 -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($msg)
$ws.SendAsync([System.ArraySegment[byte]]::new($bytes, 0, $bytes.Length), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait()
$buf = New-Object byte[] 65536
$res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf, 0, $buf.Length), $ct).Result
Write-Output ("evaluate: " + [System.Text.Encoding]::UTF8.GetString($buf, 0, $res.Count))
$ws.Dispose()

Start-Sleep -Seconds 4

$wins = [WinEnum]::Dump($pidApp)
Write-Output "=== visible top-level windows of the app process ==="
$dlg = $null
foreach ($w in $wins) {
    Write-Output $w
    $parts = $w.Split('|')
    if ($parts[1] -eq '#32770') { $dlg = $parts }
}

if (-not $dlg) {
    Write-Output "RESULT: no native dialog window found"
    exit 0
}

$hwnd = [IntPtr][int64]$dlg[0]
$rect = $dlg[3].Split(',')
$x = [int]$rect[0]; $y = [int]$rect[1]; $w = [int]$rect[2]; $h = [int]$rect[3]
[WinEnum]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 900

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$bmp.Save($ShotOut, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output ("RESULT: dialog found, title='{0}', screenshot={1}" -f $dlg[2], $ShotOut)

# dismiss the modal dialog so the app stays usable
[WinEnum]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 1
Write-Output "dialog dismissed"
