// 忽略规则 / 过滤器：对比前按规则排除噪音文件（.git、node_modules、*.log 之类）。
// 语义与网页版保持一致：
//   · 通配符 * ?，不区分大小写
//   · 行尾带 / = 只匹配目录（该目录连同内容一起忽略）
//   · 不含 / 的规则按"名字"匹配任意层级（像 .gitignore）；含 / 的规则从根开始匹配，支持 **/
//   · 行首 ! = 例外，把已经忽略的再放回来
// 规则存在 %APPDATA%\文件比较器\设置.txt，下次启动照旧生效。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileDiffTool
{
    internal class IgnoreRule
    {
        public string Pattern = string.Empty;
        public string[] Segs;          // 按 '/' 拆开（已去掉尾部 '/'）
        public bool DirOnly;           // 原文以 '/' 结尾
        public bool Negate;            // 行首 '!'
        public bool Anchored;          // 含 '/'（从根匹配，否则只比名字）
    }

    internal class IgnoreRules
    {
        // ── 预设 ────────────────────────────────────────────
        public bool SkipVcs;     // 版本控制目录
        public bool SkipTemp;    // 系统与临时文件
        public bool SkipLogs;    // 日志与备份

        public string Custom = string.Empty;   // 自定义规则，多行

        private static readonly string[] VcsPatterns = new string[] {
            ".git/", ".svn/", ".hg/", ".bzr/", "_darcs/", "CVS/"
        };
        private static readonly string[] TempPatterns = new string[] {
            "Thumbs.db", "desktop.ini", ".DS_Store", "~$*", "*.tmp", "*.temp", "*.swp", "*.swo", "*~"
        };
        private static readonly string[] LogPatterns = new string[] {
            "*.log", "*.bak", "*.old", "*.orig"
        };

        private List<IgnoreRule> _rules = new List<IgnoreRule>();

        /// <summary>规则条数（预设展开 + 自定义非空行）；0 表示不过滤。</summary>
        public int RuleCount { get { return _rules.Count; } }

        public bool IsEmpty { get { return _rules.Count == 0; } }

        // ── 编译与匹配 ──────────────────────────────────────

        /// <summary>把预设与自定义文本编译成规则表。</summary>
        public void Compile()
        {
            _rules = new List<IgnoreRule>();
            if (SkipVcs) AddLines(VcsPatterns);
            if (SkipTemp) AddLines(TempPatterns);
            if (SkipLogs) AddLines(LogPatterns);
            if (Custom != null && Custom.Length > 0)
            {
                string[] lines = Custom.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                AddLines(lines);
            }
        }

        private void AddLines(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                IgnoreRule r = Parse(lines[i]);
                if (r != null) _rules.Add(r);
            }
        }

        private static IgnoreRule Parse(string raw)
        {
            if (raw == null) return null;
            string s = raw.Trim();
            if (s.Length == 0) return null;
            if (s[0] == '#' || s[0] == ';') return null;      // 注释行

            IgnoreRule r = new IgnoreRule();
            if (s[0] == '!') { r.Negate = true; s = s.Substring(1).Trim(); }
            if (s.Length == 0) return null;

            s = s.Replace('\\', '/');
            while (s.StartsWith("./")) s = s.Substring(2);
            if (s.EndsWith("/")) { r.DirOnly = true; s = s.TrimEnd('/'); }
            if (s.Length == 0) return null;

            r.Pattern = s.ToLowerInvariant();
            r.Anchored = r.Pattern.IndexOf('/') >= 0;
            r.Segs = r.Pattern.Split('/');
            return r;
        }

        /// <summary>判断相对路径（含其各级父目录）是否命中忽略规则。</summary>
        public bool IsIgnored(string relPath, bool isDir)
        {
            if (_rules.Count == 0) return false;
            string rel = Normalize(relPath);
            if (rel.Length == 0) return false;

            string[] segs = rel.Split('/');
            bool hit = false;
            for (int i = 0; i < _rules.Count; i++)
            {
                IgnoreRule r = _rules[i];
                if (!MatchesAny(r, segs, isDir)) continue;
                if (r.Negate) return false;     // 例外：直接放行
                hit = true;
            }
            return hit;
        }

        /// <summary>先比自身，再比各级父目录（父目录按目录判断）。</summary>
        private static bool MatchesAny(IgnoreRule r, string[] segs, bool isDir)
        {
            if (MatchesOne(r, segs, segs.Length, isDir)) return true;
            for (int depth = 1; depth < segs.Length; depth++)
            {
                if (MatchesOne(r, segs, depth, true)) return true;
            }
            return false;
        }

        private static bool MatchesOne(IgnoreRule r, string[] segs, int depth, bool isDir)
        {
            if (r.DirOnly && !isDir) return false;
            if (!r.Anchored) return SegMatch(r.Segs[0], segs[depth - 1]);

            string[] sub = new string[depth];
            Array.Copy(segs, sub, depth);
            return MatchSegs(r.Segs, 0, sub, 0);
        }

        /// <summary>逐段匹配，'**' 可匹配 0 到多段。</summary>
        private static bool MatchSegs(string[] pat, int pi, string[] path, int xi)
        {
            while (pi < pat.Length)
            {
                if (pat[pi] == "**")
                {
                    if (pi == pat.Length - 1) return true;
                    for (int k = xi; k <= path.Length; k++)
                    {
                        if (MatchSegs(pat, pi + 1, path, k)) return true;
                    }
                    return false;
                }
                if (xi >= path.Length) return false;
                if (!SegMatch(pat[pi], path[xi])) return false;
                pi++;
                xi++;
            }
            return xi == path.Length;
        }

        /// <summary>单段通配符匹配（* 和 ?，不跨 '/'）。</summary>
        private static bool SegMatch(string pat, string name)
        {
            int p = 0, n = 0;
            int starP = -1, starN = 0;
            while (n < name.Length)
            {
                if (p < pat.Length && (pat[p] == '?' || pat[p] == name[n]))
                {
                    p++; n++;
                }
                else if (p < pat.Length && pat[p] == '*')
                {
                    starP = p; starN = n; p++;
                }
                else if (starP >= 0)
                {
                    p = starP + 1; starN++; n = starN;
                }
                else return false;
            }
            while (p < pat.Length && pat[p] == '*') p++;
            return p == pat.Length;
        }

        private static string Normalize(string path)
        {
            if (path == null) return string.Empty;
            string s = path.Replace('\\', '/').Trim();
            while (s.StartsWith("/")) s = s.Substring(1);
            while (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
            return s.ToLowerInvariant();
        }

        /// <summary>给界面用的一句话摘要。</summary>
        public string Summary()
        {
            if (_rules.Count == 0) return "未启用";
            List<string> parts = new List<string>();
            if (SkipVcs) parts.Add("版本控制目录");
            if (SkipTemp) parts.Add("系统与临时文件");
            if (SkipLogs) parts.Add("日志与备份");
            if (Custom != null && Custom.Trim().Length > 0) parts.Add("自定义");
            string s = string.Empty;
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) s += "、";
                s += parts[i];
            }
            return s + "（" + _rules.Count + " 条规则）";
        }

        public IgnoreRules Clone()
        {
            IgnoreRules c = new IgnoreRules();
            c.SkipVcs = SkipVcs; c.SkipTemp = SkipTemp; c.SkipLogs = SkipLogs;
            c.Custom = Custom;
            c.Compile();
            return c;
        }

        /// <summary>是否与另一份规则等价（用于判断"改了没有"）。</summary>
        public bool SameAs(IgnoreRules o)
        {
            if (o == null) return false;
            return SkipVcs == o.SkipVcs && SkipTemp == o.SkipTemp && SkipLogs == o.SkipLogs &&
                   NormalizeText(Custom) == NormalizeText(o.Custom);
        }

        private static string NormalizeText(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\r\n", "\n").Trim();
        }

        // ── 存档：%APPDATA%\文件比较器\设置.txt ──────────────

        public static string SettingsDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "文件比较器");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDir, "设置.txt"); }
        }

        public static IgnoreRules Load()
        {
            IgnoreRules r = new IgnoreRules();
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path)) { r.Compile(); return r; }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                StringBuilder custom = new StringBuilder();
                bool inCustom = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string t = line.Trim();
                    if (t == "[custom]") { inCustom = true; continue; }
                    if (inCustom)
                    {
                        custom.AppendLine(line);
                        continue;
                    }
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = t.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = t.Substring(eq + 1).Trim();
                    bool on = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
                    if (key == "vcs") r.SkipVcs = on;
                    else if (key == "temp") r.SkipTemp = on;
                    else if (key == "logs") r.SkipLogs = on;
                }
                r.Custom = custom.ToString().TrimEnd('\r', '\n');
            }
            catch (Exception) { }
            r.Compile();
            return r;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 文件比较器设置（忽略规则）—— 可直接编辑");
                sb.AppendLine("vcs=" + (SkipVcs ? "1" : "0"));
                sb.AppendLine("temp=" + (SkipTemp ? "1" : "0"));
                sb.AppendLine("logs=" + (SkipLogs ? "1" : "0"));
                sb.AppendLine("[custom]");
                if (Custom != null && Custom.Length > 0) sb.AppendLine(Custom.TrimEnd('\r', '\n'));

                File.WriteAllText(SettingsPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception) { }
        }
    }
}
