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
        Description = $"入れ替え: {MacroSlot.GetKeyLabel(slotA)} ⇄ {MacroSlot.GetKeyLabel(slotB)}";
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

        Description = $"ブックコピー: {srcChar.CharacterId} Book{srcBook} → {dstChar.CharacterId} Book{dstBook}";
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