using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace FF11MacroOrganizer.Core;

public static class MacroBinaryCodec
{
    public const int HeaderSize = 24;
    public const int SlotSize = 380;
    public const int SlotsPerPage = 20;
    public const int DatFileSize = HeaderSize + SlotSize * SlotsPerPage; // 7624 bytes

    public const int TitleRecordSize = 16;
    public const int MaxBookTitleBytes = TitleRecordSize - 1; // 15 bytes (末尾 0x00 を確保)
    public const int BooksPerTitleFile = 20;
    public const int TtlFileSize = HeaderSize + TitleRecordSize * BooksPerTitleFile; // 344 bytes



    // 実ファイル検証で確認済みの標準ヘッダ（新規作成時の初期値）
    public static readonly byte[] DefaultDatHeader = [0x01, 0x00, 0x00, 0x00, 0x55, 0xD6, 0x41, 0x06];
    public static readonly byte[] DefaultTtlHeader = [0x01, 0x00, 0x00, 0x00, 0x88, 0xA7, 0xC7, 0x0D];

    private static readonly Encoding Sjis;
    private static readonly Encoding SjisStrict;

    public static AutoTranslateDictionary Dictionary { get; } = new();

    static MacroBinaryCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Sjis = Encoding.GetEncoding("shift_jis");
        SjisStrict = Encoding.GetEncoding(
            "shift_jis",
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ReplacementFallback);
    }

    public static string GetDatFileName(int bookNumber, int pageNumber)
    {
        int index = (bookNumber - 1) * 10 + (pageNumber - 1);
        return index == 0 ? "mcr.dat" : $"mcr{index}.dat";
    }

    public static string DecodeFf11Text(ReadOnlySpan<byte> raw, bool forEditing = false)
    {
        int nullIdx = raw.IndexOf((byte)0x00);
        var slice = nullIdx >= 0 ? raw[..nullIdx] : raw;
        if (slice.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        int start = 0;

        for (int i = 0; i < slice.Length; i++)
        {
            if (slice[i] == 0xFD && i + 5 < slice.Length && slice[i + 5] == 0xFD)
            {
                if (i > start) sb.Append(Sjis.GetString(slice[start..i]));
                uint code = BinaryPrimitives.ReadUInt32BigEndian(slice.Slice(i + 1, 4));
                sb.Append(forEditing
                    ? Dictionary.FormatForEditing(code)
                    : Dictionary.FormatForDisplay(code));
                i += 5;
                start = i + 1;
            }
        }

        if (start < slice.Length)
        {
            sb.Append(Sjis.GetString(slice[start..]));
        }

        return sb.ToString();
    }

    /// <summary>
    /// ブック名テキストを 16バイトの mcr.ttl レコードへ変換し、実バイト数を計測する
    /// </summary>
    public static bool TryEncodeBookTitle(
        string title,
        out byte[] recordBytes,
        out int byteCount,
        out string? errorMessage)
    {
        recordBytes = new byte[TitleRecordSize];
        byteCount = 0;
        errorMessage = null;

        if (string.IsNullOrEmpty(title))
        {
            return true; // 空文字は全ゼロ（無題）
        }

        foreach (char c in title)
        {
            if (char.IsControl(c))
            {
                errorMessage = "改行や制御文字はブック名に使用できません。";
                return false;
            }
        }

        try
        {
            byte[] encoded = SjisStrict.GetBytes(title);
            byteCount = encoded.Length;

            if (byteCount > MaxBookTitleBytes)
            {
                errorMessage = $"文字数が上限を超えています ({byteCount} / {MaxBookTitleBytes} バイト)";
                return false;
            }

            Buffer.BlockCopy(encoded, 0, recordBytes, 0, encoded.Length);
            return true;
        }
        catch (EncoderFallbackException ex)
        {
            errorMessage = $"FF11 (Shift_JIS) で使用できない文字が含まれています: '{ex.CharUnknown}'";
            return false;
        }
    }

    /// <summary>
    /// 編集テキスト（【単語】や【単語#XXXXXXXX】を含む）を FF11 バイナリへ変換し、実バイト数を計測する
    /// </summary>
    public static bool TryEncodeFf11Text(
        string text,
        int maxBytes,
        out byte[] encodedBytes,
        out int byteCount,
        out string? errorMessage)
    {
        encodedBytes = [];
        byteCount = 0;
        errorMessage = null;

        if (string.IsNullOrEmpty(text)) return true;

        using var ms = new MemoryStream(maxBytes + 16);
        int pos = 0;

        try
        {
            while (pos < text.Length)
            {
                int openIdx = text.IndexOf('【', pos);
                if (openIdx < 0)
                {
                    WritePlainSjis(ms, text.AsSpan(pos));
                    break;
                }

                if (openIdx > pos)
                {
                    WritePlainSjis(ms, text.AsSpan(pos, openIdx - pos));
                }

                int closeIdx = text.IndexOf('】', openIdx + 1);
                if (closeIdx < 0)
                {
                    // 閉じ括弧がない場合は通常テキストとして扱う
                    WritePlainSjis(ms, text.AsSpan(openIdx));
                    break;
                }

                string inner = text.Substring(openIdx + 1, closeIdx - openIdx - 1);
                if (Dictionary.TryResolveCodeFromToken(inner, out uint code, out bool isAmbiguous))
                {
                    ms.WriteByte(0xFD);
                    ms.WriteByte((byte)(code >> 24));
                    ms.WriteByte((byte)(code >> 16));
                    ms.WriteByte((byte)(code >> 8));
                    ms.WriteByte((byte)code);
                    ms.WriteByte(0xFD);
                }
                else if (isAmbiguous)
                {
                    errorMessage = $"「【{inner}】」は同名の定型文が複数あるため、定型文挿入から選択してください。";
                    byteCount = (int)ms.Length;
                    return false;
                }
                else
                {
                    // 辞書にない【...】は通常の括弧付きテキストとしてそのまま Shift_JIS 変換
                    WritePlainSjis(ms, text.AsSpan(openIdx, closeIdx - openIdx + 1));
                }

                pos = closeIdx + 1;
            }
        }
        catch (EncoderFallbackException ex)
        {
            errorMessage = $"FF11 (Shift_JIS) で使用できない文字が含まれています: '{ex.CharUnknown}'";
            byteCount = (int)ms.Length;
            return false;
        }

        encodedBytes = ms.ToArray();
        byteCount = encodedBytes.Length;

        if (byteCount > maxBytes)
        {
            errorMessage = $"文字数が上限を超えています ({byteCount} / {maxBytes} バイト)";
            return false;
        }

        return true;
    }

    private static void WritePlainSjis(MemoryStream ms, ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty) return;
        byte[] bytes = SjisStrict.GetBytes(chars.ToString());
        ms.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// 380バイトのスロットデータから編集用のタイトルと本文6行を取り出す（同名衝突コードは #XXXXXXXX 付き）
    /// </summary>
    public static (string Title, string[] Lines) DecodeSlotForEditing(ReadOnlySpan<byte> slotBytes)
    {
        var lines = new string[6];
        if (slotBytes.Length < SlotSize)
        {
            for (int i = 0; i < 6; i++) lines[i] = string.Empty;
            return (string.Empty, lines);
        }

        for (int i = 0; i < 6; i++)
        {
            lines[i] = DecodeFf11Text(slotBytes.Slice(4 + i * 61, 61), forEditing: true);
        }

        string title = DecodeFf11Text(slotBytes.Slice(370, 10), forEditing: true);
        return (title, lines);
    }

    /// <summary>
    /// 編集後のタイトルと本文6行を 380バイトの FF11 スロットバイナリへ組み立てる
    /// </summary>
    public static bool TryEncodeSlot(
        ReadOnlySpan<byte> originalSlotBytes,
        string title,
        IReadOnlyList<string> lines,
        out byte[] newSlotBytes,
        out string? errorMessage)
    {
        newSlotBytes = new byte[SlotSize];
        errorMessage = null;

        if (!TryEncodeFf11Text(title, 8, out byte[] titleBytes, out _, out string? titleErr))
        {
            errorMessage = $"タイトル: {titleErr}";
            return false;
        }

        var lineBytesList = new byte[6][];
        bool hasAnyContent = titleBytes.Length > 0;

        for (int i = 0; i < 6; i++)
        {
            string lineText = i < lines.Count ? lines[i] : string.Empty;
            if (!TryEncodeFf11Text(lineText, 60, out byte[] encoded, out _, out string? lineErr))
            {
                errorMessage = $"{i + 1}行目: {lineErr}";
                return false;
            }
            lineBytesList[i] = encoded;
            if (encoded.Length > 0) hasAnyContent = true;
        }

        // タイトル・本文とも完全に空の場合は全ゼロ（空スロット）として返す
        if (!hasAnyContent)
        {
            return true;
        }

        // 先頭4バイトは元スロットの値を保持
        if (originalSlotBytes.Length >= 4)
        {
            originalSlotBytes[..4].CopyTo(newSlotBytes.AsSpan(0, 4));
        }

        for (int i = 0; i < 6; i++)
        {
            byte[] encoded = lineBytesList[i];
            if (encoded.Length > 0)
            {
                Buffer.BlockCopy(encoded, 0, newSlotBytes, 4 + i * 61, encoded.Length);
            }
        }

        if (titleBytes.Length > 0)
        {
            Buffer.BlockCopy(titleBytes, 0, newSlotBytes, 370, titleBytes.Length);
        }

        return true;
    }

    public static MacroPage ReadPage(string characterId, string filePath, int bookNumber, int pageNumber)
    {
        var page = new MacroPage(characterId, filePath, bookNumber, pageNumber);

        if (!File.Exists(filePath))
        {
            return page; // 未作成ページは空20スロットとして扱う
        }

        byte[] data = File.ReadAllBytes(filePath);
        if (data.Length != DatFileSize)
        {
            throw new InvalidDataException($"ファイルサイズが不正です ({data.Length} bytes): {filePath}");
        }

        Array.Copy(data, 0, page.HeaderBytes, 0, 8);
        for (int i = 0; i < SlotsPerPage; i++)
        {
            var slotBytes = new byte[SlotSize];
            Array.Copy(data, HeaderSize + i * SlotSize, slotBytes, 0, SlotSize);
            page.Slots[i].SetBytes(slotBytes);
        }

        page.ExistsOnDisk = true;
        page.LoadedLastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
        page.IsDirty = false;
        return page;
    }

    public static byte[] SerializePage(MacroPage page)
    {
        byte[] data = new byte[DatFileSize];
        Array.Copy(page.HeaderBytes, 0, data, 0, 8);

        for (int i = 0; i < SlotsPerPage; i++)
        {
            page.Slots[i].CopyTo(data, HeaderSize + i * SlotSize);
        }

        byte[] md5 = MD5.HashData(data.AsSpan(HeaderSize));
        Array.Copy(md5, 0, data, 8, 16);
        return data;
    }

    public static TitleFileUnit ReadTitleFile(string filePath, int bookOffset)
    {
        var unit = new TitleFileUnit(filePath, bookOffset);
        if (!File.Exists(filePath))
        {
            return unit;
        }

        byte[] data = File.ReadAllBytes(filePath);
        if (data.Length != TtlFileSize)
        {
            throw new InvalidDataException($"タイトルファイルサイズが不正です ({data.Length} bytes): {filePath}");
        }

        Array.Copy(data, 0, unit.HeaderBytes, 0, 8);
        for (int i = 0; i < BooksPerTitleFile; i++)
        {
            Array.Copy(data, HeaderSize + i * TitleRecordSize, unit.RawTitles[i], 0, TitleRecordSize);
        }

        unit.ExistsOnDisk = true;
        unit.LoadedLastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
        unit.IsDirty = false;
        return unit;
    }

    public static byte[] SerializeTitleFile(TitleFileUnit unit)
    {
        byte[] data = new byte[TtlFileSize];
        Array.Copy(unit.HeaderBytes, 0, data, 0, 8);

        for (int i = 0; i < BooksPerTitleFile; i++)
        {
            Array.Copy(unit.RawTitles[i], 0, data, HeaderSize + i * TitleRecordSize, TitleRecordSize);
        }

        byte[] md5 = MD5.HashData(data.AsSpan(HeaderSize));
        Array.Copy(md5, 0, data, 8, 16);
        return data;
    }
}