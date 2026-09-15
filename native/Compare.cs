// 文件夹扫描、文件比较、结果模型 —— 全部为原生文件系统访问
using System;
using System.Collections.Generic;
using System.IO;

namespace FileDiffTool
{
    internal class FileEntry
    {
        public string FullPath;
        public string RelPath;
        public long Size;
        public DateTime Modified;
    }

    internal class CompareItem
    {
        public string RelPath;
        public FileEntry A;
        public FileEntry B;
        public long SizeA;
        public long SizeB;
        public bool SizeDiff;
        public string Note = string.Empty;

        public long DisplaySize
        {
            get { return A != null ? A.Size : (B != null ? B.Size : 0); }
        }
    }

    internal class CompareResult
    {
        public List<CompareItem> OnlyInA = new List<CompareItem>();
        public List<CompareItem> OnlyInB = new List<CompareItem>();
        public List<CompareItem> Modified = new List<CompareItem>();
        public List<CompareItem> Same = new List<CompareItem>();
        public List<string> Errors = new List<string>();
        public string NameA = string.Empty;
        public string NameB = string.Empty;

        public bool HasDiff
        {
            get { return Modified.Count > 0 || OnlyInA.Count > 0 || OnlyInB.Count > 0; }
        }

        public int ExportFileCount
        {
            get { return OnlyInA.Count + OnlyInB.Count + Modified.Count * 2; }
        }
    }

    /// <summary>取消令牌：用简单的 volatile 标志，避免依赖 .NET 4 的 CancellationToken。</summary>
    internal class CancelFlag
    {
        private volatile bool _cancelled;
        public bool Cancelled { get { return _cancelled; } set { _cancelled = value; } }
        public void Reset() { _cancelled = false; }
    }

    /// <summary>一次扫描的统计结果。</summary>
    internal class ScanStats
    {
        public Dictionary<string, FileEntry> Map =
            new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        public long TotalSize;
        public int SkippedDirs;      // 无权限跳过的目录
        public int IgnoredFiles;     // 命中忽略规则、未纳入对比的文件
        public int IgnoredDirs;      // 命中忽略规则、整棵剪掉的目录
    }

    internal static class FolderScanner
    {
        private class PendingDir
        {
            public string Full;
            public string Rel;
        }

        /// <summary>
        /// 递归扫描文件夹（带忽略规则）。返回 相对路径(不含根文件夹名) -> 条目。
        /// 命中忽略规则的目录会整棵剪掉（连内容一起忽略），文件单独跳过；
        /// 无权限的子目录会被跳过并计入 SkippedDirs，不会中断整个扫描。
        /// </summary>
        public static ScanStats Scan(string root, IgnoreRules rules)
        {
            if (rules != null) rules.Compile();

            ScanStats stats = new ScanStats();
            Dictionary<string, FileEntry> map = stats.Map;

            string rootFull = root;
            try { rootFull = Path.GetFullPath(root); }
            catch (Exception) { }
            if (rootFull.EndsWith("\\") || rootFull.EndsWith("/"))
                rootFull = rootFull.Substring(0, rootFull.Length - 1);

            Stack<PendingDir> pending = new Stack<PendingDir>();
            PendingDir first = new PendingDir();
            first.Full = rootFull;
            first.Rel = string.Empty;
            pending.Push(first);

            while (pending.Count > 0)
            {
                PendingDir dir = pending.Pop();

                string[] subDirs = null;
                try { subDirs = Directory.GetDirectories(dir.Full); }
                catch (Exception) { stats.SkippedDirs++; }
                if (subDirs != null)
                {
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        PendingDir sub = new PendingDir();
                        sub.Full = subDirs[i];
                        sub.Rel = AppendRel(dir.Rel, Path.GetFileName(subDirs[i]));
                        // 目录命中规则 → 整棵剪掉，不进去扫
                        if (rules != null && rules.IsIgnored(sub.Rel, true))
                        {
                            stats.IgnoredDirs++;
                            continue;
                        }
                        pending.Push(sub);
                    }
                }

                string[] files = null;
                try { files = Directory.GetFiles(dir.Full); }
                catch (Exception) { stats.SkippedDirs++; }
                if (files == null) continue;

                for (int i = 0; i < files.Length; i++)
                {
                    string full = files[i];
                    string rel = full.Length > rootFull.Length ? full.Substring(rootFull.Length) : Path.GetFileName(full);
                    rel = rel.TrimStart('\\', '/');
                    if (rel.Length == 0) continue;

                    if (rules != null && rules.IsIgnored(rel, false))
                    {
                        stats.IgnoredFiles++;
                        continue;
                    }
                    if (map.ContainsKey(rel)) continue;   // 理论上不会重复，保险起见

                    FileEntry e = new FileEntry();
                    e.FullPath = full;
                    e.RelPath = rel;
                    try
                    {
                        FileInfo fi = new FileInfo(full);
                        e.Size = fi.Length;
                        e.Modified = fi.LastWriteTime;
                    }
                    catch (Exception)
                    {
                        e.Size = 0;
                        e.Modified = DateTime.MinValue;
                    }
                    map[rel] = e;
                    stats.TotalSize += e.Size;
                }
            }

            return stats;
        }

        private static string AppendRel(string parentRel, string name)
        {
            if (parentRel == null || parentRel.Length == 0) return name;
            return parentRel + "\\" + name;
        }

        /// <summary>不带忽略规则的旧签名（保留兼容）。</summary>
        public static Dictionary<string, FileEntry> Scan(string root, out long totalSize, out int skippedDirs)
        {
            ScanStats s = Scan(root, null);
            totalSize = s.TotalSize;
            skippedDirs = s.SkippedDirs;
            return s.Map;
        }

        /// <summary>把相对路径集合合并排序，供比较用。</summary>
        public static List<string> MergePaths(Dictionary<string, FileEntry> a, Dictionary<string, FileEntry> b)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<string> list = new List<string>();
            foreach (KeyValuePair<string, FileEntry> kv in a)
            {
                if (!seen.ContainsKey(kv.Key)) { seen[kv.Key] = true; list.Add(kv.Key); }
            }
            foreach (KeyValuePair<string, FileEntry> kv in b)
            {
                if (!seen.ContainsKey(kv.Key)) { seen[kv.Key] = true; list.Add(kv.Key); }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }

    internal static class FileComparer
    {
        public const int Same = 0;        // 完全相同
        public const int ContentDiff = 1; // 大小相同但内容不同
        public const int SizeDiff = 2;    // 大小不同
        public const int Error = 3;       // 读取失败

        private const int BufferSize = 128 * 1024;

        /// <summary>
        /// 流式逐字节比较：原生实现没有网页 50MB 的限制，几百 MB 的文件也只占一块缓冲区。
        /// </summary>
        public static int Compare(string pathA, string pathB, long sizeA, long sizeB, out string error)
        {
            error = null;
            if (sizeA != sizeB) return SizeDiff;

            FileStream fa = null;
            FileStream fb = null;
            try
            {
                fa = new FileStream(pathA, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize);
                fb = new FileStream(pathB, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize);

                byte[] bufA = new byte[BufferSize];
                byte[] bufB = new byte[BufferSize];

                while (true)
                {
                    int readA = ReadFull(fa, bufA);
                    int readB = ReadFull(fb, bufB);
                    if (readA != readB) return SizeDiff;
                    if (readA == 0) break;
                    for (int i = 0; i < readA; i++)
                    {
                        if (bufA[i] != bufB[i]) return ContentDiff;
                    }
                }
                return Same;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return Error;
            }
            finally
            {
                if (fa != null) try { fa.Close(); } catch (Exception) { }
                if (fb != null) try { fb.Close(); } catch (Exception) { }
            }
        }

        private static int ReadFull(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer, total, buffer.Length - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        /// <summary>
        /// 执行完整对比。report 会在每处理一个文件后被调用（done, total, 当前路径）。
        /// </summary>
        public static CompareResult CompareFolders(
            Dictionary<string, FileEntry> mapA,
            Dictionary<string, FileEntry> mapB,
            string nameA,
            string nameB,
            CancelFlag cancel,
            ProgressReport report)
        {
            CompareResult result = new CompareResult();
            result.NameA = nameA;
            result.NameB = nameB;

            List<string> paths = FolderScanner.MergePaths(mapA, mapB);
            int total = paths.Count;

            for (int idx = 0; idx < total; idx++)
            {
                if (cancel.Cancelled) break;
                string rel = paths[idx];

                FileEntry ea = mapA.ContainsKey(rel) ? mapA[rel] : null;
                FileEntry eb = mapB.ContainsKey(rel) ? mapB[rel] : null;

                if (ea != null && eb == null)
                {
                    CompareItem it = new CompareItem();
                    it.RelPath = rel;
                    it.A = ea;
                    it.SizeA = ea.Size;
                    result.OnlyInA.Add(it);
                }
                else if (ea == null && eb != null)
                {
                    CompareItem it = new CompareItem();
                    it.RelPath = rel;
                    it.B = eb;
                    it.SizeB = eb.Size;
                    result.OnlyInB.Add(it);
                }
                else if (ea != null && eb != null)
                {
                    string err;
                    int verdict = Compare(ea.FullPath, eb.FullPath, ea.Size, eb.Size, out err);

                    CompareItem it = new CompareItem();
                    it.RelPath = rel;
                    it.A = ea;
                    it.B = eb;
                    it.SizeA = ea.Size;
                    it.SizeB = eb.Size;

                    if (verdict == Same)
                    {
                        result.Same.Add(it);
                    }
                    else if (verdict == SizeDiff)
                    {
                        it.SizeDiff = true;
                        result.Modified.Add(it);
                    }
                    else if (verdict == ContentDiff)
                    {
                        result.Modified.Add(it);
                    }
                    else
                    {
                        result.Errors.Add(rel + " — " + (err == null ? "读取失败" : err));
                    }
                }

                if (report != null) report(idx + 1, total, rel);
            }

            return result;
        }
    }

    internal delegate void ProgressReport(int done, int total, string current);
}
