# 打官网分发的压缩包：把 exe + 说明文档装进 文件比较器.zip，
# 并**自动生成一份「请先读我.txt」放在最前面** —— 讲清楚"可能被杀毒软件误报、
# 建议加白名单"。包内文件名都去掉版本后缀，解压出来就是「文件比较器.exe」。
# 用法: make-package.ps1 [-Exe <exe>] [-Manual <使用说明.txt>] [-Out <输出zip>]
param(
    [string]$Exe    = "$PSScriptRoot\build-native\文件比较器(XP-Win7版).exe",
    [string]$Manual = "$PSScriptRoot\使用说明.txt",
    [string]$Out    = "$PSScriptRoot\dist\文件比较器.zip"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$NL = "`r`n"

foreach ($f in @($Exe, $Manual)) { if (-not (Test-Path -LiteralPath $f)) { throw "找不到: $f" } }

$exeName = "文件比较器.exe"
$readme  = "请先读我.txt"
$manName = "使用说明.txt"

# ── 1) 「请先读我.txt」正文 ────────────────────────────────────────────
# 这一版是用户自己改过的：去掉了"压缩包里有什么"和"联系作者"，开头直奔杀毒软件误报。
# 保留原样，只把"三招"改成"两步"（他删掉了"解除锁定"那一步，但没改计数）。
$readmeText = @'
一、杀毒软件可能报毒 —— 请放心，这是误报
    本程序没有买数字签名证书（个人作品），从网上下载的，
    所以有可能被 360 / 火绒 / 电脑管家 / Windows Defender 拦下，
    或提示"未知程序、建议阻止"。典型提示名长得像
      Trojan:Win32/Wacatac.B!ml 、 HEUR/QVM202.xxx 、 Win32/Trojan.xxx
    这类带 HEUR / QVM / Gen / !ml 字样的，基本都是启发式误报。

    本程序的行为很干净：
      · 不联网、不下载任何东西
      · 不写注册表启动项、不加开机自启
      · 只在自己目录里读写：%APPDATA%\文件比较器\（设置和历史记录）
      · 不留后台进程，关掉窗口就结束

    三步解决，按顺序试：
      1) 右键这个 exe → 属性 → 最下面勾上「解除锁定 (Unblock)」→ 确定。
         （这一步最有效：它去掉"来自网络"的标记，很多杀软就是靠这个标记直接拦的。）
      2) 加进杀毒软件的白名单 / 信任区：
           · 360 安全卫士：木马查杀 → 右下角「信任区」→ 添加文件
           · Windows Defender：设置 → 隐私和安全性 → 病毒和威胁防护
             → 排除项 → 添加文件
           · 火绒：设置 → 信任区
           · 腾讯电脑管家：设置 → 病毒查杀 → 信任区
      3) 如果是"下载保护 / 实时防护"弹窗，选「允许本次运行 / 信任此文件」。

二、运行环境
    · Windows XP SP3 及以上都能跑（XP 需要系统已启用 .NET Framework 3.5）
    · Windows 7 / 8 / 10 / 11 一般都自带所需的 .NET，双击就能用
    · 若提示缺少 .NET Framework：
      控制面板 → 程序和功能 → 启用或关闭 Windows 功能
      → 勾选「.NET Framework 3.5（包括 .NET 2.0 和 3.0）」→ 确定

三、怎么用
    选两个文件夹 → 点「开始对比」→ 看四类结果
    （内容不同 / 仅在 A 中 / 仅在 B 中 / 完全相同）
    → 需要时点「导出差异文件 (ZIP)」把差异打包保存。
    更详细的说明见同目录的「使用说明.txt」。
'@

$readmeText = ($readmeText -replace "`r`n", "`n") -replace "`n", $NL

# ── 2) 搭一个临时目录，装进去再打包 ────────────────────────────────────
$stage = Join-Path $env:TEMP ("fd-pack-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath $Exe -Destination (Join-Path $stage $exeName) -Force
    Copy-Item -LiteralPath $Manual -Destination (Join-Path $stage $manName) -Force
    $utf8bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText((Join-Path $stage $readme), $readmeText, $utf8bom)

    if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stage, $Out, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    # ── 3) 回读校验：条目名单必须正好是这三个 ──────────────────────────
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Out)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        $want = @($exeName, $readme, $manName)
        foreach ($w in $want) {
            if ($names -notcontains $w) { throw ("压缩包里缺少 " + $w) }
        }
        $extra = @($names | Where-Object { $want -notcontains $_ })
        if ($extra.Count -gt 0) { throw ("压缩包里有计划外的文件: " + ($extra -join ', ')) }
    }
    finally { $zip.Dispose() }

    $fi = Get-Item -LiteralPath $Out
    Write-Output ("package : {0}" -f $fi.FullName)
    Write-Output ("  size  : {0:N0} bytes  ({1:N0} KB)" -f $fi.Length, ($fi.Length / 1KB))
    Write-Output ("  exe   : {0:N0} bytes -> 包内名 {1}" -f (Get-Item $Exe).Length, $exeName)
    Write-Output ("  files : {0}" -f ($want -join ' / '))
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
