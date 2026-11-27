namespace FF11MacroOrganizer.Core;

public interface IUndoAction
{
    string Description { get; }
    void Execute();
    void Undo();
}

public sealed class UndoManager
{
    private readonly Stack<IUndoAction> _undoStack = new();
    private readonly Stack<IUndoAction> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? NextUndoDescription => CanUndo ? _undoStack.Peek().Description : null;
    public string? NextRedoDescription => CanRedo ? _redoStack.Peek().Description : null;

    public void Execute(IUndoAction action)
    {
        action.Execute();
        _undoStack.Push(action);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var action = _redoStack.Pop();
        action.Execute();
        _undoStack.Push(action);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

public sealed class SwapSlotAction : IUndoAction
{
    private readonly MacroPage _pageA;
    private readonly int _slotA;
    private readonly MacroPage _pageB;
    private readonly int _slotB;

    public string Description { get; }

    public SwapSlotAction(MacroPage pageA, int slotA, MacroPage pageB, int slotB)
    {
        _pageA = pageA;
        _slotA = slotA;
        _pageB = pageB;
        _slotB = slotB;
        Description = Loc.Instance.Format("UndoSwap", MacroSlot.GetKeyLabel(slotA), MacroSlot.GetKeyLabel(slotB));
    }

    public void Execute() => Swap();
    public void Undo() => Swap();

    private void Swap()
    {
        byte[] a = _pageA.Slots[_slotA].CloneBytes();
        byte[] b = _pageB.Slots[_slotB].CloneBytes();
        _pageA.Slots[_slotA].SetBytes(b);
        _pageB.Slots[_slotB].SetBytes(a);
        _pageA.IsDirty = true;
        _pageB.IsDirty = true;
    }
}

public sealed class OverwriteSlotAction : IUndoAction
{
    private readonly MacroPage _targetPage;
    private readonly int _targetSlot;
    private readonly byte[] _oldBytes;
    private readonly byte[] _newBytes;

    public string Description { get; }

    public OverwriteSlotAction(MacroPage targetPage, int targetSlot, byte[] newBytes, string description)
    {
        _targetPage = targetPage;
        _targetSlot = targetSlot;
        _oldBytes = targetPage.Slots[targetSlot].CloneBytes();
        _newBytes = (byte[])newBytes.Clone();
        Description = description;
    }

    public void Execute()
    {
        _targetPage.Slots[_targetSlot].SetBytes(_newBytes);
        _targetPage.IsDirty = true;
    }

    public void Undo()
    {
        _targetPage.Slots[_targetSlot].SetBytes(_oldBytes);
        _targetPage.IsDirty = true;
    }
}

public sealed class CopyBookAction : IUndoAction
{
    private readonly CharacterMacroStore _dstChar;
    private readonly int _dstBook;
    private readonly bool _copyTitle;

    private readonly byte[] _oldTitleBytes;
    private readonly byte[] _newTitleBytes;
    private readonly byte[][][] _oldPagesBytes = new byte[10][][];
    private readonly byte[][][] _newPagesBytes = new byte[10][][];

    public string Description { get; }

    public CopyBookAction(CharacterMacroStore srcChar, int srcBook, CharacterMacroStore dstChar, int dstBook, bool copyTitle)
    {
        _dstChar = dstChar;
        _dstBook = dstBook;
        _copyTitle = copyTitle;

        _oldTitleBytes = dstChar.CloneBookTitleBytes(dstBook);
        _newTitleBytes = srcChar.CloneBookTitleBytes(srcBook);

        for (int p = 0; p < 10; p++)
        {
            var srcPage = srcChar.GetPage(srcBook, p + 1);
            var dstPage = dstChar.GetPage(dstBook, p + 1);

            _oldPagesBytes[p] = new byte[MacroBinaryCodec.SlotsPerPage][];
            _newPagesBytes[p] = new byte[MacroBinaryCodec.SlotsPerPage][];

            for (int s = 0; s < MacroBinaryCodec.SlotsPerPage; s++)
            {
                _oldPagesBytes[p][s] = dstPage.Slots[s].CloneBytes();
                _newPagesBytes[p][s] = srcPage.Slots[s].CloneBytes();
            }
        }

        Description = Loc.Instance.Format("UndoCopyBook", srcChar.DisplayName, srcBook, dstChar.DisplayName, dstBook);
    }

    public void Execute() => Apply(_newPagesBytes, _newTitleBytes);
    public void Undo() => Apply(_oldPagesBytes, _oldTitleBytes);

    private void Apply(byte[][][] pagesBytes, byte[] titleBytes)
    {
        for (int p = 0; p < 10; p++)
        {
            var dstPage = _dstChar.GetPage(_dstBook, p + 1);
            for (int s = 0; s < MacroBinaryCodec.SlotsPerPage; s++)
            {
                dstPage.Slots[s].SetBytes(pagesBytes[p][s]);
            }
            dstPage.IsDirty = true;
        }

        if (_copyTitle)
        {
            _dstChar.SetBookTitleBytes(_dstBook, titleBytes);
        }
    }
}

public sealed class CopyPageAction : IUndoAction
{
    private readonly List<OverwriteSlotAction> _slotActions = new(MacroBinaryCodec.SlotsPerPage);

    public string Description { get; }

    public CopyPageAction(
        CharacterMacroStore srcChar, int srcBook, int srcPageNum,
        CharacterMacroStore dstChar, int dstBook, int dstPageNum)
    {
        var srcPage = srcChar.GetPage(srcBook, srcPageNum);
        var dstPage = dstChar.GetPage(dstBook, dstPageNum);

        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            byte[] srcBytes = srcPage.Slots[i].CloneBytes();
            _slotActions.Add(new OverwriteSlotAction(dstPage, i, srcBytes, $"CopyPage Slot {i}"));
        }

        Description = Loc.Instance.Format(
            "UndoCopyPage",
            srcChar.DisplayName, srcBook, srcPageNum,
            dstChar.DisplayName, dstBook, dstPageNum);
    }

    public void Execute()
    {
        foreach (var action in _slotActions)
        {
            action.Execute();
        }
    }

    public void Undo()
    {
        // IsDirty フラグを正確に元の状態へ戻すため逆順で取り消す
        for (int i = _slotActions.Count - 1; i >= 0; i--)
        {
            _slotActions[i].Undo();
        }
    }
}

public sealed class RenameBookAction : IUndoAction
{
    private readonly CharacterMacroStore _character;
    private readonly int _bookNumber;
    private readonly byte[] _oldTitleBytes;
    private readonly byte[] _newTitleBytes;

    public string Description { get; }

    public RenameBookAction(
        CharacterMacroStore character,
        int bookNumber,
        byte[] newTitleBytes,
        string newTitleDisplay)
    {
        _character = character;
        _bookNumber = bookNumber;
        _oldTitleBytes = character.CloneBookTitleBytes(bookNumber);
        _newTitleBytes = (byte[])newTitleBytes.Clone();

        string label = string.IsNullOrEmpty(newTitleDisplay) ? Loc.Instance["SlotUntitled"] : newTitleDisplay;
        Description = Loc.Instance.Format("UndoRenameBook", character.DisplayName, bookNumber, label);
    }

    public void Execute() => _character.SetBookTitleBytes(_bookNumber, _newTitleBytes);
    public void Undo() => _character.SetBookTitleBytes(_bookNumber, _oldTitleBytes);
}

public sealed class ClearPageAction : IUndoAction
{
    private readonly MacroPage _targetPage;
    private readonly byte[][] _oldSlots = new byte[MacroBinaryCodec.SlotsPerPage][];

    public string Description { get; }

    public ClearPageAction(CharacterMacroStore character, int bookNumber, int pageNumber)
    {
        _targetPage = character.GetPage(bookNumber, pageNumber);
        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            _oldSlots[i] = _targetPage.Slots[i].CloneBytes();
        }

        Description = Loc.Instance.Format("UndoClearPage", character.DisplayName, bookNumber, pageNumber);
    }

    public void Execute()
    {
        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            _targetPage.Slots[i].SetBytes(new byte[MacroBinaryCodec.SlotSize]);
        }
        _targetPage.IsDirty = true;
    }

    public void Undo()
    {
        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            _targetPage.Slots[i].SetBytes(_oldSlots[i]);
        }
        _targetPage.IsDirty = true;
    }
}

public sealed class ClearBookAction : IUndoAction
{
    private readonly CharacterMacroStore _character;
    private readonly int _bookNumber;
    private readonly bool _clearTitle;
    private readonly byte[] _oldTitleBytes;
    private readonly List<(MacroPage Page, byte[][] OldSlots)> _affectedPages = new();

    public string Description { get; }

    public ClearBookAction(CharacterMacroStore character, int bookNumber, bool clearTitle)
    {
        _character = character;
        _bookNumber = bookNumber;
        _clearTitle = clearTitle;
        _oldTitleBytes = character.CloneBookTitleBytes(bookNumber);

        // 実際にデータが入っているページだけをクリア対象にし、未作成 .dat の不要生成を防ぐ
        for (int p = 1; p <= 10; p++)
        {
            var page = character.GetPage(bookNumber, p);
            if (!page.Slots.Any(s => !s.IsEmpty)) continue;

            var oldSlots = new byte[MacroBinaryCodec.SlotsPerPage][];
            for (int s = 0; s < MacroBinaryCodec.SlotsPerPage; s++)
            {
                oldSlots[s] = page.Slots[s].CloneBytes();
            }
            _affectedPages.Add((page, oldSlots));
        }

        string suffix = clearTitle ? Loc.Instance["UndoClearBookTitleSuffix"] : "";
        Description = Loc.Instance.Format("UndoClearBook", character.DisplayName, bookNumber, suffix);
    }

    public void Execute()
    {
        foreach (var (page, _) in _affectedPages)
        {
            for (int s = 0; s < MacroBinaryCodec.SlotsPerPage; s++)
            {
                page.Slots[s].SetBytes(new byte[MacroBinaryCodec.SlotSize]);
            }
            page.IsDirty = true;
        }

        if (_clearTitle)
        {
            _character.SetBookTitleBytes(_bookNumber, new byte[MacroBinaryCodec.TitleRecordSize]);
        }
    }

    public void Undo()
    {
        foreach (var (page, oldSlots) in _affectedPages)
        {
            for (int s = 0; s < MacroBinaryCodec.SlotsPerPage; s++)
            {
                page.Slots[s].SetBytes(oldSlots[s]);
            }
            page.IsDirty = true;
        }

        if (_clearTitle)
        {
            _character.SetBookTitleBytes(_bookNumber, _oldTitleBytes);
        }
    }
}