# Folder Compare Tool - build project

Turns the single-file web page `D:\桌面\文件比较器.html` ("folder comparator")
into standalone Windows executables. Two independent approaches live here;
**the native one is the current deliverable** because it is the only one that
can run on Windows XP.

> **Where the shipped files live (from 2026-09-15)**: everything that is meant to
> be handed to users sits in `..\文件比较器成品\` — `文件比较器-官网.html`,
> `文件比较器.zip`, `文件比较器(XP-Win7版).exe`, `文件比较器(Win10-11版).exe`,
> `文件比较器-使用说明.txt`. The default paths of `make-package.ps1`,
> `make-site.ps1`, `verify-contact.ps1`, `verify-site.ps1`, `verify-lnk.ps1` and
> `shot-dpi.ps1` all point there. The desktop now only holds the user's original
> `文件比较器.html`, which is never modified.

---

## 1. Native rewrite — delivered (`native/`)

A plain C# / WinForms program. **No browser engine, no runtime to install beyond
.NET itself, no side files.** Same feature set as the web page, implemented
against the real file system (so it is also noticeably faster).

| Deliverable | Target framework | Runs on |
|---|---|---|
| `文件比较器(Win8-11版).exe` (95 KB) | CLR 4.0 | Windows 8 / 8.1 / 10 / 11 out of the box; Windows 7 with .NET Framework 4.0+ |
| `文件比较器(XP-Win7版).exe` (108 KB) | CLR 2.0 | **Windows XP SP3** / Vista / Windows 7 with .NET Framework 2.0 or 3.5 |

Both are built from the *same* source; only the compiler and the referenced
framework assemblies differ. Verified at runtime: the CLR 2.0 build loads
`mscorwks.dll` (v2.0.50727), the CLR 4.0 build loads `clr.dll` (v4.0.30319).

Windows XP was the last Windows to support .NET 4.0, so the CLR 2.0 build is the
one to hand to an XP machine; the CLR 4.0 build is the one for a modern machine,
where .NET 4.x is already part of the OS.

### Features

* Pick two folders, recursive scan, compare by relative path (root folder name excluded)
* Size compare first, then **streaming byte-by-byte compare** — no 50 MB ceiling
  like the web version, memory use is one buffer regardless of file size
* Four result groups: content differs / only in B / only in A / identical
* Per-row detail: size, `A size → B size`, "same size, different content"
* Double-click (or Enter) a differing text file → line-by-line diff window with
  LCS algorithm, 2 lines of context, collapsed identical runs, red/green rows,
  `F3`/`F4` (or Ctrl+N / Ctrl+P) to jump between changes
* Right-click menu: view diff, reveal in Explorer, open A/B version, copy relative path
* Export differences to a ZIP: `only_in_A/`, `only_in_B/`, `modified/version_A/`,
  `modified/version_B/`, plus a `对比说明.txt` summary
* Text encoding detection (BOM → strict UTF-8 → GBK fallback) so Chinese `.txt`/`.log`
  files do not turn into mojibake
* Cancel button for long scans/compares/exports, progress bar and status text
* Drag two folders onto the window (or onto the exe, or into a path box) to compare immediately
* Always-on-top toggle (checkbox next to the action buttons, or Ctrl+T)
* Dark / light theme switch (web build: pill button at the top-right of the page; it
  follows the system theme until you pick one, then remembers your choice)
* Ignore rules / filters (both builds): three presets (VCS dirs, temp/system files, logs
  and backups) plus custom glob lines. Semantics: `*` `?` (no `/` crossing), `**` across
  levels, trailing `/` = directories only (the whole subtree is pruned), a leading `!` is
  an exception, `#` starts a comment, matching is case-insensitive and also tests every
  parent directory. Ignored files never enter the result lists, the counts, or the ZIP.
  Rules are remembered; the button shows how many are active and the folder line shows
  how many were skipped. Presets default to OFF (no silent behaviour change).
* History: every successful ZIP export records an entry (time, both folders, the four counts,
  and the full list of differing / only-in-A / only-in-B files — `identical` is count-only).
  The native build can **restore the snapshot** to the result view and **re-pack the ZIP from
  the current disk** (noting "N files differ from the record"); if a folder is gone it says so
  and greys out the export button while the list stays readable. Delete = tick rows + a
  bottom button ("clean selected N" / "clean everything") with a second confirmation.
  Entries are append-only, capped at 20 / 5 MB (`%APPDATA%\文件比较器\历史.dat`).
  The WebView2 build can only **view and clean** history: Chromium never exposes absolute
  paths and the page cannot re-read the disk, so a re-pack there is impossible by design.
* Single instance; window layout adapts to the screen

### Keyboard

| Key | Action |
|---|---|
| Ctrl+Enter / F5 | start comparison |
| Ctrl+E | export differences to ZIP |
| Ctrl+T | toggle "always on top" |
| Enter / double-click | open the line diff of the selected file |
| F3 / Ctrl+N, F4 / Ctrl+P | next / previous change in the diff window |
| Esc | close the diff window |

### Command line

```
文件比较器(Win8-11版).exe "文件夹A" "文件夹B"                    # open and compare right away
文件比较器(Win8-11版).exe "文件夹A" "文件夹B" out.zip            # compare, then export automatically
文件比较器(Win8-11版).exe "文件夹A" "文件夹B" out.zip --silent   # ... and exit (batch / scheduled task)
```

`--silent` is useful for Task Scheduler: the exit code is 0 on success.

### Build

```powershell
powershell -ExecutionPolicy Bypass -File build-native.ps1
```

Needs only what ships with Windows: `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`
for the CLR 4.0 target and `...\v2.0.50727\csc.exe` for the CLR 2.0 target.
No SDK, no npm, no NuGet, no internet.

Sources are deliberately written in C# 2.0 style (no LINQ, no `Func`/`Action`,
no extension methods, no `HashSet`) so the very same files compile for both
runtime generations, and the CLR 2.0 build really does avoid every .NET 3.5+
API. ZIP output is hand-written against the PKZIP spec (CRC32 + `DeflateStream`)
because `System.IO.Compression.ZipArchive` only exists from .NET 4.5 on.

Files:
- `native/Program.cs` — entry point, single-instance guard, embedded icon
- `native/MainForm.cs` — window, scanning, comparison, result lists, export
- `native/Compare.cs` — folder scanner, file comparer, result model
- `native/DiffEngine.cs` — LCS line diff + collapsing
- `native/DiffForm.cs` — line diff window (virtual-mode DataGridView)
- `native/ZipWriter.cs` — PKZIP writer
- `native/Util.cs` — text detection, encoding, size formatting, font picking
- `native/app.manifest` — XP visual styles, DPI awareness, supported OS list
- `build-native.ps1` — builds both targets, generates the icon if missing
- `tests/TestHarness.cs` — console harness for the diff engine, encoding
  detection and ZIP round-trip (not part of the shipped programs)
- `test-gui.ps1` — drives the real window: launches with two folders, screenshots
  it, opens a diff window, checks the list item count
- `shot-dpi.ps1` — DPI-aware screenshot helper (declares DPI awareness before any
  window/DC query, so at 125% scaling it captures real physical pixels). Pass
  `-Exe <path>` to point it at a freshly built exe; its default looks in
  `..\文件比较器成品\` and then `build-native\`, which is a good way to end up
  measuring a stale binary.
- `--dump-ui <file>` (hidden switch of the native build) — writes the control tree
  (type, bounds, effective font, client size, preferred size) and exits. Handy when
  something "looks right in the code but renders wrong".
- `verify-topmost.ps1` — automated check of the always-on-top toggle: for the native
  build it clicks the checkbox (`BM_CLICK`) and reads `WS_EX_TOPMOST`; with `-Web` it
  drives the injected page button over CDP and checks the same window flag twice
  (on, then off). Pass `-CheckText` for the native run (WinForms draws both buttons
  and checkboxes as `BS_OWNERDRAW`, so the text is the only way to tell them apart).
- `verify-ignore.ps1` — ignore-rules check on the native build: launches the real window,
  reads the button/info line, opens the dialog, unticks a preset, saves, and confirms the
  automatic rescan + recompare (ignored counts and result counts must change). Note it
  clicks the modal dialog with `PostMessage`/`BM_CLICK` — a synchronous `SendMessage`
  would block on the modal loop and hang the script.
- `verify-ignore-web.ps1` — ignore-rules check on the WebView2 build over CDP: runs the
  same 20 semantic cases as the native unit test, then feeds real files to the page with
  `DOM.setFileInputFiles` (a `webkitdirectory` input wants the DIRECTORY path — Blink
  enumerates it and fills `webkitRelativePath`) and checks the filtered count, the panel
  text and a real compare. Resets the rules afterwards.
- `tests/TestIgnoreRules.cs` — 40 assertions for the rule engine (compile together with
  `native/IgnoreRules.cs`: `csc /target:exe /codepage:65001 native/IgnoreRules.cs tests/TestIgnoreRules.cs`)
- `verify-history.ps1` — history check on the native build (37 assertions): an entry is written when a comparison FINISHES (no export needed); three interactive runs
  create three entries, the panel shows them, restore brings the snapshot back with the counts,
  moving folder B away makes it warn + grey out export while the list stays readable, **ticking two
  rows anywhere in the `清理` cell (clicking far from the little box used to do nothing at all)
  deletes exactly those two and keeps the unticked one**, the column header toggles select-all, and
  the one-click clean goes through a confirmation box, and a plain compare without any export still leaves one entry behind. It also refuses to run while another copy of
  the app holds the single-instance mutex (that used to hang the script on the "already running" box).
- `verify-history-web.ps1` — history check on the WebView2 build (22 assertions): real files → compare **already** writes an entry (scraped from the page DOM); export →
  compare → export (the host's save dialog is a DirectUI window; its `Button|保存(&S)` child can
  be found and clicked), then the pill shows `历史记录 (1)`, the snapshot views, and cleaning works.
- `verify-anim-web.ps1` — "dynamic island" motion check on the WebView2 build (33 assertions):
  all four pills carry the animation base class + a transform transition, a real mouse hover lifts
  them, a click adds the shine sweep and the spring pop, both panels play `fd-island-in` with a
  `transform-origin` computed from the clicked pill, closing reverses it back into the capsule, the
  theme/pin labels roll over to the new value with a fading ghost of the old one, and the two panels
  stay mutually exclusive. Section 10 freezes the animation with the Web Animations API
  (`pause()` + `currentTime`) and writes frame-by-frame PNGs to `动效截图\` (the CDP screenshot call
  itself takes ~200 ms, so plain captures only ever show the settled state).
- `make-site.ps1` — builds the "official site" page from the untouched `文件比较器.html`. The page
  **is the Win10-11 build in web form**: the script extracts the `UiScript` constant straight out of
  `src\Program.cs` (the same source the exe embeds) and inlines it, so the three pills, both panels,
  the island animations, the ignore engine and the theme switch behave exactly like in the exe.
  Two host-only bits degrade on their own: `窗口置顶` is not created (a browser tab has no window to
  pin) and history falls back to `localStorage` (`.fd-history`), mirroring the host protocol
  (`history-add/list/delete/clear` → `history-ok <count> <bytes> <base64>`) so no panel code changed.
  It also inserts a card below the tool with **download the full version**
  (`href="文件比较器(XP-Win7版).exe" download`) and **contact the author** (`https://b23.tv/7ojZmWb`).
  Card style uses the page's CSS variables, so dark mode follows for free. Publish by uploading the
  generated html next to the exe.
- `verify-site.ps1` — drives the generated site in a real browser (headless Edge over CDP, page served
  by a throwaway node static server): 34 assertions covering the three pills (and the deliberate
  absence of `窗口置顶`), the untouched tool markup, the theme switch + `localStorage`, the island
  panel animation, the ignore engine (19 preset rules = 6+9+4), history seeded into `localStorage`
  then listed and cleared through the panel's two-step confirm, and both download/contact links.
- `verify-contact.ps1` — 43 assertions for the distribution package and both entry points: the native exe
  contains the author URL as a UTF-16 literal, a real window shows the `联系作者` button in the
  bottom-right corner (measured against the window rect, visible + enabled), and the generated site
  has both links with the right targets and insertion points while the original html stays 76023 bytes.
- Hidden switches for automation / unattended re-packing (native build):
  `--silent`, `--dump-ui <file>`, `--dump-history <file>`, `--restore-history <index> [--export <zip>]`.
- `make-icon.ps1` — 7-size ICO from GDI+ drawing code

---

## 2. WebView2 host — earlier approach (`src/`, `build.ps1`)

Keeps the original page verbatim and hosts it in a Chromium (WebView2) control.
Output: `build\FolderDiff.exe`, ~1 MB, single file, works offline because the two
CDN scripts are embedded.

This is the nicer-looking option (it *is* the original page), but it requires the
WebView2 Evergreen Runtime — which exists on Windows 10/11, can be installed on
Windows 7, and **does not exist on Windows XP or Vista at all**. It also cannot
run on a Windows 7 that lacks a compatible runtime build.

Kept for reference; rebuild with:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -SourceHtml "D:\桌面\文件比较器.html"
```

Verification scripts for this variant: `verify.ps1` (CDP probe of the live page)
and `test-download.ps1` (checks the native save-as dialog appears).

---

## Verification performed

Native build:
- comparison result on crafted test data: **3 differing / 2 only-in-A / 2 only-in-B / 3 identical** — matches the expected answer exactly
- diff engine: expected sequence `Equal|Removed|Added|Equal|Added|Equal` — PASS
- collapsing: 103 raw rows → 13 displayed rows with 1 "omitted 55 identical lines" marker — PASS
- encoding: GBK / UTF-8 / UTF-8-BOM copies of the same Chinese text all decode identically — PASS
- ZIP: 3 entries opened by .NET, extracted by `Expand-Archive`, SHA-256 identical
  after round-trip, 1.5 MB text entry deflated to 102 KB
- end-to-end GUI export: 11 entries with the documented folder structure, exit code 0
- both framework targets produce byte-identical output for the same input
  (only the timestamp inside `对比说明.txt` differs)

WebView2 build:
- page renders correctly; `JSZip 3.10.1`, `saveAs`, `webkitdirectory` all present in the live page
- a real blob download raises the native "save as" dialog

## Uninstall

Native: delete the exe. Nothing else is written anywhere.
WebView2: delete the exe, then delete `%LOCALAPPDATA%\FolderDiff`.
