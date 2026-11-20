using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace FF11MacroOrganizer.Core;

public sealed record AutoTranslateEntry(
    uint Code,
    string Text,
    string Reading,
    string Category,
    bool IsItem,
    string NormalizedText,
    string NormalizedReading);

public sealed class AutoTranslateDictionary
{
    private const int ItemRecordSize = 0x1400; // 5,120 bytes

    // 日本語アイテムDAT（実アイテム系10ファイル）
    private static readonly (string RelPath, string Category)[] JapaneseItemDats =
    [
        (@"ROM\0\4.DAT",     "アイテム(一般・素材)"),
        (@"ROM\0\5.DAT",     "アイテム(消費・薬品)"),
        (@"ROM\0\6.DAT",     "アイテム(武器)"),
        (@"ROM\0\7.DAT",     "アイテム(防具)"),
        (@"ROM\0\8.DAT",     "アイテム(アタッチメント)"),
        (@"ROM\0\9.DAT",     "アイテム(ギル)"),
        (@"ROM\286\72.DAT",  "アイテム(防具・IL装備)"),
        (@"ROM\301\114.DAT", "アイテム(追加一般)"),
        (@"ROM\217\20.DAT",  "アイテム(メイズタビュラ)"),
        (@"ROM\387\13.DAT",  "アイテム(セレスタイト等)"),
    ];

    private static readonly byte[] StringHeaderMarker = CreateStringHeaderMarker();

    private readonly Dictionary<uint, AutoTranslateEntry> _entries = new();
    private readonly Dictionary<string, List<uint>> _textToCodes = new(StringComparer.Ordinal);
    private readonly List<AutoTranslateEntry> _allEntries = [];
    private readonly List<string> _categories = [];

    public int Count => _entries.Count;
    public int StandardCount { get; private set; }
    public int ItemCount { get; private set; }
    public string? LoadedFf11Root { get; private set; }
    public IReadOnlyList<string> Categories => _categories;

    public bool TryResolve(uint code, out string text)
    {
        if (_entries.TryGetValue(code, out var entry))
        {
            text = entry.Text;
            return true;
        }
        text = string.Empty;
        return false;
    }

    public bool IsAmbiguous(uint code)
    {
        return _entries.TryGetValue(code, out var entry) &&
               _textToCodes.TryGetValue(entry.Text, out var list) &&
               list.Count > 1;
    }

    /// <summary>
    /// 閲覧プレビュー用の表記（常にコードなしの日本語表記）
    /// </summary>
    public string FormatForDisplay(uint code)
    {
        return _entries.TryGetValue(code, out var entry)
            ? $"【{entry.Text}】"
            : $"【定型文:{code:X8}】";
    }

    /// <summary>
    /// テキスト編集用の表記（同名衝突する単語だけ #コード を付けて安全に往復させる）
    /// </summary>
    public string FormatForEditing(uint code)
    {
        if (!_entries.TryGetValue(code, out var entry))
        {
            return $"【定型文:{code:X8}】";
        }

        return IsAmbiguous(code)
            ? $"【{entry.Text}#{code:X8}】"
            : $"【{entry.Text}】";
    }

    /// <summary>
    /// 【...】の中身から4バイト定型文コードを逆引きする
    /// </summary>
    public bool TryResolveCodeFromToken(string tokenInner, out uint code, out bool isAmbiguous)
    {
        code = 0;
        isAmbiguous = false;
        if (string.IsNullOrEmpty(tokenInner)) return false;

        // 1. 【定型文:XXXXXXXX】形式
        const string rawPrefix = "定型文:";
        if (tokenInner.StartsWith(rawPrefix, StringComparison.Ordinal) &&
            tokenInner.Length == rawPrefix.Length + 8 &&
            uint.TryParse(tokenInner.AsSpan(rawPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
        {
            return true;
        }

        // 2. 【名称#XXXXXXXX】形式（衝突回避トークン）
        if (tokenInner.Length >= 10 &&
            tokenInner[^9] == '#' &&
            uint.TryParse(tokenInner.AsSpan(tokenInner.Length - 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
        {
            return true;
        }

        // 3. 通常の【単語】形式を辞書から逆引き
        if (_textToCodes.TryGetValue(tokenInner, out var candidates))
        {
            if (candidates.Count == 1)
            {
                code = candidates[0];
                return true;
            }

            if (candidates.Count > 1)
            {
                isAmbiguous = true;
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 編集画面の定型文挿入ピッカー用検索（ひらがな・カタカナ・全角半角の揺れを吸収）
    /// </summary>
    public List<AutoTranslateEntry> Search(string query, string? categoryFilter = null, int maxResults = 200)
    {
        string normQuery = NormalizeForSearch(query);
        bool hasCategory = !string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != "すべて";

        var exactOrPrefix = new List<AutoTranslateEntry>();
        var contains = new List<AutoTranslateEntry>();

        foreach (var entry in _allEntries)
        {
            if (hasCategory && !string.Equals(entry.Category, categoryFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (normQuery.Length == 0)
            {
                exactOrPrefix.Add(entry);
                if (exactOrPrefix.Count >= maxResults) break;
                continue;
            }

            if (entry.NormalizedText.StartsWith(normQuery, StringComparison.Ordinal) ||
                entry.NormalizedReading.StartsWith(normQuery, StringComparison.Ordinal))
            {
                exactOrPrefix.Add(entry);
            }
            else if (entry.NormalizedText.Contains(normQuery, StringComparison.Ordinal) ||
                     entry.NormalizedReading.Contains(normQuery, StringComparison.Ordinal))
            {
                contains.Add(entry);
            }
        }

        var results = new List<AutoTranslateEntry>(Math.Min(maxResults, exactOrPrefix.Count + contains.Count));
        results.AddRange(exactOrPrefix.OrderBy(e => e.IsItem).ThenBy(e => e.Text.Length).Take(maxResults));

        int remain = maxResults - results.Count;
        if (remain > 0)
        {
            results.AddRange(contains.OrderBy(e => e.IsItem).ThenBy(e => e.Text.Length).Take(remain));
        }

        return results;
    }

    /// <summary>
    /// 指定されたパス（USERフォルダやキャラフォルダでも可）または既定パスから FF11 ルートを探して辞書を読み込む
    /// </summary>
    public bool TryLoadFromHintPath(string? hintPath)
    {
        string? ff11Root = FindFf11Root(hintPath);
        if (ff11Root == null) return false;

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
        _textToCodes.Clear();
        _allEntries.Clear();
        _categories.Clear();
        StandardCount = 0;
        ItemCount = 0;
        LoadedFf11Root = ff11Root;

        var categorySet = new HashSet<string>(StringComparer.Ordinal);

        // 1. 通常定型文 (0x02) + 参照先 d_msg マスターの読み込み
        string mainDat = Path.Combine(ff11Root, @"ROM\168\24.DAT");
        if (File.Exists(mainDat))
        {
            string[] areas = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\165\63.DAT"), sjis);     // @A
            string[] jobs = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\165\64.DAT"), sjis);      // @J
            string[] abilities = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\181\68.DAT"), sjis); // @Y
            string[] spells = ReadDMsgStrings(Path.Combine(ff11Root, @"ROM\181\69.DAT"), sjis);    // @C

            var rawEntries = ParseAutoTranslateDat(mainDat, sjis);
            foreach (var (code, rawText, reading, category) in rawEntries)
            {
                string resolved = ResolveReferenceTag(rawText, areas, spells, jobs, abilities);
                if (string.IsNullOrWhiteSpace(resolved)) continue;

                AddEntry(code, resolved, reading, category, isItem: false, categorySet);
                StandardCount++;
            }
        }

        // 2. 日本語アイテム名 (0x07) の読み込み
        foreach (var (relPath, category) in JapaneseItemDats)
        {
            string fullPath = Path.Combine(ff11Root, relPath);
            if (!File.Exists(fullPath)) continue;

            LoadJapaneseItemDat(fullPath, category, sjis, categorySet);
        }
    }

    private void AddEntry(
        uint code,
        string text,
        string reading,
        string category,
        bool isItem,
        HashSet<string> categorySet)
    {
        if (_entries.ContainsKey(code)) return;

        var entry = new AutoTranslateEntry(
            Code: code,
            Text: text,
            Reading: reading,
            Category: category,
            IsItem: isItem,
            NormalizedText: NormalizeForSearch(text),
            NormalizedReading: NormalizeForSearch(reading));

        _entries[code] = entry;
        _allEntries.Add(entry);

        if (!_textToCodes.TryGetValue(text, out var codeList))
        {
            codeList = [];
            _textToCodes[text] = codeList;
        }
        codeList.Add(code);

        if (categorySet.Add(category))
        {
            _categories.Add(category);
        }
    }

    private void LoadJapaneseItemDat(string path, string category, Encoding sjis, HashSet<string> categorySet)
    {
        byte[] allBytes = File.ReadAllBytes(path);
        int recordCount = allBytes.Length / ItemRecordSize;
        Span<byte> dec = stackalloc byte[0x180];

        for (int i = 0; i < recordCount; i++)
        {
            int offset = i * ItemRecordSize;
            DecodeRor5(allBytes.AsSpan(offset, dec.Length), dec);

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(dec[..4]);
            if (id == 0 || id > 0xFFFF) continue;

            int markerIdx = dec.IndexOf(StringHeaderMarker);
            if (markerIdx < 0) continue;

            var textSlice = dec[(markerIdx + StringHeaderMarker.Length)..];
            int nullIdx = textSlice.IndexOf((byte)0x00);
            var nameBytes = nullIdx >= 0 ? textSlice[..nullIdx] : textSlice;
            if (nameBytes.Length == 0) continue;

            string name = sjis.GetString(nameBytes);
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "(空き)") continue;

            uint code = 0x07010000u | id;
            int before = _entries.Count;
            AddEntry(code, name, string.Empty, category, isItem: true, categorySet);
            if (_entries.Count > before) ItemCount++;
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

    private static List<(uint Code, string Text, string Reading, string Category)> ParseAutoTranslateDat(string path, Encoding sjis)
    {
        var list = new List<(uint, string, string, string)>();
        byte[] data = File.ReadAllBytes(path);
        int offset = 0;

        while (offset + 76 <= data.Length)
        {
            if (data[offset] != 0x02 || data[offset + 1] != 0x01 || data[offset + 3] != 0x00) break;

            byte groupId = data[offset + 2];
            string categoryName = DecodeNullTerminated(data.AsSpan(offset + 4, 32), sjis);
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                categoryName = $"定型文グループ 0x{groupId:X2}";
            }

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

                string text = DecodeNullTerminated(data.AsSpan(pos, textLen), sjis);
                pos += textLen;

                string reading = string.Empty;
                if (eLang == 0x01 && pos < nextCategory)
                {
                    int readingLen = data[pos];
                    pos += 1;
                    if (pos + readingLen <= nextCategory)
                    {
                        reading = DecodeNullTerminated(data.AsSpan(pos, readingLen), sjis);
                    }
                    pos += readingLen;
                }

                uint code = ((uint)eType << 24) | ((uint)eLang << 16) | ((uint)eGroup << 8) | eId;
                list.Add((code, text, reading, categoryName));
            }

            offset = nextCategory;
        }

        return list;
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

            if (fType != 0 || fOffset < 0 || fOffset >= rec.Length) continue;

            var slice = rec[fOffset..];
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

    private static void DecodeRor5(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            byte b = src[i];
            dst[i] = (byte)((b >> 5) | (b << 3));
        }
    }

    private static void XorInPlace(byte[] buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] ^= 0xFF;
        }
    }

    private static string DecodeNullTerminated(ReadOnlySpan<byte> span, Encoding sjis)
    {
        int nullIdx = span.IndexOf((byte)0x00);
        var slice = nullIdx >= 0 ? span[..nullIdx] : span;
        return slice.Length > 0 ? sjis.GetString(slice) : string.Empty;
    }

    private static byte[] CreateStringHeaderMarker()
    {
        var buf = new byte[28];
        buf[0] = 0x01;
        return buf;
    }

    /// <summary>
    /// 検索用の正規化（NFKCで全角英数・半角カナを統一 → 小文字化 → カタカナをひらがなへ変換）
    /// </summary>
    public static string NormalizeForSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string nfkc = input.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder(nfkc.Length);
        foreach (char c in nfkc)
        {
            // カタカナ (ァ〜ヶ) をひらがな (ぁ〜ゖ) に揃える
            if (c >= '\u30A1' && c <= '\u30F6')
            {
                sb.Append((char)(c - 0x60));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}