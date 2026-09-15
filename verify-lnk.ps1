# 验收 .lnk 快捷方式解析：把两个指向文件夹的快捷方式当命令行参数传给 exe，
# 让它走 NormalizeFolderPath → ResolveShortcutTarget，最后核对导出 ZIP 的内容对不对。
# （这段代码刚从 WScript.Shell + 反射改成直连 shell32 的 IShellLink，必须实测。）
# 用法: verify-lnk.ps1 [-Exe <原生版exe>]
param(
    [string]$Exe = "$PSScriptRoot\build-native\文件比较器(XP-Win7版).exe"
)
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP 'fd-lnk-test'
if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
$dirA = Join-Path $work '真文件夹A'
$dirB = Join-Path $work '真文件夹B'
New-Item -ItemType Directory -Force -Path $dirA, $dirB | Out-Null
Set-Content -Path (Join-Path $dirA 'same.txt')  -Value 'same'    -Encoding UTF8
Set-Content -Path (Join-Path $dirB 'same.txt')  -Value 'same'    -Encoding UTF8
Set-Content -Path (Join-Path $dirA 'diff.txt')  -Value 'AAA'     -Encoding UTF8
Set-Content -Path (Join-Path $dirB 'diff.txt')  -Value 'BBB'     -Encoding UTF8
Set-Content -Path (Join-Path $dirA 'onlyA.txt') -Value 'a only'  -Encoding UTF8

$lnkA = Join-Path $work '快捷方式A.lnk'
$lnkB = Join-Path $work '快捷方式B.lnk'
$sh = New-Object -ComObject WScript.Shell
foreach ($pair in @(@($lnkA, $dirA), @($lnkB, $dirB))) {
    $sc = $sh.CreateShortcut($pair[0])
    $sc.TargetPath = $pair[1]
    $sc.WorkingDirectory = $pair[1]
    $sc.Save()
}
$zip = Join-Path $work 'out.zip'

$pass = 0; $fail = 0
function Assert([string]$label, $expect, $got) {
    if ("$expect" -eq "$got") { $script:pass++; Write-Output ("  OK   " + $label); return }
    $script:fail++
    Write-Output ("FAIL  [" + $label + "] 期望 " + $expect + " 实际 " + $got)
}

Write-Output "=== 1) 快捷方式本身没问题 ==="
foreach ($l in @($lnkA, $lnkB)) {
    Assert ((Split-Path $l -Leaf) + " 已生成") $true (Test-Path -LiteralPath $l)
    Assert "  … 能被 WScript 读回目标" $true (((New-Object -ComObject WScript.Shell).CreateShortcut($l).TargetPath).Length -gt 0)
}

Write-Output "=== 2) 把 .lnk 当参数交给 exe（走的就是改过的那段解析）==="
$p = Start-Process $Exe -ArgumentList @("`"$lnkA`"", "`"$lnkB`"", '--silent', '--export', "`"$zip`"") -PassThru
if (-not $p.WaitForExit(60000)) { try { $p.Kill() } catch { }; throw "exe 60 秒没退出" }
Write-Output ("  退出码 = " + $p.ExitCode)
Assert "退出码 0（说明两个 .lnk 都解析成了真文件夹）" 0 $p.ExitCode
Assert "导出的 ZIP 已生成" $true (Test-Path -LiteralPath $zip)

Write-Output "=== 3) ZIP 内容（对着 A/B 的真实差异核对）==="
if (Test-Path -LiteralPath $zip) {
    $dest = Join-Path $work 'unzip'
    Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    $files = Get-ChildItem $dest -Recurse -File | ForEach-Object { $_.FullName.Substring($dest.Length).TrimStart('\').Replace('\', '/') }
    $files | Sort-Object | ForEach-Object { Write-Output ("  " + $_) }
    Assert "包里有 diff.txt（内容不同）" $true (($files -join '|') -like '*diff.txt*')
    Assert "包里有 onlyA.txt（仅在 A 中）" $true (($files -join '|') -like '*onlyA.txt*')
    Assert "same.txt 不该进包（完全相同）" $false (($files -join '|') -like '*same.txt*')
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Output ""
Write-Output ("总计：通过 " + $pass + " 项，失败 " + $fail + " 项")
if ($fail -gt 0) { exit 1 }
