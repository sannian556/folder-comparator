// 行级差异计算：与网页版同源的 LCS 算法（超过阈值时退化为逐行简单对比）
using System;
using System.Collections.Generic;

namespace FileDiffTool
{
    internal class DiffLine
    {
        public const int Equal = 0;
        public const int Removed = 1;
        public const int Added = 2;
        public const int Skip = 3;   // 折叠的相同内容

        public int Kind;
        public string TextA;
        public string TextB;
        public int IndexA = -1;
        public int IndexB = -1;
        public int SkippedCount;
    }

    internal static class DiffEngine
    {
        private const int MaxLcsLines = 800;

        public static List<DiffLine> Compute(string[] linesA, string[] linesB)
        {
            if (linesA.Length > MaxLcsLines || linesB.Length > MaxLcsLines)
                return SimpleDiff(linesA, linesB);
            return LcsDiff(linesA, linesB);
        }

        private static List<DiffLine> LcsDiff(string[] a, string[] b)
        {
            int m = a.Length;
            int n = b.Length;

            // 用一维滚动数组记录长度表会让回溯变复杂；这里直接用二维 int 数组（m,n <= 800 → 最大 640k 项，可接受）
            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal))
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = dp[i - 1, j] >= dp[i, j - 1] ? dp[i - 1, j] : dp[i, j - 1];
                }
            }

            List<DiffLine> reversed = new List<DiffLine>();
            int x = m, y = n;
            while (x > 0 || y > 0)
            {
                if (x > 0 && y > 0 && string.Equals(a[x - 1], b[y - 1], StringComparison.Ordinal))
                {
                    DiffLine d = new DiffLine();
                    d.Kind = DiffLine.Equal;
                    d.TextA = a[x - 1];
                    d.TextB = b[y - 1];
                    d.IndexA = x - 1;
                    d.IndexB = y - 1;
                    reversed.Add(d);
                    x--; y--;
                }
                else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
                {
                    DiffLine d = new DiffLine();
                    d.Kind = DiffLine.Added;
                    d.TextB = b[y - 1];
                    d.IndexB = y - 1;
                    reversed.Add(d);
                    y--;
                }
                else
                {
                    DiffLine d = new DiffLine();
                    d.Kind = DiffLine.Removed;
                    d.TextA = a[x - 1];
                    d.IndexA = x - 1;
                    reversed.Add(d);
                    x--;
                }
            }

            reversed.Reverse();
            return reversed;
        }

        private static List<DiffLine> SimpleDiff(string[] a, string[] b)
        {
            List<DiffLine> list = new List<DiffLine>();
            int min = a.Length < b.Length ? a.Length : b.Length;
            for (int i = 0; i < min; i++)
            {
                if (string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    DiffLine d = new DiffLine();
                    d.Kind = DiffLine.Equal;
                    d.TextA = a[i]; d.TextB = b[i];
                    d.IndexA = i; d.IndexB = i;
                    list.Add(d);
                }
                else
                {
                    DiffLine r = new DiffLine();
                    r.Kind = DiffLine.Removed; r.TextA = a[i]; r.IndexA = i;
                    list.Add(r);
                    DiffLine ad = new DiffLine();
                    ad.Kind = DiffLine.Added; ad.TextB = b[i]; ad.IndexB = i;
                    list.Add(ad);
                }
            }
            for (int i = min; i < a.Length; i++)
            {
                DiffLine r = new DiffLine();
                r.Kind = DiffLine.Removed; r.TextA = a[i]; r.IndexA = i;
                list.Add(r);
            }
            for (int i = min; i < b.Length; i++)
            {
                DiffLine ad = new DiffLine();
                ad.Kind = DiffLine.Added; ad.TextB = b[i]; ad.IndexB = i;
                list.Add(ad);
            }
            return list;
        }

        /// <summary>
        /// 把差异序列折叠成"只显示变更行 ± context 行"的展示序列。
        /// </summary>
        public static List<DiffLine> Collapse(List<DiffLine> diff, int context)
        {
            int total = diff.Count;
            bool[] show = new bool[total];
            for (int i = 0; i < total; i++)
            {
                if (diff[i].Kind != DiffLine.Equal)
                {
                    int start = i - context; if (start < 0) start = 0;
                    int end = i + context; if (end > total - 1) end = total - 1;
                    for (int j = start; j <= end; j++) show[j] = true;
                }
            }

            List<DiffLine> output = new List<DiffLine>();
            int lastShown = -1;
            for (int i = 0; i < total; i++)
            {
                if (!show[i])
                {
                    // 攒到下一个可见行再合并成一条"省略"
                    int runStart = i;
                    while (i + 1 < total && !show[i + 1]) i++;
                    if (lastShown >= 0 && i + 1 < total)
                    {
                        DiffLine skip = new DiffLine();
                        skip.Kind = DiffLine.Skip;
                        skip.SkippedCount = i - runStart + 1;
                        output.Add(skip);
                    }
                    continue;
                }
                output.Add(diff[i]);
                lastShown = i;
            }
            return output;
        }

        public static void CountChanges(List<DiffLine> diff, out int added, out int removed)
        {
            added = 0;
            removed = 0;
            for (int i = 0; i < diff.Count; i++)
            {
                if (diff[i].Kind == DiffLine.Added) added++;
                else if (diff[i].Kind == DiffLine.Removed) removed++;
            }
        }
    }
}
