using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace FF11MacroOrganizer.Core;

public enum DictionaryLanguage
{
    Japanese,
    English
}

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

    private sealed record LanguageDatSet(
        byte LangByte,
        string MainAutoTranslateDat,
        string AreaDMsgDat,
        string JobDMsgDat,
        string AbilityDMsgDat,
        string SpellDMsgDat,
        (string RelPath, string Category)[] ItemDats);

    private static readonly LanguageDatSet JapaneseDatSet = new(
        LangByte: 0x01,
        MainAutoTranslateDat: @"ROM\168\24.DAT",
        AreaDMsgDat: @"ROM\165\63.DAT",
        JobDMsgDat: @"ROM\165\64.DAT",
        AbilityDMsgDat: @"ROM\181\68.DAT",
        SpellDMsgDat: @"ROM\181\69.DAT",
        ItemDats:
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
        ]);

    private static readonly LanguageDatSet EnglishDatSet = new(
        LangByte: 0x02,
        MainAutoTranslateDat: @"ROM\168\25.DAT",
        AreaDMsgDat: @"ROM\165\84.DAT",
        JobDMsgDat: @"ROM\165\86.DAT",
        AbilityDMsgDat: @"ROM\181\72.DAT",
        SpellDMsgDat: @"ROM\181\73.DAT",
        ItemDats:
        [
            (@"ROM\118\106.DAT", "Items (General)"),
            (@"ROM\118\107.DAT", "Items (Usable)"),
            (@"ROM\118\108.DAT", "Items (Weapons)"),
            (@"ROM\118\109.DAT", "Items (Armor)"),
            (@"ROM\118\110.DAT", "Items (Automaton)"),
            (@"ROM\174\48.DAT",  "Items (Currency)"),
            (@"ROM\286\73.DAT",  "Items (Armor / iLvl)"),
            (@"ROM\301\115.DAT", "Items (General 2)"),
            (@"ROM\217\21.DAT",  "Items (Maze Tabula)"),
            (@"ROM\387\14.DAT",  "Items (Misc)"),
        ]);

    private static readonly byte[] StringHeaderMarker = CreateStringHeaderMarker();

    private readonly Dictionary<uint, AutoTranslateEntry> _entries = new();
    private readonly Dictionary<string, List<uint>> _textToCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<uint>> _textToCodesIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AutoTranslateEntry> _allEntries = [];
    private readonly List<string> _categories = [];

    public int Count => _entries.Count;
    public int StandardCount { get; private set; }
    public int ItemCount { get; private set; }
    public string? LoadedFf11Root { get; private set; }
    public DictionaryLanguage CurrentLanguage { get; private set; } = DictionaryLanguage.Japanese;
    public IReadOnlyList<string> Categories => _categories;

    private byte ActiveLangByte => CurrentLanguage == DictionaryLanguage.English ? (byte)0x02 : (byte)0x01;

    private bool TryGetEntry(uint code, out AutoTranslateEntry entry)
    {
        if (_entries.TryGetValue(code, out entry!))
        {
            return true;
        }

        // 定型文(0x02)・アイテム(0x07)で別言語バイト(0x01〜0x04)だった場合、現在の辞書言語へ正規化して解決
        byte type = (byte)(code >> 24);
        byte lang = (byte)((code >> 16) & 0xFF);
        byte activeLang = ActiveLangByte;

        if ((type == 0x02 || type == 0x07) && lang is >= 0x01 and <= 0x04 && lang != activeLang)
        {
            uint mappedCode = (code & 0xFF00FFFFu) | ((uint)activeLang << 16);
            if (_entries.TryGetValue(mappedCode, out entry!))
            {
                return true;
            }
        }

        entry = default!;
        return false;
    }

    public bool TryResolve(uint code, out string text)
    {
        if (TryGetEntry(code, out var entry))
        {
            text = entry.Text;
            return true;
        }
        text = string.Empty;
        return false;
    }

    public bool IsAmbiguous(uint code)
    {
        return TryGetEntry(code, out var entry) &&
               _textToCodes.TryGetValue(entry.Text, out var list) &&
               list.Count > 1;
    }

    private string FormatUnknown(uint code)
    {
        return CurrentLanguage == DictionaryLanguage.English
            ? $"【AT:{code:X8}】"
            : $"【定型文:{code:X8}】";
    }

    /// <summary>
    /// 閲覧プレビュー用の表記（常にコードなしの現在言語表記）
    /// </summary>
    public string FormatForDisplay(uint code)
    {
        return TryGetEntry(code, out var entry)
            ? $"【{entry.Text}】"
            : FormatUnknown(code);
    }

    /// <summary>
    /// テキスト編集用の表記（同名衝突する単語だけ #コード を付けて安全に往復させる）
    /// </summary>
    public string FormatForEditing(uint code)
    {
        if (!TryGetEntry(code, out var entry))
        {
            return FormatUnknown(code);
        }

        return IsAmbiguous(entry.Code)
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

        // 1. 【定型文:XXXXXXXX】 / 【AT:XXXXXXXX】形式
        const string jpRawPrefix = "定型文:";
        if (tokenInner.StartsWith(jpRawPrefix, StringComparison.Ordinal) &&
            tokenInner.Length == jpRawPrefix.Length + 8 &&
            uint.TryParse(tokenInner.AsSpan(jpRawPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
        {
            return true;
        }

        const string enRawPrefix = "AT:";
        if (tokenInner.StartsWith(enRawPrefix, StringComparison.OrdinalIgnoreCase) &&
            tokenInner.Length == enRawPrefix.Length + 8 &&
            uint.TryParse(tokenInner.AsSpan(enRawPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
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

        // 3. 通常の【単語】形式を辞書から逆引き（完全一致を最優先）
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

        // 4. 英語手入力向けの大文字・小文字無視フォールバック
        if (_textToCodesIgnoreCase.TryGetValue(tokenInner, out var ciCandidates))
        {
            if (ciCandidates.Count == 1)
            {
                code = ciCandidates[0];
                return true;
            }

            if (ciCandidates.Count > 1)
            {
                isAmbiguous = true;
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 編集画面の定型文挿入ピッカー用検索（ひらがな・カタカナ・全角半角・大小文字の揺れを吸収）
    /// </summary>
    public List<AutoTranslateEntry> Search(string query, string? categoryFilter = null, int maxResults = 200)
    {
        string normQuery = NormalizeForSearch(query);
        bool hasCategory = !string.IsNullOrWhiteSpace(categoryFilter) &&
                           categoryFilter != "すべて" &&
                           !string.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase);

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
    /// 指定されたパス（USERフォルダやコピー先フォルダでも可）またはレジストリ検出先から FF11 ルートを探して辞書を読み込む
    /// </summary>
    public bool TryLoadFromHintPath(string? hintPath, DictionaryLanguage? language = null)
    {
        var targetLanguage = language ?? CurrentLanguage;
        string? ff11Root = FindFf11Root(hintPath);
        if (ff11Root == null)
        {
            CurrentLanguage = targetLanguage;
            return false;
        }

        if (string.Equals(LoadedFf11Root, ff11Root, StringComparison.OrdinalIgnoreCase) &&
            CurrentLanguage == targetLanguage &&
            _entries.Count > 0)
        {
            return true;
        }

        LoadFromFf11Root(ff11Root, targetLanguage);
        return _entries.Count > 0;
    }

    /// <summary>
    /// 読み込み済みの FF11 ルートのまま辞書言語を切り替える（UI言語切替時用）
    /// </summary>
    public bool SwitchLanguage(DictionaryLanguage language)
    {
        if (CurrentLanguage == language && _entries.Count > 0)
        {
            return true;
        }

        CurrentLanguage = language;
        if (string.IsNullOrEmpty(LoadedFf11Root))
        {
            return false;
        }

        LoadFromFf11Root(LoadedFf11Root, language);
        return _entries.Count > 0;
    }

    private static string? FindFf11Root(string? hintPath)
    {
        return Ff11InstallDetector.ResolveFf11Root(hintPath);
    }

    private void LoadFromFf11Root(string ff11Root, DictionaryLanguage language)
    {
        // 英語版 DAT も H♂ Mannequin 等の記号に Shift_JIS を使用するため日英共通で shift_jis を使用
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding("shift_jis");

        var datSet = language == DictionaryLanguage.English ? EnglishDatSet : JapaneseDatSet;

        _entries.Clear();
        _textToCodes.Clear();
        _textToCodesIgnoreCase.Clear();
        _allEntries.Clear();
        _categories.Clear();
        StandardCount = 0;
        ItemCount = 0;
        LoadedFf11Root = ff11Root;
        CurrentLanguage = language;

        var categorySet = new HashSet<string>(StringComparer.Ordinal);

        // 1. 通常定型文 (0x02) + 参照先 d_msg マスターの読み込み
        string mainDat = Path.Combine(ff11Root, datSet.MainAutoTranslateDat);
        if (File.Exists(mainDat))
        {
            string[] areas = ReadDMsgStrings(Path.Combine(ff11Root, datSet.AreaDMsgDat), sjis);         // @A
            string[] jobs = ReadDMsgStrings(Path.Combine(ff11Root, datSet.JobDMsgDat), sjis);           // @J
            string[] abilities = ReadDMsgStrings(Path.Combine(ff11Root, datSet.AbilityDMsgDat), sjis);  // @Y
            string[] spells = ReadDMsgStrings(Path.Combine(ff11Root, datSet.SpellDMsgDat), sjis);       // @C

            var rawEntries = ParseAutoTranslateDat(mainDat, datSet.LangByte, sjis);
            foreach (var (code, rawText, reading, category) in rawEntries)
            {
                string resolved = ResolveReferenceTag(rawText, areas, spells, jobs, abilities);
                if (string.IsNullOrWhiteSpace(resolved)) continue;

                AddEntry(code, resolved, reading, category, isItem: false, categorySet);
                StandardCount++;
            }
        }

        // 2. アイテム名 (0x07) の読み込み
        foreach (var (relPath, category) in datSet.ItemDats)
        {
            string fullPath = Path.Combine(ff11Root, relPath);
            if (!File.Exists(fullPath)) continue;

            LoadItemDat(fullPath, datSet.LangByte, category, sjis, categorySet);
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

        if (!_textToCodesIgnoreCase.TryGetValue(text, out var ciList))
        {
            ciList = [];
            _textToCodesIgnoreCase[text] = ciList;
        }
        ciList.Add(code);

        if (categorySet.Add(category))
        {
            _categories.Add(category);
        }
    }

    private void LoadItemDat(string path, byte langByte, string category, Encoding sjis, HashSet<string> categorySet)
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
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "(空き)" || name == "(NULL)") continue;

            uint code = 0x07000000u | ((uint)langByte << 16) | id;
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

    private static List<(uint Code, string Text, string Reading, string Category)> ParseAutoTranslateDat(
        string path,
        byte expectedLangByte,
        Encoding sjis)
    {
        var list = new List<(uint, string, string, string)>();
        byte[] data = File.ReadAllBytes(path);
        int offset = 0;

        while (offset + 76 <= data.Length)
        {
            if (data[offset] != 0x02 || data[offset + 1] != expectedLangByte || data[offset + 3] != 0x00) break;

            byte groupId = data[offset + 2];
            string categoryName = DecodeNullTerminated(data.AsSpan(offset + 4, 32), sjis);
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                categoryName = expectedLangByte == 0x02
                    ? $"Auto-Translate 0x{groupId:X2}"
                    : $"定型文グループ 0x{groupId:X2}";
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