// 手写 ZIP 打包器：.NET Framework 2.0 没有 System.IO.Compression.ZipArchive，
// 这里按 PKZIP 规范直接输出（本地头 + 数据 + 中央目录 + EOCD），压缩用 DeflateStream。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileDiffTool
{
    internal class ZipEntryInfo
    {
        public string Name;
        public ushort Method;
        public ushort DosTime;
        public ushort DosDate;
        public uint Crc;
        public long CompressedSize;
        public long UncompressedSize;
        public long LocalHeaderOffset;
    }

    internal class ZipWriter : IDisposable
    {
        private const uint LocalSig = 0x04034b50;
        private const uint CentralSig = 0x02014b50;
        private const uint EndSig = 0x06054b50;
        private const ushort MethodStore = 0;
        private const ushort MethodDeflate = 8;
        private const ushort FlagUtf8 = 0x0800;
        private const long ZipLimit = 4000000000L;   // 无 Zip64，留出安全边界

        private Stream _out;
        private List<ZipEntryInfo> _entries = new List<ZipEntryInfo>();
        private long _offset;
        private uint[] _crcTable;
        private bool _finished;
        private long _totalUncompressed;

        public ZipWriter(Stream output)
        {
            _out = output;
            _crcTable = BuildCrcTable();
        }

        public int EntryCount { get { return _entries.Count; } }

        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static ushort ToDosTime(DateTime t)
        {
            return (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
        }

        private static ushort ToDosDate(DateTime t)
        {
            int year = t.Year;
            if (year < 1980) return (ushort)((0 << 9) | (1 << 5) | 1);
            if (year > 2107) year = 2107;
            return (ushort)(((year - 1980) << 9) | (t.Month << 5) | t.Day);
        }

        private static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] > 127) return false;
            return true;
        }

        /// <summary>加入一个磁盘文件。压缩后反而更大时自动改用 Store 方式。</summary>
        public void AddFile(string entryName, string sourcePath, DateTime lastWrite)
        {
            if (_finished) throw new InvalidOperationException("ZIP 已结束");
            if (_entries.Count >= 65000) throw new InvalidOperationException("文件数量超过 ZIP 上限（65000 个）");

            string safeName = entryName.Replace('\\', '/');

            long uncompressed;
            long compressed;
            uint crc;
            string temp = Path.Combine(Path.GetTempPath(), "fdzip_" + Guid.NewGuid().ToString("N") + ".tmp");
            bool useStore;

            FileStream src = null;
            try
            {
                src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024);
                uncompressed = src.Length;

                using (FileStream tmp = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024))
                {
                    using (DeflateStream deflate = new DeflateStream(tmp, CompressionMode.Compress, true))
                    {
                        crc = Pump(src, deflate);
                    }
                    tmp.Flush();
                }
                compressed = new FileInfo(temp).Length;
                useStore = (uncompressed == 0) || (compressed >= uncompressed);

                if (_totalUncompressed + uncompressed > ZipLimit)
                    throw new InvalidOperationException("导出内容超过 4 GB，标准 ZIP 无法承载");

                long headerOffset = WriteLocalHeader(safeName, useStore ? MethodStore : MethodDeflate,
                    useStore ? uncompressed : compressed, uncompressed, crc,
                    ToDosTime(lastWrite), ToDosDate(lastWrite));

                if (useStore)
                {
                    src.Position = 0;
                    CopyStream(src, _out);
                    _offset += uncompressed;
                }
                else
                {
                    using (FileStream tf = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        CopyStream(tf, _out);
                    }
                    _offset += compressed;
                }

                ZipEntryInfo info = new ZipEntryInfo();
                info.Name = safeName;
                info.Method = useStore ? MethodStore : MethodDeflate;
                info.DosTime = ToDosTime(lastWrite);
                info.DosDate = ToDosDate(lastWrite);
                info.Crc = crc;
                info.CompressedSize = useStore ? uncompressed : compressed;
                info.UncompressedSize = uncompressed;
                info.LocalHeaderOffset = headerOffset;
                _entries.Add(info);
                _totalUncompressed += uncompressed;
            }
            finally
            {
                if (src != null) try { src.Close(); } catch (Exception) { }
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
            }
        }

        /// <summary>把一段内存数据作为条目加入（用于附带说明文件）。</summary>
        public void AddText(string entryName, string content)
        {
            byte[] data = new UTF8Encoding(false).GetBytes(content);
            string safeName = entryName.Replace('\\', '/');
            uint crc = Crc32(data, 0, data.Length);
            DateTime now = DateTime.Now;

            long headerOffset = WriteLocalHeader(safeName, MethodStore, data.Length, data.Length, crc,
                ToDosTime(now), ToDosDate(now));
            _out.Write(data, 0, data.Length);
            _offset += data.Length;

            ZipEntryInfo info = new ZipEntryInfo();
            info.Name = safeName;
            info.Method = MethodStore;
            info.DosTime = ToDosTime(now);
            info.DosDate = ToDosDate(now);
            info.Crc = crc;
            info.CompressedSize = data.Length;
            info.UncompressedSize = data.Length;
            info.LocalHeaderOffset = headerOffset;
            _entries.Add(info);
            _totalUncompressed += data.Length;
        }

        private static int ByteCount(string s)
        {
            return Encoding.UTF8.GetByteCount(s);
        }

        private long WriteLocalHeader(string name, ushort method, long compSize, long uncompSize,
            uint crc, ushort dosTime, ushort dosDate)
        {
            long start = _offset;
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            ushort flags = IsAscii(name) ? (ushort)0 : FlagUtf8;

            WriteUInt32(LocalSig);
            WriteUInt16(20);              // version needed
            WriteUInt16(flags);
            WriteUInt16(method);
            WriteUInt16(dosTime);
            WriteUInt16(dosDate);
            WriteUInt32(crc);
            WriteUInt32((uint)compSize);
            WriteUInt32((uint)uncompSize);
            WriteUInt16((ushort)nameBytes.Length);
            WriteUInt16(0);               // extra length
            _out.Write(nameBytes, 0, nameBytes.Length);
            _offset += nameBytes.Length;  // 固定部分(30字节)已由上面的辅助方法累加
            return start;
        }

        private uint Pump(Stream input, Stream output)
        {
            byte[] buffer = new byte[128 * 1024];
            uint crc = 0xFFFFFFFFu;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                crc = UpdateCrc(crc, buffer, 0, read);
                output.Write(buffer, 0, read);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[128 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                output.Write(buffer, 0, read);
        }

        private uint UpdateCrc(uint crc, byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                crc = _crcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private uint Crc32(byte[] data, int offset, int count)
        {
            return UpdateCrc(0xFFFFFFFFu, data, offset, count) ^ 0xFFFFFFFFu;
        }

        private void WriteUInt16(ushort v)
        {
            _out.WriteByte((byte)(v & 0xFF));
            _out.WriteByte((byte)((v >> 8) & 0xFF));
            _offset += 2;
        }

        private void WriteUInt32(uint v)
        {
            _out.WriteByte((byte)(v & 0xFF));
            _out.WriteByte((byte)((v >> 8) & 0xFF));
            _out.WriteByte((byte)((v >> 16) & 0xFF));
            _out.WriteByte((byte)((v >> 24) & 0xFF));
            _offset += 4;
        }

        public void Finish()
        {
            if (_finished) return;
            _finished = true;

            long centralStart = _offset;
            for (int i = 0; i < _entries.Count; i++)
            {
                ZipEntryInfo e = _entries[i];
                byte[] nameBytes = Encoding.UTF8.GetBytes(e.Name);
                ushort flags = IsAscii(e.Name) ? (ushort)0 : FlagUtf8;

                WriteUInt32(CentralSig);
                WriteUInt16(20);          // version made by
                WriteUInt16(20);          // version needed
                WriteUInt16(flags);
                WriteUInt16(e.Method);
                WriteUInt16(e.DosTime);
                WriteUInt16(e.DosDate);
                WriteUInt32(e.Crc);
                WriteUInt32((uint)e.CompressedSize);
                WriteUInt32((uint)e.UncompressedSize);
                WriteUInt16((ushort)nameBytes.Length);
                WriteUInt16(0);           // extra
                WriteUInt16(0);           // comment
                WriteUInt16(0);           // disk number
                WriteUInt16(0);           // internal attrs
                WriteUInt32(0x20);        // external attrs: archive
                WriteUInt32((uint)e.LocalHeaderOffset);
                _out.Write(nameBytes, 0, nameBytes.Length);
                _offset += nameBytes.Length;
            }
            long centralSize = _offset - centralStart;

            WriteUInt32(EndSig);
            WriteUInt16(0);
            WriteUInt16(0);
            WriteUInt16((ushort)_entries.Count);
            WriteUInt16((ushort)_entries.Count);
            WriteUInt32((uint)centralSize);
            WriteUInt32((uint)centralStart);
            WriteUInt16(0);
        }

        public void Dispose()
        {
            if (!_finished) Finish();
            if (_out != null)
            {
                try { _out.Flush(); } catch (Exception) { }
                _out = null;
            }
        }
    }
}
