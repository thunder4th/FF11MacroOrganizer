using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FF11MacroOrganizer.Core;

public static class MacroBinaryCodec
{
    public const int HeaderSize = 24;
    public const int SlotSize = 380;
    public const int SlotsPerPage = 20;
    public const int DatFileSize = HeaderSize + SlotSize * SlotsPerPage; // 7624 bytes

    public const int TitleRecordSize = 16;
    public const int BooksPerTitleFile = 20;
    public const int TtlFileSize = HeaderSize + TitleRecordSize * BooksPerTitleFile; // 344 bytes

    // 実ファイル検証で確認済みの標準ヘッダ（新規作成時の初期値）
    public static readonly byte[] DefaultDatHeader = [0x01, 0x00, 0x00, 0x00, 0x55, 0xD6, 0x41, 0x06];
    public static readonly byte[] DefaultTtlHeader = [0x01, 0x00, 0x00, 0x00, 0x88, 0xA7, 0xC7, 0x0D];

    private static readonly Encoding Sjis;

    static MacroBinaryCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Sjis = Encoding.GetEncoding("shift_jis");
    }

    public static string GetDatFileName(int bookNumber, int pageNumber)
    {
        int index = (bookNumber - 1) * 10 + (pageNumber - 1);
        return index == 0 ? "mcr.dat" : $"mcr{index}.dat";
    }

    public static string DecodeFf11Text(ReadOnlySpan<byte> raw)
    {
        int len = raw.IndexOf((byte)0x00);
        if (len == 0) return string.Empty;
        var slice = len > 0 ? raw[..len] : raw;

        var sb = new StringBuilder();
        int start = 0;
        for (int i = 0; i < slice.Length; i++)
        {
            // 0xFD で始まり 0xFD で終わる6バイトは定型文コードとして保護表示
            if (slice[i] == 0xFD && i + 5 < slice.Length && slice[i + 5] == 0xFD)
            {
                if (i > start) sb.Append(Sjis.GetString(slice[start..i]));
                string code = Convert.ToHexString(slice.Slice(i + 1, 4));
                sb.Append($"【定型文:{code}】");
                i += 5;
                start = i + 1;
            }
        }
        if (start < slice.Length) sb.Append(Sjis.GetString(slice[start..]));
        return sb.ToString();
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