// 控制台测试台：独立验证 diff 算法与 ZIP 写出（不经过 GUI）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileDiffTool;

internal static class TestHarness
{
    private static StringBuilder _log = new StringBuilder();

    private static void Log(string s)
    {
        _log.AppendLine(s);
    }

    private static int Main(string[] args)
    {
        string outFile = args.Length > 0 ? args[0] : "harness-out.txt";
        string zipFile = args.Length > 1 ? args[1] : "harness-out.zip";
        string srcFile = args.Length > 2 ? args[2] : null;

        // ── diff 算法 ───────────────────────────────
        // 注意：文本以 \n 结尾，按 \n 切行会多出一个末尾空行，两边都有 → 视为相同行
        string textA = "line1\nline2\nline3\n";
        string textB = "line1\nline2 changed\nline3\nline4 added\n";
        List<DiffLine> diff = DiffEngine.Compute(TextUtil.SplitLines(textA), TextUtil.SplitLines(textB));
        List<DiffLine> shown = DiffEngine.Collapse(diff, 2);

        int added, removed;
        DiffEngine.CountChanges(diff, out added, out removed);
        Log("diff: linesA=4 linesB=5 added=" + added + " removed=" + removed);

        string expect = "Equal(line1)|Removed(line2)|Added(line2 changed)|Equal(line3)|Added(line4 added)|Equal()";
        StringBuilder got = new StringBuilder();
        for (int i = 0; i < diff.Count; i++)
        {
            if (i > 0) got.Append('|');
            DiffLine d = diff[i];
            if (d.Kind == DiffLine.Equal) got.Append("Equal(" + d.TextA + ")");
            else if (d.Kind == DiffLine.Removed) got.Append("Removed(" + d.TextA + ")");
            else got.Append("Added(" + d.TextB + ")");
        }
        Log("diff sequence : " + got);
        Log("expected      : " + expect);
        Log("diff match    : " + (got.ToString() == expect ? "PASS" : "FAIL"));
        Log("collapsed rows: " + shown.Count + " (context=2, all rows kept)");

        // 折叠验证：100 行里改两处（第 20、80 行），中间那段相同内容应折叠成一条省略提示
        StringBuilder big = new StringBuilder();
        for (int i = 1; i <= 100; i++) big.Append("line " + i + "\r\n");
        string[] bigA = TextUtil.SplitLines(big.ToString());
        string b2 = big.ToString().Replace("line 20\r\n", "line 20 CHANGED\r\n")
                                 .Replace("line 80\r\n", "line 80 CHANGED\r\n");
        string[] bigB = TextUtil.SplitLines(b2);
        List<DiffLine> rawDiff = DiffEngine.Compute(bigA, bigB);
        List<DiffLine> c2 = DiffEngine.Collapse(rawDiff, 2);
        int skips = 0;
        int skippedLines = 0;
        for (int i = 0; i < c2.Count; i++)
        {
            if (c2[i].Kind == DiffLine.Skip) { skips++; skippedLines += c2[i].SkippedCount; }
        }
        Log("collapse test : raw=" + rawDiff.Count + " -> collapsed=" + c2.Count
            + " skip-markers=" + skips + " skipped-lines=" + skippedLines);
        Log("collapse check: " + (skips == 1 && c2.Count < 20 ? "PASS" : "FAIL"));

        // ── 编码识别 ────────────────────────────────
        string gbkPath = Path.Combine(Path.GetTempPath(), "fdtest_gbk.txt");
        string utf8Path = Path.Combine(Path.GetTempPath(), "fdtest_utf8.txt");
        string utf8bomPath = Path.Combine(Path.GetTempPath(), "fdtest_bom.txt");
        File.WriteAllText(gbkPath, "中文测试：第一行\r\n第二行\r\n", Encoding.GetEncoding(936));
        File.WriteAllText(utf8Path, "中文测试：第一行\r\n第二行\r\n", new UTF8Encoding(false));
        File.WriteAllText(utf8bomPath, "中文测试：第一行\r\n第二行\r\n", new UTF8Encoding(true));
        string rg = TextUtil.ReadAllTextAuto(gbkPath);
        string ru = TextUtil.ReadAllTextAuto(utf8Path);
        string rb = TextUtil.ReadAllTextAuto(utf8bomPath);
        Log("gbk  read     : " + rg.Replace("\r", "").Replace("\n", " / "));
        Log("utf8 read     : " + ru.Replace("\r", "").Replace("\n", " / "));
        Log("utf8+bom read : " + rb.Replace("\r", "").Replace("\n", " / "));
        Log("encoding match: " + (rg == ru && ru == rb ? "PASS" : "FAIL"));

        // 文本类型识别
        Log("isText(a.txt)=" + TextUtil.IsTextFile("a.txt")
            + " isText(Makefile)=" + TextUtil.IsTextFile("sub\\Makefile")
            + " isText(a.exe)=" + TextUtil.IsTextFile("a.exe") + " (expect True/True/False)");
        Log("formatSize    : " + TextUtil.FormatSize(999) + " | " + TextUtil.FormatSize(2048)
            + " | " + TextUtil.FormatSize(5 * 1024 * 1024) + " | " + TextUtil.FormatSize(3L * 1024 * 1024 * 1024));

        // ── ZIP 写出 ────────────────────────────────
        try
        {
            if (File.Exists(zipFile)) File.Delete(zipFile);
            using (FileStream fs = new FileStream(zipFile, FileMode.Create, FileAccess.Write))
            {
                using (ZipWriter zip = new ZipWriter(fs))
                {
                    zip.AddText("对比说明.txt", "hello 中文 content\nsecond line\n");
                    if (srcFile != null && File.Exists(srcFile))
                    {
                        zip.AddFile("only_in_A/sub/large.bin", srcFile, DateTime.Now);
                        zip.AddFile("modified/version_B/sub/large.bin", srcFile, DateTime.Now);
                    }
                    zip.Finish();
                }
            }
            Log("zip written   : " + zipFile + " (" + new FileInfo(zipFile).Length + " bytes)");
        }
        catch (Exception ex)
        {
            Log("zip FAILED    : " + ex.Message);
        }

        // 清理临时文件
        try { File.Delete(gbkPath); File.Delete(utf8Path); File.Delete(utf8bomPath); } catch (Exception) { }

        File.WriteAllText(outFile, _log.ToString(), new UTF8Encoding(false));
        return 0;
    }
}
