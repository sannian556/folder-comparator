// 文件比较器 —— 纯原生 Win32/WinForms 实现（不依赖任何浏览器内核）
// 目标：Windows XP SP3 ~ Windows 11 全线兼容，单文件 exe，体积 < 1 MB
// 源码刻意只使用 .NET Framework 2.0 提供的 API，以便同一份代码分别编译成
//   - CLR 2.0 目标（XP / Vista / Win7 自带运行时）
//   - CLR 4.0 目标（Win8 / Win10 / Win11 自带运行时）
// C# 3.0 语法以内，不使用 LINQ / Func / Action / 扩展方法 / HashSet。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileDiffTool
{
    internal static class TextUtil
    {
        private static readonly Dictionary<string, bool> TextExtensions = BuildTextExtensions();

        private static Dictionary<string, bool> BuildTextExtensions()
        {
            string[] names = new string[] {
                "txt", "text", "html", "htm", "xhtml", "css", "js", "jsx", "mjs", "cjs", "ts", "tsx",
                "json", "xml", "xsd", "xsl", "md", "markdown", "yaml", "yml", "toml", "ini", "cfg",
                "conf", "config", "log", "csv", "tsv", "py", "pyw", "java", "c", "cpp", "cc", "cxx",
                "h", "hpp", "hxx", "cs", "vb", "rb", "php", "go", "rs", "swift", "kt", "kts", "scala",
                "sh", "bash", "zsh", "bat", "cmd", "ps1", "sql", "r", "m", "mm", "pl", "pm", "lua",
                "vim", "gitignore", "gitattributes", "editorconfig", "env", "properties", "gradle",
                "sbt", "dockerfile", "makefile", "mk", "cmake", "tex", "bib", "rst", "org", "el",
                "clj", "cljs", "edn", "hs", "lhs", "ml", "mli", "fs", "fsx", "asp", "aspx", "jsp",
                "vue", "svelte", "astro", "njk", "nunjucks", "ejs", "hbs", "handlebars", "mustache",
                "pug", "jade", "scss", "sass", "less", "styl", "stylus", "coffee", "ls",
                "svg", "graphql", "gql", "prisma", "proto", "thrift", "avsc", "reg", "inf",
                "nfo", "srt", "ass", "vtt", "diff", "patch", "po", "pot", "strings"
            };
            Dictionary<string, bool> map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++) map[names[i]] = true;
            return map;
        }

        /// <summary>按扩展名（或整个文件名，如 Makefile）判断是否可按文本逐行对比。</summary>
        public static bool IsTextFile(string path)
        {
            if (path == null) return false;
            string name = path;
            int slash = name.LastIndexOfAny(new char[] { '\\', '/' });
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.Length == 0) return false;

            if (TextExtensions.ContainsKey(name)) return true;   // Makefile / Dockerfile 之类

            int dot = name.LastIndexOf('.');
            if (dot < 0 || dot == name.Length - 1) return false;
            string ext = name.Substring(dot + 1);
            return TextExtensions.ContainsKey(ext);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024L * 1024L)
                return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024L * 1024L)
                return (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        public static string TimeStamp()
        {
            DateTime now = DateTime.Now;
            return now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        /// <summary>ZIP 条目名里不能出现盘符/反斜杠。</summary>
        public static string ToZipPath(string relativePath)
        {
            return relativePath.Replace('\\', '/');
        }

        private static bool TryDecodeUtf8(byte[] data, int offset, int count, out string text)
        {
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                text = strict.GetString(data, offset, count);
                return true;
            }
            catch (Exception)
            {
                text = null;
                return false;
            }
        }

        private static Encoding GbkEncoding()
        {
            try { return Encoding.GetEncoding(936); }
            catch (Exception) { return Encoding.Default; }
        }

        /// <summary>
        /// 读取文本文件：优先 BOM，其次严格 UTF-8，最后回退到简体中文 GBK。
        /// 这样原生的中文 .txt/.log 不会像浏览器那样乱码。
        /// </summary>
        public static string ReadAllTextAuto(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int len = data.Length;
            if (len == 0) return string.Empty;

            if (len >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return new UTF8Encoding(false).GetString(data, 3, len - 3);
            if (len >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, len - 2);
            if (len >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data, 2, len - 2);

            string text;
            if (TryDecodeUtf8(data, 0, len, out text)) return text;
            return GbkEncoding().GetString(data);
        }

        /// <summary>按 \n 切行并去掉行尾 \r，与原网页的 split('\n') 行为一致但更干净。</summary>
        public static string[] SplitLines(string text)
        {
            string[] raw = text.Split('\n');
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i].Length > 0 && raw[i][raw[i].Length - 1] == '\r')
                    raw[i] = raw[i].Substring(0, raw[i].Length - 1);
            }
            return raw;
        }

        /// <summary>给控件选一个系统里存在的 UI 字体（XP 上通常没有雅黑）。</summary>
        public static System.Drawing.Font PickUiFont(float size, bool bold)
        {
            string[] candidates = new string[] {
                "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
                "Segoe UI", "Tahoma", "SimSun", "宋体"
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    System.Drawing.Font f = new System.Drawing.Font(candidates[i], size,
                        bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
                    if (string.Equals(f.Name, candidates[i], StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch (Exception) { }
            }
            return new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, size,
                bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
        }

        public static bool IsWritableDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return false;
                string probe = Path.Combine(path, "~fdtest" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
