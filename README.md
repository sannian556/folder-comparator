# 文件比较器（Folder Comparator）

比较两个文件夹，找出 **内容不同 / 仅在 A 中 / 仅在 B 中 / 完全相同** 的文件，能逐行查看文本差异，并把手上的差异**导出成一个 ZIP**。

- **桌面版**：单文件绿色 exe，**Windows XP SP3 ~ 11 全兼容**，不需要安装、不需要联网、不写注册表启动项
- **在线网页版**：GitHub Pages 免费托管，打开就是工具本体（不用装任何东西）
- **开源协议**：MIT（随便用、随便改、可商用，保留版权声明即可）

---

## 一、用哪个

| 方式 | 地址 / 说明 |
|---|---|
| **在线使用**（最省事） | `https://<你的用户名>.github.io/folder-comparator/` —— 页面上半部分就是工具本体，页内还有下载按钮 |
| **下载完整版** | [`docs/文件比较器.zip`](docs/文件比较器.zip)（约 83 KB），解压后双击 `文件比较器.exe` |
| **自己编译** | 见下方「从源码构建」，只需要系统自带的 .NET Framework，不用装 SDK |

> **第一次部署时**：仓库 → Settings → Pages → **Source 选「GitHub Actions」**（一次性设置）。
> 工作流已经写好，推上去就会自动发布，地址就是 `https://<用户名>.github.io/<仓库名>/`。
> 想换成自己的域名，在 Pages 里填 Custom domain，并在域名商加 DNS 即可（可选，不影响使用）。

压缩包里是三个文件：

```
文件比较器.exe      ← 主程序（单文件绿色版，XP ~ 11 通用）
请先读我.txt        ← 被杀毒软件误报时怎么办（请看这一份）
使用说明.txt        ← 完整使用说明
```

> **⚠️ 关于杀毒软件误报**：这是没有买数字签名的个人作品，从网上下载的 exe 有可能被 360 / 火绒 /
> 电脑管家 / Defender 拦下 —— **这是启发式误报**。处理办法写在包里的「请先读我.txt」，最有效的三步：
> ① 右键 exe → 属性 → 勾上「**解除锁定 (Unblock)**」；② 加进杀软的白名单 / 信任区；
> ③ 下载保护弹窗选「允许本次运行」。
>
> **还担心的话**：本程序不需要任何第三方运行库，你可以照「从源码构建」用**系统自带的 csc.exe
> 自己编一份** —— 自己编出来的 exe 没有“来自网络”标记，基本不会被误报。

---

## 二、功能

- **四类结果一目了然**：内容不同 / 仅在 A 中 / 仅在 B 中 / 完全相同，各自带计数与清单
- **文本逐行差异**：双击「内容不同」里的文本文件，左右对照看改动；`F3` / `F4` 跳到上/下一处改动
- **大文件不怕**：逐字节比对，没有大小上限
- **中文不乱码**：自动识别 UTF-8 / GBK，日志、txt 都能正常显示
- **忽略规则**：`.gitignore` 风格的规则（`*` `?` `**`、目录剪枝、`!` 例外），内置三档预设
  （版本控制目录 / 系统与临时文件 / 日志与备份），改完立刻重扫重比
- **历史记录**：**每跑完一次对比就自动记一条**（不用先导出），可还原当时的清单，
  也能按当前磁盘上的文件重新打包；导出过的条目会补记导出文件名
- **导出 ZIP**：把差异打成一个包，里面按 `only_in_A/`、`only_in_B/`、`modified/version_A|B/`
  分好目录，并附一份「对比说明.txt」
- **窗口置顶**、文件夹拖放、手动粘贴路径（支持 `.lnk` 快捷方式、`%环境变量%`）
- **命令行**：`文件比较器.exe <文件夹A> <文件夹B> [导出.zip] [--silent]`，方便批处理

---

## 三、运行环境

| 版本 | 需要什么 |
|---|---|
| `docs/文件比较器.zip` 里的 exe | **Windows XP SP3 及以上**。XP 需要系统已启用 .NET Framework 3.5；Win7/8/10/11 一般都自带 |
| 在线网页版 | 任意现代浏览器（Chrome / Edge / Firefox / 手机浏览器都行） |

如果提示缺少 .NET Framework：控制面板 → 程序和功能 → 启用或关闭 Windows 功能 →
勾选「.NET Framework 3.5（包括 .NET 2.0 和 3.0）」→ 确定。

---

## 四、从源码构建

源码只有一套风格（C#），**不依赖任何第三方库**：

```powershell
# 原生版（XP ~ 11 通用）：产出 build-native\文件比较器(XP-Win7版).exe 与 (Win8-11版).exe
powershell -ExecutionPolicy Bypass -File .\build-native.ps1

# 打包分发用的压缩包（exe + 请先读我.txt + 使用说明.txt）→ docs\文件比较器.zip
powershell -ExecutionPolicy Bypass -File .\make-package.ps1

# 生成官网页面（把注入脚本内联进页面 + 页内下载按钮）→ docs\index.html
powershell -ExecutionPolicy Bypass -File .\make-site.ps1
```

- 编译用的是**系统自带的 csc.exe**（.NET Framework 自带），不需要装 SDK、不需要 NuGet：
  - `C:\Windows\Microsoft.NET\Framework\v2.0.50727\csc.exe`（CLR 2.0，XP 能跑的那个）
  - `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`（CLR 4.0）
- 源码刻意只用 C# 2.0 语法（没有 LINQ / `var` / 自动属性），所以同一份 `native\*.cs`
  能被两代编译器分别编出两个目标。

网页版 exe（可选：把网页版装进 WebView2 宿主，需要先还原 WebView2 SDK）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SourceHtml .\web\文件比较器.html
```

---

## 五、目录结构

```
native\              原生版源码（WinForms，XP ~ 11）
src\Program.cs       网页版宿主 + 注入脚本（UiScript）
web\文件比较器.html   网页版工具本体（原页面，单文件、离线可用）
docs\                GitHub Pages 站点：index.html（官网页面）+ 文件比较器.zip（下载件）
.github\workflows\   推送后自动发布 Pages 的工作流
assets\              图标与两个前端库（离线内置，页面不依赖 CDN 也能用）
tests\               忽略规则引擎的单元测试
testdata\            验收用的两个文件夹夹具
build-native.ps1     构建原生版两个目标
build.ps1            构建网页版 exe
make-package.ps1     打分发压缩包
make-site.ps1        生成官网页面
verify-*.ps1         自动化验收脚本（真窗口 / 真浏览器，共 200+ 项断言）
shot-dpi.ps1         高 DPI 截图辅助
使用说明.txt          给用户的完整说明
README-dev.md        开发向的英文说明（构建与验收细节）
```

---

## 六、常见问题

**Q：为什么有两个 exe？**
`文件比较器(XP-Win7版).exe` 用 CLR 2.0 编译，XP 就能跑；`文件比较器(Win8-11版).exe` 用 CLR 4.0 编译，
体积略小。随包发布的那个（CLR 2.0）在新系统上照样跑，所以只发一个就够。

**Q：会被杀毒软件报毒吗？**
可能会（无数字签名的个人作品 + 全新哈希 + 无云信誉）。详见包里的「请先读我.txt」。
本程序不联网、不写启动项、只在 `%APPDATA%\文件比较器\` 读写自己的设置与历史记录。

**Q：网页版和桌面版有什么差别？**
网页版受浏览器限制：拿不到文件夹绝对路径、不能重读磁盘（历史记录因此是只读快照）、
超过 50 MB 的文件只比大小、中文只按 UTF-8 解码。要完整功能请用桌面版。

**Q：在线的网页访问不了 / 很慢？**
`github.io` 在国内网络下时通时断，属于网络环境问题，不是站点坏了。想稳定的话两个办法：
① 把 `web\文件比较器.html` 下载到本地双击打开（单文件、完全离线可用）；
② 给 Pages 绑一个自己的域名（Pages → Custom domain + 域名商加 DNS），必要时再套一层 CDN。

---

## 七、开源协议

[MIT](LICENSE) —— 你可以自由使用、修改、分发、商用，只需保留版权声明。
版权所有 © 2026 三年前的我。

## 八、联系作者

<https://b23.tv/7ojZmWb>（B 站）
