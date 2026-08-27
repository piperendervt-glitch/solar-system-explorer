using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SolarSystem.Editor
{
    /// <summary>
    /// `.unitypackage` を読み、取り込み先を差し替えて書き出す (Step 11-1a)。
    ///
    /// ■ 構造
    /// gzip された tar。エントリは `&lt;guid&gt;/pathname`（元のアセットパス）、
    /// `&lt;guid&gt;/asset`（中身）、`&lt;guid&gt;/asset.meta`、`&lt;guid&gt;/preview.png`。
    /// **フォルダ名が GUID そのもの**なので、取り込まなくても GUID が読める。
    ///
    /// ■ **gzip ヘッダに FEXTRA が入っている。**
    /// Asset Store のメタ情報（id / version / publisher など、実測 330 バイト）が
    /// FEXTRA サブフィールドに埋まっている。Python の `tarfile` はこれを扱えず
    /// `ReadError: invalid compressed data` になった。
    /// **ここではヘッダを自前で読み飛ばし、本体を `DeflateStream` に渡す。**
    /// 実装が推測に依存しないので、Demo 4 のステーションアセットでも同じ経路が使える。
    ///
    /// ■ なぜ取り込み先を書き換えるのか
    /// `AssetDatabase.ImportPackageImmediately` は**宛先の引数を持たない。**
    /// パッケージが記録しているパス（`Assets/HiRezSpaceshipsCreatorFree/…`）へ
    /// そのまま展開される。それは `.gitignore` の `/unity/Assets/ThirdParty/*` の
    /// 外なので、**取り込んだ瞬間に追跡対象になる。**
    ///
    /// 「取り込んでから移動する」案は採らない。`run_unity.ps1` は 15 分で
    /// プロセスツリーを Kill する作りなので、**取り込み中に落ちると追跡対象の場所に
    /// 140MB が残り、次の `git add -A` で public に乗る。**
    /// 既存の仕組みが実際に起こしうる経路になっている。
    /// </summary>
    public static class UnityPackageReader
    {
        const int BlockSize = 512;

        public sealed class Entry
        {
            /// <summary>tar 上の名前。`&lt;guid&gt;/pathname` など。</summary>
            public string Name;

            public byte[] Content;

            public string Guid => Name.Split('/')[0];

            public string Kind
            {
                get
                {
                    string[] parts = Name.Split('/');
                    return parts[parts.Length - 1];
                }
            }
        }

        /// <summary>
        /// gzip ヘッダを読み飛ばし、deflate 本体のストリームを返す。
        /// **FEXTRA / FNAME / FCOMMENT / FHCRC を明示的に処理する。**
        /// </summary>
        public static Stream OpenDeflate(Stream raw, out byte[] extra)
        {
            extra = Array.Empty<byte>();

            var header = new byte[10];
            if (raw.Read(header, 0, 10) != 10)
            {
                throw new InvalidDataException("gzip ヘッダが短すぎる");
            }

            if (header[0] != 0x1F || header[1] != 0x8B)
            {
                throw new InvalidDataException(
                    $"gzip ではない (magic {header[0]:x2}{header[1]:x2})");
            }

            if (header[2] != 8)
            {
                throw new InvalidDataException($"deflate ではない (CM {header[2]})");
            }

            byte flg = header[3];

            if ((flg & 0x04) != 0) // FEXTRA
            {
                var xlenBytes = new byte[2];
                raw.Read(xlenBytes, 0, 2);
                int xlen = xlenBytes[0] | (xlenBytes[1] << 8);
                extra = new byte[xlen];
                ReadExactly(raw, extra, xlen);
            }

            if ((flg & 0x08) != 0) { SkipZeroTerminated(raw); } // FNAME
            if ((flg & 0x10) != 0) { SkipZeroTerminated(raw); } // FCOMMENT
            if ((flg & 0x02) != 0) { raw.ReadByte(); raw.ReadByte(); } // FHCRC

            return new DeflateStream(raw, CompressionMode.Decompress, leaveOpen: false);
        }

        /// <summary>全エントリを読む。**サイズの都合で中身も持つ**（数百 MB になりうる）。</summary>
        public static List<Entry> Read(string packagePath, out byte[] storeMetadata)
        {
            var entries = new List<Entry>();
            using (FileStream raw = File.OpenRead(packagePath))
            using (Stream tar = OpenDeflate(raw, out storeMetadata))
            {
                var header = new byte[BlockSize];
                while (true)
                {
                    if (!TryReadBlock(tar, header))
                    {
                        break;
                    }

                    if (IsAllZero(header))
                    {
                        break; // tar の終端
                    }

                    string name = ReadString(header, 0, 100);
                    long size = ReadOctal(header, 124, 12);
                    char type = (char)header[156];

                    var content = new byte[size];
                    ReadExactly(tar, content, (int)size);
                    SkipPadding(tar, size);

                    // '0' / '\0' が通常ファイル。'5' はディレクトリ。
                    if (type == '0' || type == '\0')
                    {
                        entries.Add(new Entry { Name = name, Content = content });
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// `pathname` の中身だけを差し替えて書き出す。
        /// **tar 上の名前（GUID）は触らない。** GUID が変わると参照が壊れる。
        /// </summary>
        public static int Rewrite(string sourcePath, string destinationPath,
                                  Func<string, string> mapPathname)
        {
            List<Entry> entries = Read(sourcePath, out byte[] _);
            int rewritten = 0;

            foreach (Entry e in entries)
            {
                if (e.Kind != "pathname")
                {
                    continue;
                }

                string original = Encoding.UTF8.GetString(e.Content);
                string mapped = mapPathname(original);
                if (mapped != original)
                {
                    e.Content = Encoding.UTF8.GetBytes(mapped);
                    rewritten++;
                }
            }

            using (FileStream outFile = File.Create(destinationPath))
            using (var gz = new GZipStream(outFile, CompressionLevel.Fastest))
            {
                foreach (Entry e in entries)
                {
                    WriteEntry(gz, e);
                }

                // tar の終端は 0 埋めブロック 2 つ。
                var end = new byte[BlockSize * 2];
                gz.Write(end, 0, end.Length);
            }

            return rewritten;
        }

        /// <summary>`pathname` の中身を GUID 付きで返す。取り込まずに一覧を見るため。</summary>
        public static Dictionary<string, string> ReadPathnames(string packagePath)
        {
            var map = new Dictionary<string, string>();
            foreach (Entry e in Read(packagePath, out byte[] _))
            {
                if (e.Kind == "pathname")
                {
                    map[e.Guid] = Encoding.UTF8.GetString(e.Content).Trim();
                }
            }

            return map;
        }

        // ---- tar の下回り ----

        static void WriteEntry(Stream output, Entry e)
        {
            var header = new byte[BlockSize];
            WriteString(header, 0, 100, e.Name);
            WriteOctal(header, 100, 8, 0x1A4);              // mode 0644
            WriteOctal(header, 108, 8, 0);                  // uid
            WriteOctal(header, 116, 8, 0);                  // gid
            WriteOctal(header, 124, 12, e.Content.Length);  // size
            WriteOctal(header, 136, 12, 0);                 // mtime
            header[156] = (byte)'0';                        // 通常ファイル
            Encoding.ASCII.GetBytes("ustar ").CopyTo(header, 257);
            header[263] = (byte)' ';
            header[264] = (byte)'\0';

            // チェックサムは、その欄を空白で埋めた状態で計算する。
            for (int i = 148; i < 156; i++) { header[i] = (byte)' '; }
            int sum = 0;
            foreach (byte b in header) { sum += b; }
            WriteOctal(header, 148, 7, sum);
            header[155] = (byte)' ';

            output.Write(header, 0, BlockSize);
            output.Write(e.Content, 0, e.Content.Length);

            int pad = (BlockSize - (e.Content.Length % BlockSize)) % BlockSize;
            if (pad > 0)
            {
                output.Write(new byte[pad], 0, pad);
            }
        }

        static bool TryReadBlock(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < BlockSize)
            {
                int n = s.Read(buffer, read, BlockSize - read);
                if (n <= 0)
                {
                    return read != 0 ? throw new InvalidDataException("tar が途中で終わった") : false;
                }

                read += n;
            }

            return true;
        }

        static void ReadExactly(Stream s, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, read, count - read);
                if (n <= 0)
                {
                    throw new InvalidDataException("tar のデータが途中で終わった");
                }

                read += n;
            }
        }

        static void SkipPadding(Stream s, long size)
        {
            int pad = (int)((BlockSize - (size % BlockSize)) % BlockSize);
            if (pad > 0)
            {
                ReadExactly(s, new byte[pad], pad);
            }
        }

        static void SkipZeroTerminated(Stream s)
        {
            int b;
            do
            {
                b = s.ReadByte();
            }
            while (b > 0);
        }

        static bool IsAllZero(byte[] b)
        {
            foreach (byte x in b)
            {
                if (x != 0) { return false; }
            }

            return true;
        }

        static string ReadString(byte[] b, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && b[end] != 0) { end++; }
            return Encoding.UTF8.GetString(b, offset, end - offset);
        }

        static long ReadOctal(byte[] b, int offset, int length)
        {
            string s = ReadString(b, offset, length).Trim();
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }

            long value = 0;
            foreach (char c in s)
            {
                if (c < '0' || c > '7') { break; }
                value = (value * 8) + (c - '0');
            }

            return value;
        }

        static void WriteString(byte[] b, int offset, int length, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length >= length)
            {
                throw new InvalidDataException($"tar の名前が長すぎる ({bytes.Length} >= {length}): {value}");
            }

            bytes.CopyTo(b, offset);
        }

        static void WriteOctal(byte[] b, int offset, int length, long value)
        {
            string s = Convert.ToString(value, 8).PadLeft(length - 1, '0');
            Encoding.ASCII.GetBytes(s).CopyTo(b, offset);
            b[offset + length - 1] = 0;
        }
    }
}
