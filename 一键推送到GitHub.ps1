# 一键把本仓库推到 GitHub（需要你自己有一个 GitHub 账号）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\一键推送到GitHub.ps1
#   或直接给参数：
#   powershell -ExecutionPolicy Bypass -File .\一键推送到GitHub.ps1 -RepoUrl https://github.com/你的用户名/仓库名.git
#
# 说明：
#   · 仓库要先在 GitHub 网页上建好（空仓库，不要勾 README），或者用 -Create 让脚本调用 API 帮你建
#   · 推送时 GitHub 不再接受密码，要用 Personal Access Token（PAT）：
#       GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens
#       权限给 Contents: Read and write（Private repositories 还要给 Metadata: Read）
#   · token 不会写进 .git/config：只在这一次 push 的 URL 里临时用一下，推完会把 remote 还原成不带 token 的地址

param(
    [string]$RepoUrl,
    [string]$Token,
    [string]$UserName = "三年前的我",
    [string]$UserEmail = "WjianBiao@users.noreply.github.com"
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
Set-Location $here

# ── 1) 本仓库的 git 身份（只写本地，不动你的全局配置）──────────────────
if (-not (Test-Path (Join-Path $here '.git'))) {
    git init | Out-Null
    "已初始化 git 仓库: $here"
}
git symbolic-ref HEAD refs/heads/main 2>$null
git config user.name  $UserName
git config user.email $UserEmail

# ── 2) 提交 ──────────────────────────────────────────────────────────
git add -A
$pending = (git status --porcelain | Measure-Object).Count
if ($pending -gt 0) {
    git commit -m "文件比较器：首个公开版本（原生版源码 + 网页版 + 官网页面，MIT 开源）" | Out-Null
    "已提交 $pending 个改动"
} else {
    "没有需要提交的改动"
}
"当前提交：" + (git log -1 --pretty=format:'%h %s')

# ── 3) 要仓库地址 ─────────────────────────────────────────────────────
if (-not $RepoUrl) {
    Write-Host ""
    Write-Host "请粘贴你的 GitHub 仓库地址（形如 https://github.com/你的用户名/folder-comparator.git）：" -ForegroundColor Cyan
    $RepoUrl = Read-Host
}
$RepoUrl = $RepoUrl.Trim()
if ($RepoUrl -notmatch '^https://github\.com/.+/.+?(\.git)?$') {
    throw "地址看起来不对：$RepoUrl（本脚本只支持 https 形式）"
}
if ($RepoUrl -notmatch '\.git$') { $RepoUrl += '.git' }

git remote remove origin 2>$null
git remote add origin $RepoUrl
"remote origin = $RepoUrl"

# ── 4) 推送（token 只在这一次的 URL 里用）──────────────────────────────
if (-not $Token) {
    Write-Host ""
    Write-Host "请粘贴你的 Personal Access Token（输入时不显示；直接回车则用系统凭据管理器弹窗）：" -ForegroundColor Cyan
    $sec = Read-Host -AsSecureString
    $Token = [System.Net.NetworkCredential]::new('', $sec).Password
}

if ($Token) {
    $withToken = $RepoUrl -replace '^https://', ("https://x-access-token:" + $Token + "@")
    try {
        git push $withToken main:main
        "推送成功 ?"
    } finally {
        git remote set-url origin $RepoUrl      # 立刻把 token 从配置里抹掉
    }
} else {
    git push -u origin main
    "推送成功 ?（走系统凭据管理器）"
}

git remote set-url origin $RepoUrl
Write-Host ""
Write-Host "推完了。接下来在 GitHub 网页上开启网页（一次性，免费）：" -ForegroundColor Green
Write-Host "  · Settings → Pages → Source 选「GitHub Actions」"
Write-Host "    然后访问：https://<你的用户名>.github.io/folder-comparator/"
Write-Host "  · 想绑自己的域名：Pages → Custom domain 填域名，再去域名商加 DNS（可选）"
Write-Host "  · 也可在 Releases 里把 docs\文件比较器.zip 传一份当附件，方便别人下载"
Write-Host ""
Write-Host "提示：本机 hosts 把 github.com 指向了 127.0.0.1（Steam++ 反代），" -ForegroundColor Yellow
Write-Host "      如果 push 报 SSL/证书错误，先关掉 Steam++ 加速，或临时注释掉 hosts 里那几行 github。" -ForegroundColor Yellow
