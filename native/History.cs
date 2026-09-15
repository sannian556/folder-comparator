// 历史记录（R2）：每次「导出 ZIP 成功」存一条快照。
//   · 只增不改：从历史还原后再导出，是新增一条，原记录不动
//   · 存清单快照（内容不同 / 仅在A / 仅在B），「完全相同」只存计数（省空间）
//   · 存放 %APPDATA%\文件比较器\历史.dat：Deflate 压缩的文本，UTF-8 带 BOM，另留 .bak
//   · 上限 20 条 / 压缩后 5MB，超了删最旧的；读坏了忽略重建，绝不让程序打不开
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileDiffTool
{
    internal class HistoryItem
    {
        public string Rel = string.Empty;
        public long SizeA;
        public long SizeB;
        public char Kind;              // 'M' 内容不同 / 'A' 仅在 A / 'B' 仅在 B
    }

    internal class HistoryEntry
    {
        public DateTime Time;
        public string DirA = string.Empty;
        public string DirB = string.Empty;
        public string ZipPath = string.Empty;
        public string IgnoreSummary = string.Empty;
        public int CountModified;
        public int CountOnlyB;
        public int CountOnlyA;
        public int CountSame;
        public List<HistoryItem> Items = new List<HistoryItem>();

        public string TimeText { get { return Time.ToString("yyyy-MM-dd HH:mm"); } }
        public string TimeFull { get { return Time.ToString("yyyy-MM-dd HH:mm:ss"); } }

        public string DirsText
        {
            get { return FolderName(DirA) + "  ⇄  " + FolderName(DirB); }
        }

        public string DiffText
        {
            get
            {
                return CountModified + " 不同 · " + CountOnlyB + " 增 · "
                     + CountOnlyA + " 缺 · " + CountSame + " 同";
            }
        }

        public static string FolderName(string dir)
        {
            if (dir == null || dir.Length == 0) return "?";
            string s = dir.TrimEnd('\\', '/');
            int i = s.LastIndexOfAny(new char[] { '\\', '/' });
            if (i >= 0 && i + 1 < s.Length) return s.Substring(i + 1);
            return s;
        }
    }

    internal static class HistoryStore
    {
        public const int MaxEntries = 20;
        public const long MaxBytes = 5L * 1024 * 1024;

        public static string Dir { get { return IgnoreRules.SettingsDir; } }
        public static string FilePath { get { return Path.Combine(Dir, "历史.dat"); } }
        public static string BakPath { get { return Path.Combine(Dir, "历史.bak"); } }
        public static string TmpPath { get { return Path.Combine(Dir, "历史.tmp"); } }

        public static long CurrentBytes()
        {
            try
            {
                FileInfo fi = new FileInfo(FilePath);
                if (fi.Exists) return fi.Length;
            }
            catch (Exception) { }
            return 0;
        }

        // ── 读 ──────────────────────────────────────────────
        public static List<HistoryEntry> Load()
        {
            List<HistoryEntry> list = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                byte[] raw = File.ReadAllBytes(FilePath);
                if (raw.Length == 0) return list;

                using (MemoryStream ms = new MemoryStream(raw))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (StreamReader sr = new StreamReader(ds, Encoding.UTF8))
                {
                    HistoryEntry cur = null;
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        if (line.StartsWith("FDHIST")) continue;
                        string[] f = line.Split('\t');
                        if (f[0] == "E" && f.Length >= 10)
                        {
                            cur = new HistoryEntry();
                            cur.Time = ParseTime(f[1]);
                            cur.DirA = f[2];
                            cur.DirB = f[3];
                            cur.ZipPath = f[4];
                            cur.CountModified = ParseInt(f[5]);
                            cur.CountOnlyB = ParseInt(f[6]);
                            cur.CountOnlyA = ParseInt(f[7]);
                            cur.CountSame = ParseInt(f[8]);
                            cur.IgnoreSummary = f[9];
                            list.Add(cur);
                        }
                        else if (cur != null && f.Length >= 3 && (f[0] == "M" || f[0] == "A" || f[0] == "B"))
                        {
                            HistoryItem it = new HistoryItem();
                            it.Kind = f[0][0];
                            it.Rel = f[1];
                            if (it.Kind == 'M')
                            {
                                it.SizeA = ParseLong(f[2]);
                                it.SizeB = f.Length > 3 ? ParseLong(f[3]) : 0;
                            }
                            else if (it.Kind == 'A') it.SizeA = ParseLong(f[2]);
                            else it.SizeB = ParseLong(f[2]);
                            cur.Items.Add(it);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 文件坏了：当作没有历史，重新开始（原文件留着不动，便于事后排查）
                try
                {
                    if (File.Exists(FilePath) && !File.Exists(FilePath + ".bad"))
                        File.Copy(FilePath, FilePath + ".bad", true);
                }
                catch (Exception) { }
                return new List<HistoryEntry>();
            }
            return list;
        }

        // ── 写（先写 .tmp 再替换，替换前把旧的留成 .bak）─────
        public static void Save(List<HistoryEntry> list)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                byte[] data;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    using (StreamWriter sw = new StreamWriter(ds, new UTF8Encoding(true)))
                    {
                        sw.WriteLine("FDHIST 1");
                        for (int i = 0; i < list.Count; i++)
                        {
                            HistoryEntry e = list[i];
                            sw.WriteLine("E\t" + e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + e.DirA + "\t" + e.DirB
                                + "\t" + e.ZipPath + "\t" + e.CountModified + "\t" + e.CountOnlyB + "\t"
                                + e.CountOnlyA + "\t" + e.CountSame + "\t" + Clean(e.IgnoreSummary));
                            for (int j = 0; j < e.Items.Count; j++)
                            {
                                HistoryItem it = e.Items[j];
                                if (it.Kind == 'M')
                                    sw.WriteLine("M\t" + it.Rel + "\t" + it.SizeA + "\t" + it.SizeB);
                                else if (it.Kind == 'A')
                                    sw.WriteLine("A\t" + it.Rel + "\t" + it.SizeA);
                                else
                                    sw.WriteLine("B\t" + it.Rel + "\t" + it.SizeB);
                            }
                        }
                    }
                    data = ms.ToArray();
                }

                File.WriteAllBytes(TmpPath, data);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        if (File.Exists(BakPath)) File.Delete(BakPath);
                        File.Move(FilePath, BakPath);
                    }
                    catch (Exception) { }
                }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(TmpPath, FilePath);
            }
            catch (Exception) { }
        }

        /// <summary>新增一条（头插）并按上限裁剪。</summary>
        public static void Add(HistoryEntry e)
        {
            List<HistoryEntry> list = Load();
            list.Insert(0, e);
            Trim(list);
            Save(list);
        }

        /// <summary>按条数 + 压缩后大小两道闸门裁剪，只留最新的。</summary>
        public static void Trim(List<HistoryEntry> list)
        {
            while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
            // 大小闸门：先存一次看实际字节数，超了再砍最旧的
            for (int guard = 0; guard < 50; guard++)
            {
                if (list.Count <= 1) break;
                long size = EstimateBytes(list);
                if (size <= MaxBytes) break;
                list.RemoveAt(list.Count - 1);
            }
        }

        /// <summary>把列表压成字节估算大小（避免反复读写磁盘）。</summary>
        private static long EstimateBytes(List<HistoryEntry> list)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    using (StreamWriter sw = new StreamWriter(ds, new UTF8Encoding(true)))
                    {
                        sw.WriteLine("FDHIST 1");
                        for (int i = 0; i < list.Count; i++)
                        {
                            HistoryEntry e = list[i];
                            sw.WriteLine("E\t" + e.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + e.DirA + "\t" + e.DirB
                                + "\t" + e.ZipPath + "\t" + e.CountModified + "\t" + e.CountOnlyB + "\t"
                                + e.CountOnlyA + "\t" + e.CountSame + "\t" + Clean(e.IgnoreSummary));
                            for (int j = 0; j < e.Items.Count; j++)
                            {
                                HistoryItem it = e.Items[j];
                                sw.WriteLine(it.Kind + "\t" + it.Rel + "\t" + it.SizeA + "\t" + it.SizeB);
                            }
                        }
                    }
                    return ms.Length;
                }
            }
            catch (Exception) { return 0; }
        }

        private static string Clean(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static DateTime ParseTime(string s)
        {
            DateTime t;
            if (DateTime.TryParse(s, out t)) return t;
            return DateTime.MinValue;
        }

        private static int ParseInt(string s)
        {
            int v;
            if (int.TryParse(s, out v)) return v;
            return 0;
        }

        private static long ParseLong(string s)
        {
            long v;
            if (long.TryParse(s, out v)) return v;
            return 0;
        }
    }
}
