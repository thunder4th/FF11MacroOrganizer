using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace FF11MacroOrganizer.Core;

public sealed class AutoTranslateDictionary
{
    private readonly Dictionary<uint, string> _entries = new();

    public int Count => _entries.Count;
    public string? LoadedFf11Root { get; private set; }

    public bool TryResolve(uint code, out string text)
    {
        return _entries.TryGetValue(code, out text!);
    }

    /// <summary>
    /// 指定されたパス（USERフォルダやキャラフォルダでも可）または既定パスから FF11 ルートを探して辞書を読み込む
    /// </summary>
    public bool TryLoadFromHintPath(string? hintPath)
    {
        string? ff11Root = FindFf11Root(hintPath);
        if (ff11Root == null) return false;

        // すでに同じFF11フォルダから読み込み済みならスキップ
        if (string.Equals(LoadedFf11Root, ff11Root, StringComparison.OrdinalIgnoreCase) && _entries.Count > 0)
        {
            return true;
        }

        LoadFromFf11Root(ff11Root);
        return _entries.Count > 0;
    }

    private static string? FindFf11Root(string? hintPath)
    {
        const string relMarker = @"ROM\168\24.DAT";

        // 1. 選択フォルダから親方向へ最大4階層さかのぼって探す
        if (!string.IsNullOrWhiteSpace(hintPath))
        {
            try
            {
                var dir = new DirectoryInfo(hintPath);
                for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, relMarker)))
                    {
                        return dir.FullName;
                    }
                }
            }
            catch
            {
                // パス不正時は既定パスへフォールバック
            }
        }

        // 2. 既定のインストール先を確認
        const string defaultRoot = @"C:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        if (File.Exists(Path.Combine(defaultRoot, relMarker)))
        {
            return defaultRoot;
        }

        return null;
    }

    private void LoadFromFf11Root(string ff11Root)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding("shift_jis");

        _entries.Clear();
        LoadedFf11Root = ff11Root;

        string mainDat = Path.Combine(ff11Root, @"ROM\168\24.DAT");
        if (!File.Exists(mainDat)) return;

        // 参照先マスターテーブル（d_msg）を読み込み
        string[] areas = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\165\63.DAT"), sjis);     // @A
        string[] jobs = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\165\64.DAT"), sjis);      // @J
        string[] abilities = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\181\68.DAT"), sjis); // @Y
        string[] spells = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\181\69.DAT"), sjis);    // @C

        var rawEntries = ParseAutoTranslateDat(mainDat, sjis);
        foreach (var (code, rawText) in rawEntries)
        {
            string resolved = ResolveReferenceTag(rawText, areas, spells, jobs, abilities);
            _entries[code] = resolved;
        }
    }

    private static string ResolveReferenceTag(
        string rawText,
        string[] areas,
        string[] spells,
        string[] jobs,
        string[] abilities)
    {
        if (rawText.Length < 3 || rawText[0] != '@') return rawText;

        if (!int.TryParse(rawText.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id) || id < 0)
        {
            return rawText;
        }

        string[]? table = rawText[1] switch
        {
            'A' => areas,
            'C' => spells,
            'J' => jobs,
            'Y' => abilities,
            _ => null
        };

        if (table != null && id < table.Length && !string.IsNullOrWhiteSpace(table[id]))
        {
            return table[id];
        }

        return rawText;
    }

    private static Dictionary<uint, string> ParseAutoTranslateDat(string path, Encoding sjis)
    {
        var dict = new Dictionary<uint, string>();
        byte[] data = File.ReadAllBytes(path);
        int offset = 0;

        while (offset + 76 <= data.Length)
        {
            if (data[offset] != 0x02 || data[offset + 1] != 0x01 || data[offset + 3] != 0x00) break;

            uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 68, 4));
            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 72, 4));
            int pos = offset + 76;
            int nextCategory = pos + (int)dataSize;
            if (nextCategory > data.Length || dataSize == 0) break;

            for (int i = 0; i < entryCount && pos + 5 <= nextCategory; i++)
            {
                byte eType = data[pos];
                byte eLang = data[pos + 1];
                byte eGroup = data[pos + 2];
                byte eId = data[pos + 3];
                int textLen = data[pos + 4];
                pos += 5;

                if (eType != 0x02 || pos + textLen > nextCategory) break;

                var span = data.AsSpan(pos, textLen);
                int nullIdx = span.IndexOf((byte)0x00);
                string text = sjis.GetString(nullIdx >= 0 ? span[..nullIdx] : span);
                pos += textLen;

                // 日本語エントリの後ろに付く「読み」フィールドをスキップ
                if (eLang == 0x01 && pos < nextCategory)
                {
                    int readingLen = data[pos];
                    pos += 1 + readingLen;
                }

                uint code = ((uint)eType << 24) | ((uint)eLang << 16) | ((uint)eGroup << 8) | eId;
                dict[code] = text;
            }

            offset = nextCategory;
        }

        return dict;
    }

    private static string[] ReadDMsgStrings(string path, Encoding sjis)
    {
        if (!File.Exists(path)) return [];

        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 64) return [];
        if (!data.AsSpan(0, 5).SequenceEqual("d_msg"u8)) return [];

        bool isEncrypted = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10, 2)) != 0;
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24, 4));
        int indexTableSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28, 4));
        int fixedEntrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32, 4));
        int entryCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(40, 4));

        if (entryCount <= 0 || headerSize < 64 || headerSize > data.Length) return [];

        var result = new string[entryCount];
        int dataStart = headerSize + indexTableSize;

        if (indexTableSize > 0)
        {
            // 可変長 d_msg（オフセット表あり）
            byte[] indexBuf = data.AsSpan(headerSize, Math.Min(indexTableSize, data.Length - headerSize)).ToArray();
            if (isEncrypted) XorInPlace(indexBuf);

            for (int i = 0; i < entryCount && (i + 1) * 8 <= indexBuf.Length; i++)
            {
                int relOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(indexBuf.AsSpan(i * 8, 4));
                int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(indexBuf.AsSpan(i * 8 + 4, 4));
                if (len <= 0 || dataStart + relOffset + len > data.Length) continue;

                byte[] rec = data.AsSpan(dataStart + relOffset, len).ToArray();
                if (isEncrypted) XorInPlace(rec);
                result[i] = ExtractFirstDMsgString(rec, sjis);
            }
        }
        else if (fixedEntrySize > 0)
        {
            // 固定長 d_msg（オフセット表なし）
            for (int i = 0; i < entryCount; i++)
            {
                int offset = dataStart + i * fixedEntrySize;
                if (offset + fixedEntrySize > data.Length) break;

                byte[] rec = data.AsSpan(offset, fixedEntrySize).ToArray();
                if (isEncrypted) XorInPlace(rec);
                result[i] = ExtractFirstDMsgString(rec, sjis);
            }
        }

        return result;
    }

    private static string ExtractFirstDMsgString(ReadOnlySpan<byte> rec, Encoding sjis)
    {
        if (rec.Length < 12) return string.Empty;

        uint fieldCount = BinaryPrimitives.ReadUInt32LittleEndian(rec[..4]);
        if (fieldCount == 0 || fieldCount > 64 || 4 + fieldCount * 8 > rec.Length)
        {
            return string.Empty;
        }

        for (int f = 0; f < fieldCount; f++)
        {
            int metaPos = 4 + f * 8;
            int fOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(metaPos, 4));
            uint fType = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(metaPos + 4, 4));

            // fType == 0 が文字列フィールド
            if (fType != 0 || fOffset < 0 || fOffset >= rec.Length) continue;

            var slice = rec[fOffset..];
            // d_msg 文字列フィールド先頭には 28バイト (01 00 00 00 + 24バイト) のヘッダが付く
            if (slice.Length >= 28 && slice[0] == 0x01 && slice[1] == 0x00 && slice[2] == 0x00 && slice[3] == 0x00)
            {
                slice = slice[28..];
            }

            int nullIdx = slice.IndexOf((byte)0x00);
            var textBytes = nullIdx >= 0 ? slice[..nullIdx] : slice;
            if (textBytes.Length == 0) continue;

            return sjis.GetString(textBytes);
        }

        return string.Empty;
    }

    private static void XorInPlace(byte[] buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] ^= 0xFF;
        }
    }
}