// 忽略规则引擎的单元测试（控制台）。编译方式见 AGENTS.md：
//   csc /target:exe native/IgnoreRules.cs tests/TestIgnoreRules.cs
using System;
using System.Collections.Generic;
using System.IO;
using FileDiffTool;

class TestIgnoreRules
{
    static int pass = 0;
    static int fail = 0;

    static void Check(IgnoreRules r, string path, bool isDir, bool expect, string label)
    {
        bool got = r.IsIgnored(path, isDir);
        if (got == expect) { pass++; return; }
        fail++;
        Console.WriteLine("FAIL  [" + label + "]  " + path + (isDir ? " (目录)" : "") +
                          "  期望 " + expect + "  实际 " + got);
    }

    static void Main()
    {
        // ── 1) 三个预设全开 + 自定义 ─────────────────────────
        IgnoreRules r = new IgnoreRules();
        r.SkipVcs = true; r.SkipTemp = true; r.SkipLogs = true;
        r.Custom = "node_modules/\nbuild/**\n!keep.log\n!important/app.log\n**/temp";
        r.Compile();
        Console.WriteLine("规则条数 = " + r.RuleCount + "（预设 6+9+4 + 自定义 5 = 24）");

        Check(r, ".git/config", false, true, "vcs 目录内文件");
        Check(r, ".git", true, true, "vcs 目录本身");
        Check(r, "src/deep/.svn/entries", false, true, "嵌套 vcs");
        Check(r, "CVS/Entries", false, true, "CVS 大写");
        Check(r, ".GIT/config", false, true, "大小写不敏感");
        Check(r, "src/.gitignore", false, false, "不误伤 .gitignore");

        Check(r, "a/b/app.log", false, true, "日志");
        Check(r, "APP.LOG", false, true, "日志大写");
        Check(r, "keep.log", false, false, "! 例外");
        Check(r, "sub/deep/keep.log", false, false, "! 例外（任意层）");
        Check(r, "important/app.log", false, false, "! 锚定例外");
        Check(r, "other/app.log", false, true, "锚定例外不影响别处");
        Check(r, "data.bak", false, true, "备份");
        Check(r, "data.old", false, true, "旧文件");
        Check(r, "patch.orig", false, true, "orig");

        Check(r, "Thumbs.db", false, true, "系统文件");
        Check(r, "desktop.ini", false, true, "系统文件 2");
        Check(r, ".DS_Store", false, true, "macOS 垃圾");
        Check(r, "~$report.docx", false, true, "Office 临时文件");
        Check(r, "x.tmp", false, true, "临时后缀");
        Check(r, "y.temp", false, true, "临时后缀 2");
        Check(r, "z.swp", false, true, "vim 交换文件");
        Check(r, "backup~", false, true, "尾波浪号");

        Check(r, "node_modules/lodash/index.js", false, true, "自定义目录（根）");
        Check(r, "src/node_modules/x.js", false, true, "自定义目录（任意层）");
        Check(r, "build/out/app.exe", false, true, "锚定 + **");
        Check(r, "build", true, true, "锚定目录本身");
        Check(r, "rebuild/out.txt", false, false, "锚定不误伤 rebuild");
        Check(r, "a/b/temp", true, true, "**/temp 任意层");
        Check(r, "a/b/tempfile", false, false, "**/temp 不误伤 tempfile");

        Check(r, "readme.md", false, false, "普通文件");
        Check(r, "src/main.txt", false, false, "普通文件 2");
        Check(r, "a\\b\\app.log", false, true, "反斜杠路径");

        // ── 2) 什么都不开 ───────────────────────────────────
        IgnoreRules none = new IgnoreRules();
        none.Compile();
        Check(none, ".git/config", false, false, "空规则不过滤");
        Check(none, "app.log", false, false, "空规则不过滤 2");
        Console.WriteLine("空规则条数 = " + none.RuleCount);

        // ── 3) 注释与非法行 ─────────────────────────────────
        IgnoreRules c = new IgnoreRules();
        c.Custom = "# 注释\n; 也是注释\n\n   \n*.log\n!";
        c.Compile();
        Console.WriteLine("注释/空行后条数 = " + c.RuleCount + "（应为 1）");
        Check(c, "a.log", false, true, "注释行不影响");
        Check(c, "!", false, false, "只有 ! 的行被忽略掉");

        // ── 4) 设置文件存取往返 ─────────────────────────────
        IgnoreRules orig = new IgnoreRules();
        orig.SkipVcs = true; orig.SkipLogs = true; orig.SkipTemp = false;
        orig.Custom = "node_modules/\n*.psd";
        orig.Compile();
        orig.Save();
        IgnoreRules back = IgnoreRules.Load();
        Console.WriteLine("往返后：vcs=" + back.SkipVcs + " temp=" + back.SkipTemp + " logs=" + back.SkipLogs +
                          " custom=\"" + back.Custom.Replace("\n", "\\n") + "\" 条数=" + back.RuleCount);
        Check(back, ".git/config", false, true, "往返 vcs");
        Check(back, "a.psd", false, true, "往返 custom");
        Check(back, "Thumbs.db", false, false, "往返 temp 关闭");
        Console.WriteLine("设置文件 = " + IgnoreRules.SettingsPath);

        Console.WriteLine();
        Console.WriteLine("通过 " + pass + " 项，失败 " + fail + " 项");
        Environment.Exit(fail == 0 ? 0 : 1);
    }
}
