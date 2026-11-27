using System.IO;

namespace FF11MacroOrganizer.Core;

public sealed class MacroSlot
{
    private readonly byte[] _rawBytes = new byte[MacroBinaryCodec.SlotSize];

    public byte[] CloneBytes()
    {
        var copy = new byte[MacroBinaryCodec.SlotSize];
        Array.Copy(_rawBytes, copy, MacroBinaryCodec.SlotSize);
        return copy;
    }

    public void SetBytes(byte[] source)
    {
        if (source.Length != MacroBinaryCodec.SlotSize)
            throw new ArgumentException("スロットデータは380バイト固定です。", nameof(source));
        Array.Copy(source, _rawBytes, MacroBinaryCodec.SlotSize);
    }

    public void CopyTo(byte[] destination, int destOffset)
    {
        Array.Copy(_rawBytes, 0, destination, destOffset, MacroBinaryCodec.SlotSize);
    }

    public void Clear()
    {
        Array.Clear(_rawBytes, 0, _rawBytes.Length);
    }

    public string Title => MacroBinaryCodec.DecodeFf11Text(_rawBytes.AsSpan(370, 10));

    public string[] GetLines()
    {
        var lines = new string[6];
        for (int i = 0; i < 6; i++)
        {
            lines[i] = MacroBinaryCodec.DecodeFf11Text(_rawBytes.AsSpan(4 + i * 61, 61));
        }
        return lines;
    }

    public bool IsEmpty
    {
        get
        {
            if (_rawBytes[370] != 0x00) return false;
            for (int i = 0; i < 6; i++)
            {
                if (_rawBytes[4 + i * 61] != 0x00) return false;
            }
            return true;
        }
    }

    public static string GetKeyLabel(int slotIndex)
    {
        return slotIndex < 10
            ? $"Ctrl+{(slotIndex + 1) % 10}"
            : $"Alt+{(slotIndex - 9) % 10}";
    }
}

public sealed class MacroPage
{
    public string CharacterId { get; }
    public string FilePath { get; }
    public int BookNumber { get; }
    public int PageNumber { get; }

    public byte[] HeaderBytes { get; } = (byte[])MacroBinaryCodec.DefaultDatHeader.Clone();
    public MacroSlot[] Slots { get; }

    public bool ExistsOnDisk { get; set; }
    public DateTime? LoadedLastWriteTimeUtc { get; set; }
    public bool IsDirty { get; set; }

    public MacroPage(string characterId, string filePath, int bookNumber, int pageNumber)
    {
        CharacterId = characterId;
        FilePath = filePath;
        BookNumber = bookNumber;
        PageNumber = pageNumber;
        Slots = new MacroSlot[MacroBinaryCodec.SlotsPerPage];
        for (int i = 0; i < Slots.Length; i++)
        {
            Slots[i] = new MacroSlot();
        }
    }
}

public sealed class TitleFileUnit
{
    public string FilePath { get; }
    public int BookOffset { get; } // 0 (Book 1-20) or 20 (Book 21-40)
    public byte[] HeaderBytes { get; } = (byte[])MacroBinaryCodec.DefaultTtlHeader.Clone();
    public byte[][] RawTitles { get; }

    public bool ExistsOnDisk { get; set; }
    public DateTime? LoadedLastWriteTimeUtc { get; set; }
    public bool IsDirty { get; set; }

    public TitleFileUnit(string filePath, int bookOffset)
    {
        FilePath = filePath;
        BookOffset = bookOffset;
        RawTitles = new byte[MacroBinaryCodec.BooksPerTitleFile][];
        for (int i = 0; i < RawTitles.Length; i++)
        {
            RawTitles[i] = new byte[MacroBinaryCodec.TitleRecordSize];
        }
    }
}

public sealed class CharacterMacroStore
{
    public string CharacterId { get; }
    public string? CharacterName { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(CharacterName)
        ? CharacterId
        : $"{CharacterName} ({CharacterId})";
    public string FolderPath { get; }

    public TitleFileUnit TitleFile1 { get; } // Book 1-20 (mcr.ttl)
    public TitleFileUnit TitleFile2 { get; } // Book 21-40 (mcr_2.ttl)

    private readonly Dictionary<int, MacroPage> _pageCache = new();

    public CharacterMacroStore(string characterId, string folderPath)
    {
        CharacterId = characterId;
        FolderPath = folderPath;
        CharacterName = ResolveCharacterName(folderPath);
        TitleFile1 = MacroBinaryCodec.ReadTitleFile(Path.Combine(folderPath, "mcr.ttl"), 0);
        TitleFile2 = MacroBinaryCodec.ReadTitleFile(Path.Combine(folderPath, "mcr_2.ttl"), 20);
    }

    /// <summary>
    /// キャラフォルダ直下の .txt ファイル名（拡張子除く）をキャラ名として取得する
    /// </summary>
    private static string? ResolveCharacterName(string folderPath)
    {
        var txtFile = Directory
            .EnumerateFiles(folderPath, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (txtFile == null) return null;

        string name = Path.GetFileNameWithoutExtension(txtFile).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public MacroPage GetPage(int bookNumber, int pageNumber)
    {
        int index = (bookNumber - 1) * 10 + (pageNumber - 1);
        if (!_pageCache.TryGetValue(index, out var page))
        {
            string filePath = Path.Combine(FolderPath, MacroBinaryCodec.GetDatFileName(bookNumber, pageNumber));
            page = MacroBinaryCodec.ReadPage(CharacterId, filePath, bookNumber, pageNumber);
            _pageCache[index] = page;
        }
        return page;
    }

    public IEnumerable<MacroPage> LoadedPages => _pageCache.Values;

    public string GetBookTitle(int bookNumber)
    {
        var (unit, idx) = GetTitleLocation(bookNumber);
        string decoded = MacroBinaryCodec.DecodeFf11Text(unit.RawTitles[idx]);
        return string.IsNullOrWhiteSpace(decoded) ? $"Book{bookNumber:D2}" : decoded;
    }

    public byte[] CloneBookTitleBytes(int bookNumber)
    {
        var (unit, idx) = GetTitleLocation(bookNumber);
        var copy = new byte[MacroBinaryCodec.TitleRecordSize];
        Array.Copy(unit.RawTitles[idx], copy, MacroBinaryCodec.TitleRecordSize);
        return copy;
    }

    public void SetBookTitleBytes(int bookNumber, byte[] rawBytes)
    {
        var (unit, idx) = GetTitleLocation(bookNumber);
        Array.Copy(rawBytes, unit.RawTitles[idx], MacroBinaryCodec.TitleRecordSize);
        unit.IsDirty = true;
    }

    private (TitleFileUnit unit, int index) GetTitleLocation(int bookNumber)
    {
        return bookNumber <= 20
            ? (TitleFile1, bookNumber - 1)
            : (TitleFile2, bookNumber - 21);
    }

    public bool HasDirtyData =>
        TitleFile1.IsDirty || TitleFile2.IsDirty || _pageCache.Values.Any(p => p.IsDirty);
}