# 把「文件比较器.html」做成官网页面 —— 页面本体就是 Win10-11 版：
#   · 直接复用 exe 里那段注入脚本（src\Program.cs 的 UiScript），所以四个胶囊、
#     忽略规则面板、历史记录面板、深/浅色主题、灵动岛动效，网页上和 exe 里完全一致；
#   · 没有 WebView2 宿主时脚本自己会降级：「窗口置顶」不出现，历史记录改存 localStorage；
#   · 工具本体（原 html）一行不改，只在两处插入：<head> 里的卡片样式、</body> 前的注入脚本；
#     另外在工具下面插一张卡片：完整版下载 + 联系作者。
#   · 下载的是**压缩包**「文件比较器.zip」（由 make-package.ps1 生成）：
#     包里是 文件比较器.exe + 请先读我.txt + 使用说明.txt。
#     走压缩包是为了绕开杀软的"下载保护"直拦 exe，包里那份「请先读我」专门讲误报怎么处理。
# 用法: make-site.ps1 [-Source <原始html>] [-Out <官网html>] [-Prog <Program.cs>] [-ZipName <zip>]
param(
    [string]$Source    = "$PSScriptRoot\web\文件比较器.html",
    [string]$Out       = "$PSScriptRoot\docs\index.html",
    [string]$Prog      = "$PSScriptRoot\src\Program.cs",
    [string]$ZipName   = "文件比较器.zip",
    [string]$AuthorUrl = "https://b23.tv/7ojZmWb"
)
$ErrorActionPreference = 'Stop'
$NL = "`r`n"

foreach ($f in @($Source, $Prog)) { if (-not (Test-Path -LiteralPath $f)) { throw "找不到: $f" } }

# ── 1) 从 Program.cs 里抠出 UiScript（verbatim 字符串，"" 要还原成 "）──────────
$cs = [System.IO.File]::ReadAllText($Prog, [System.Text.Encoding]::UTF8)
$marker = 'private const string UiScript = @"'
$i = $cs.IndexOf($marker)
if ($i -lt 0) { throw "在 $Prog 里找不到 UiScript 常量" }
$body = $cs.Substring($i + $marker.Length)
$j = $body.IndexOf('";')
if ($j -lt 0) { throw "UiScript 没有正常结束" }
$js = $body.Substring(0, $j).Replace('""', '"')
if ($js.Contains('</script>')) { throw "注入脚本里含 </script>，不能直接内联" }
if ($js -match 'chrome\.webview\.(postMessage\s*\(|addEventListener\s*\()') {
    throw "注入脚本里还有裸的 chrome.webview 调用，应该走 HOST"
}
if (-not $js.Contains('var HOST =')) { throw "注入脚本里没有 HOST 降级逻辑" }
Write-Output ("UiScript: {0:N0} 字符" -f $js.Length)

# ── 2) 读原文，做三处插入 ────────────────────────────────────────────────
$html = [System.IO.File]::ReadAllText($Source, [System.Text.Encoding]::UTF8)
if ($html.Contains('id="siteFooter"')) { throw "源文件里已经有官网区块了: $Source" }
if (-not $html.Contains('</head>')) { throw "源文件里找不到 </head>" }
if (-not $html.Contains('<!-- Diff Modal -->')) { throw "源文件里找不到 Diff Modal 锚点" }
if (-not $html.Contains('</body>')) { throw "源文件里找不到 </body>" }

$zipPath = Join-Path (Split-Path $Out -Parent) $ZipName
if (-not (Test-Path -LiteralPath $zipPath)) {
    throw ("下载用的压缩包不存在: " + $zipPath + "`r`n先跑一次 make-package.ps1 生成它。")
}
$sizeTxt = "{0:N0} KB" -f ((Get-Item -LiteralPath $zipPath).Length / 1KB)

$css = @'
        /* ── 官网区块：完整版下载 + 联系作者（注入，不属于原工具界面）────── */
        .site-footer { max-width: 940px; margin: 4px auto 56px; padding: 0 20px; }
        .site-card {
            background: var(--card-bg); border: 1px solid var(--border);
            border-radius: var(--radius); box-shadow: var(--shadow);
            padding: 26px 26px 22px; text-align: center;
        }
        .site-badge {
            display: inline-block; font-size: 12px; padding: 3px 12px; border-radius: 999px;
            background: var(--primary-light); color: var(--primary); margin-bottom: 12px;
        }
        .site-title { margin: 0 0 8px; font-size: 20px; font-weight: 600; color: var(--text); }
        .site-sub {
            margin: 0 auto 18px; max-width: 600px;
            font-size: 13.5px; line-height: 1.75; color: var(--text-secondary);
        }
        .site-sub b { color: var(--text); font-weight: 600; }
        .site-feats {
            list-style: none; margin: 0 auto 22px; padding: 0; max-width: 620px;
            display: grid; grid-template-columns: 1fr 1fr; gap: 8px 20px; text-align: left;
        }
        .site-feats li {
            position: relative; padding-left: 22px;
            font-size: 13px; line-height: 1.6; color: var(--text-secondary);
        }
        .site-feats li::before {
            content: '✓'; position: absolute; left: 0; top: 0;
            color: var(--success); font-weight: 700;
        }
        .site-actions { display: flex; gap: 12px; justify-content: center; flex-wrap: wrap; }
        .site-btn {
            display: inline-flex; align-items: center; gap: 8px;
            padding: 11px 22px; border-radius: var(--radius-sm);
            font-size: 14px; font-weight: 500; text-decoration: none;
            transition: transform var(--transition), background var(--transition),
                color var(--transition), border-color var(--transition), box-shadow var(--transition);
        }
        .site-btn:hover { transform: translateY(-1.5px); }
        .site-btn-primary {
            background: var(--primary); color: #fff;
            box-shadow: 0 4px 14px rgba(74, 108, 247, 0.28);
        }
        .site-btn-primary:hover { background: var(--primary-hover); }
        .site-btn-ghost { background: transparent; color: var(--text); border: 1px solid var(--border); }
        .site-btn-ghost:hover { border-color: var(--primary); color: var(--primary); }
        .site-size { font-size: 12px; opacity: 0.88; }
        .site-note { margin: 18px 0 0; font-size: 12px; line-height: 1.7; color: var(--text-secondary); }
        @media (max-width: 640px) { .site-feats { grid-template-columns: 1fr; } }
'@

$section = @'
    <!-- ================= 官网区块：完整版下载 + 联系作者 ================= -->
    <section class="site-footer" id="siteFooter">
        <div class="site-card">
            <div class="site-badge">离线完整版</div>
            <h2 class="site-title">文件比较器 完整版</h2>
            <p class="site-sub">
                上面的 <b>网页版</b> 临时用可以，如果在断网情况下建议使用完整版。<br>
                完整版是同一套功能的<b>离线原生程序</b>--<b>兼容winXP以上系统</b>
                （如果是XP 需要系统启用 .NET 3.5）。
            </p>
            <div class="site-actions">
                <a class="site-btn site-btn-primary" id="btnDownloadFull" href="__EXE__" download="__DLNAME__">
                    <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor"
                         stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                        <polyline points="7 10 12 15 17 10"></polyline>
                        <line x1="12" y1="15" x2="12" y2="3"></line>
                    </svg>
                    下载完整版
                    <span class="site-size">__SIZE__</span>
                </a>
                <a class="site-btn site-btn-ghost" id="btnContactAuthor" href="__AUTHOR__"
                   target="_blank" rel="noopener noreferrer">
                    <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor"
                         stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"></path>
                    </svg>
                    联系作者
                </a>
            </div>
            <p class="site-note">
                下载的是一个<b>压缩包</b>，里面有 <b>文件比较器.exe</b>、<b>请先读我.txt</b> 和
                <b>使用说明.txt</b>，解压后用 exe 即可（不用安装）。<br>
                没有数字签名的个人作品有时会被杀毒软件误报 ——
                <b>压缩包里的「请先读我」写了怎么处理</b>（一般是右键属性里「解除锁定」，或加进白名单）。
            </p>
        </div>
    </section>
'@
$section = $section.Replace('__EXE__', $ZipName).Replace('__SIZE__', $sizeTxt).
    Replace('__AUTHOR__', $AuthorUrl).Replace('__DLNAME__', $ZipName)

$styleTag = '    <style id="fdSiteCss">' + $NL + $css + $NL + '    </style>' + $NL

$scriptTag = '    <!-- ===== Win10-11 版注入脚本（与 exe 里跑的是同一段源码，由 make-site.ps1 抽取）===== -->' + $NL +
             '    <!-- 没有 WebView2 宿主时：不出现「窗口置顶」，历史记录改存 localStorage。 -->' + $NL +
             '    <script>' + $NL + $js + '    </script>' + $NL

$html = $html.Replace('</head>', $styleTag + '</head>')
$html = $html.Replace('    <!-- Diff Modal -->', $section + $NL + '    <!-- Diff Modal -->')
$html = $html.Replace('</body>', $scriptTag + '</body>')

[System.IO.File]::WriteAllText($Out, $html, (New-Object System.Text.UTF8Encoding($true)))

Write-Output ("site written: {0}  ({1:N0} bytes)" -f $Out, (Get-Item -LiteralPath $Out).Length)
Write-Output ("  page     = Win10-11 版（注入脚本 {0:N0} 字符已内联）" -f $js.Length)
Write-Output ("  download -> {0}  ({1})" -f $ZipName, $sizeTxt)
Write-Output ("  contact  -> {0}" -f $AuthorUrl)
