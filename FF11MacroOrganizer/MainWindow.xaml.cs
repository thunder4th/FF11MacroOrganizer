using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FF11MacroOrganizer.Core;
using Microsoft.Win32;

namespace FF11MacroOrganizer;

public partial class MainWindow : Window
{
    private sealed class PaneState
    {
        public int PaneIndex { get; init; }
        public string Label => PaneIndex == 0 ? "左" : "右";
        public required ComboBox CmbChar { get; init; }
        public required ComboBox CmbBook { get; init; }
        public required ComboBox CmbPage { get; init; }
        public required TextBlock TxtDirty { get; init; }
        public required Border[] SlotBorders { get; init; }
        public required TextBlock[] SlotTitleTexts { get; init; }
        public required TextBlock[] SlotKeyTexts { get; init; }
        public bool IsUpdating { get; set; }

        public CharacterMacroStore? CurrentCharacter { get; set; }
        public int CurrentBook { get; set; } = 1;
        public int CurrentPageNumber { get; set; } = 1;

        public MacroPage? GetCurrentPage() => CurrentCharacter?.GetPage(CurrentBook, CurrentPageNumber);
    }

    private sealed record SlotDragPayload(int PaneIndex, MacroPage Page, int SlotIndex);

    private readonly MacroRepository _repo = new();
    private readonly UndoManager _undoManager = new();
    private readonly PaneState[] _panes = new PaneState[2];
    private readonly DispatcherTimer _polTimer = new();

    private byte[]? _slotClipboard;
    private Point _dragStartPoint;
    private (int PaneIndex, int SlotIndex)? _dragOrigin;
    private (int PaneIndex, int SlotIndex)? _selectedSlot;
    private bool _isPolRunning;

    // 配色定義
    private static readonly SolidColorBrush BrushEmptyBg = new(Color.FromRgb(249, 250, 251));
    private static readonly SolidColorBrush BrushFilledBg = new(Color.FromRgb(239, 246, 255));
    private static readonly SolidColorBrush BrushNormalBorder = new(Color.FromRgb(209, 213, 219));
    private static readonly SolidColorBrush BrushFilledBorder = new(Color.FromRgb(147, 197, 253));
    private static readonly SolidColorBrush BrushSelectedBorder = new(Color.FromRgb(37, 99, 235));
    private static readonly SolidColorBrush BrushDropSwapBg = new(Color.FromRgb(191, 219, 254));
    private static readonly SolidColorBrush BrushDropSwapBorder = new(Color.FromRgb(37, 99, 235));
    private static readonly SolidColorBrush BrushDropCopyBg = new(Color.FromRgb(254, 215, 170));
    private static readonly SolidColorBrush BrushDropCopyBorder = new(Color.FromRgb(234, 88, 12));

    public MainWindow()
    {
        InitializeComponent();

        _panes[0] = InitializePane(0, CmbLeftChar, CmbLeftBook, CmbLeftPage, TxtLeftDirty, GridLeftSlots);
        _panes[1] = InitializePane(1, CmbRightChar, CmbRightBook, CmbRightPage, TxtRightDirty, GridRightSlots);

        // 両ペインの生成が完了してからページの初期選択を行う
        foreach (var pane in _panes)
        {
            pane.IsUpdating = true;
            pane.CmbPage.SelectedIndex = 0;
            pane.IsUpdating = false;
        }

        _polTimer.Interval = TimeSpan.FromSeconds(2);
        _polTimer.Tick += (_, _) => UpdatePolStatus();
        _polTimer.Start();
        UpdatePolStatus();
    }

    private PaneState InitializePane(
        int paneIndex,
        ComboBox cmbChar,
        ComboBox cmbBook,
        ComboBox cmbPage,
        TextBlock txtDirty,
        System.Windows.Controls.Primitives.UniformGrid grid)
    {
        var borders = new Border[MacroBinaryCodec.SlotsPerPage];
        var keyTexts = new TextBlock[MacroBinaryCodec.SlotsPerPage];
        var titleTexts = new TextBlock[MacroBinaryCodec.SlotsPerPage];

        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            int slotIndex = i;

            var keyText = new TextBlock
            {
                Text = i < 10 ? $"C{(i + 1) % 10}" : $"A{(i - 9) % 10}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };

            var titleText = new TextBlock
            {
                Text = "",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                IsHitTestVisible = false
            };

            var titleBox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                IsHitTestVisible = false,
                Child = titleText
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };
            stack.Children.Add(keyText);
            stack.Children.Add(titleBox);

            var border = new Border
            {
                Margin = new Thickness(2),
                Padding = new Thickness(4, 3, 4, 3),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1.5),
                Background = BrushEmptyBg,
                BorderBrush = BrushNormalBorder,
                AllowDrop = true,
                Cursor = Cursors.Hand,
                Child = stack,
                ContextMenu = CreateSlotContextMenu(paneIndex, slotIndex)
            };

            border.PreviewMouseLeftButtonDown += (s, e) => Slot_PreviewMouseLeftButtonDown(paneIndex, slotIndex, e);
            border.MouseMove += (s, e) => Slot_MouseMove(paneIndex, slotIndex, e);
            border.MouseEnter += (_, _) => ShowPreview(paneIndex, slotIndex);
            border.DragOver += (s, e) => Slot_DragOver(paneIndex, slotIndex, e);
            border.DragLeave += (_, _) => ResetSlotVisual(paneIndex, slotIndex);
            border.Drop += (s, e) => Slot_Drop(paneIndex, slotIndex, e);

            borders[i] = border;
            keyTexts[i] = keyText;
            titleTexts[i] = titleText;
            grid.Children.Add(border);
        }

        cmbPage.Items.Clear();
        for (int p = 1; p <= 10; p++)
        {
            cmbPage.Items.Add($"Page {p}");
        }

        return new PaneState
        {
            PaneIndex = paneIndex,
            CmbChar = cmbChar,
            CmbBook = cmbBook,
            CmbPage = cmbPage,
            TxtDirty = txtDirty,
            SlotBorders = borders,
            SlotKeyTexts = keyTexts,
            SlotTitleTexts = titleTexts
        };
    }

    private ContextMenu CreateSlotContextMenu(int paneIndex, int slotIndex)
    {
        var menu = new ContextMenu();

        var miCopy = new MenuItem { Header = "コピー" };
        miCopy.Click += (_, _) => CopySlotToClipboard(paneIndex, slotIndex);

        var miPaste = new MenuItem { Header = "貼り付け（上書き）" };
        miPaste.Click += (_, _) => PasteSlotFromClipboard(paneIndex, slotIndex);

        var miClear = new MenuItem { Header = "クリア（空にする）" };
        miClear.Click += (_, _) => ClearSlot(paneIndex, slotIndex);

        menu.Opened += (_, _) =>
        {
            bool hasPage = _panes[paneIndex].GetCurrentPage() != null;
            miCopy.IsEnabled = hasPage;
            miPaste.IsEnabled = hasPage && _slotClipboard != null;
            miClear.IsEnabled = hasPage;
        };

        menu.Items.Add(miCopy);
        menu.Items.Add(miPaste);
        menu.Items.Add(new Separator());
        menu.Items.Add(miClear);
        return menu;
    }

    // ---------- フォルダ読み込み ----------

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_repo.HasUnsavedChanges)
        {
            var confirm = MessageBox.Show(
                "未保存の変更があります。破棄して別フォルダを読み込みますか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "FF11 の USER フォルダ（またはキャラフォルダ）を選択"
        };

        const string defaultFf11UserDir = @"C:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI\USER";
        if (Directory.Exists(defaultFf11UserDir))
        {
            dialog.InitialDirectory = defaultFf11UserDir;
        }

        if (dialog.ShowDialog() != true) return;

        try
        {
            _repo.LoadFolder(dialog.FolderName);
            _undoManager.Clear();
            _selectedSlot = null;

            if (_repo.Characters.Count == 0)
            {
                MessageBox.Show(
                    "指定されたフォルダ内に FF11 のマクロファイル（mcr.ttl / mcr*.dat）が見つかりませんでした。",
                    "読み込み失敗",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            BindCharactersToPane(_panes[0], 0);
            BindCharactersToPane(_panes[1], _repo.Characters.Count > 1 ? 1 : 0);

            RefreshAllUi();
            TxtStatusMessage.Text = $"読み込み完了: {_repo.Characters.Count} キャラ検出 ({_repo.RootPath})";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"読み込み中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BindCharactersToPane(PaneState pane, int defaultCharIndex)
    {
        pane.IsUpdating = true;
        try
        {
            pane.CmbChar.Items.Clear();
            foreach (var ch in _repo.Characters)
            {
                pane.CmbChar.Items.Add(ch.DisplayName);
            }

            if (_repo.Characters.Count > 0)
            {
                int idx = Math.Clamp(defaultCharIndex, 0, _repo.Characters.Count - 1);
                pane.CmbChar.SelectedIndex = idx;
                pane.CurrentCharacter = _repo.Characters[idx];
                pane.CurrentBook = 1;
                pane.CurrentPageNumber = 1;
                pane.CmbPage.SelectedIndex = 0;
                RefreshBookComboBox(pane);
            }
            else
            {
                pane.CurrentCharacter = null;
                pane.CmbBook.Items.Clear();
            }
        }
        finally
        {
            pane.IsUpdating = false;
        }
    }

    private static void RefreshBookComboBox(PaneState pane)
    {
        bool prevUpdating = pane.IsUpdating;
        pane.IsUpdating = true;
        try
        {
            pane.CmbBook.Items.Clear();
            if (pane.CurrentCharacter == null) return;

            for (int b = 1; b <= 40; b++)
            {
                string title = pane.CurrentCharacter.GetBookTitle(b);
                pane.CmbBook.Items.Add($"{b:D2}: {title}");
            }
            pane.CmbBook.SelectedIndex = Math.Clamp(pane.CurrentBook - 1, 0, 39);
        }
        finally
        {
            pane.IsUpdating = prevUpdating;
        }
    }

    // ---------- セレクタ変更イベント ----------

    private void LeftChar_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnCharChanged(_panes[0]);
    private void RightChar_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnCharChanged(_panes[1]);

    private void OnCharChanged(PaneState pane)
    {
        if (pane.IsUpdating || pane.CmbChar.SelectedIndex < 0) return;
        pane.CurrentCharacter = _repo.Characters[pane.CmbChar.SelectedIndex];
        RefreshBookComboBox(pane);
        RefreshAllUi();
    }

    private void LeftBook_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnBookChanged(_panes[0]);
    private void RightBook_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnBookChanged(_panes[1]);

    private void OnBookChanged(PaneState pane)
    {
        if (pane.IsUpdating || pane.CmbBook.SelectedIndex < 0) return;
        pane.CurrentBook = pane.CmbBook.SelectedIndex + 1;
        RefreshAllUi();
    }

    private void LeftPage_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnPageChanged(_panes[0]);
    private void RightPage_SelectionChanged(object sender, SelectionChangedEventArgs e) => OnPageChanged(_panes[1]);

    private void OnPageChanged(PaneState pane)
    {
        if (pane.IsUpdating || pane.CmbPage.SelectedIndex < 0) return;
        pane.CurrentPageNumber = pane.CmbPage.SelectedIndex + 1;
        RefreshAllUi();
    }

    private void LeftPrevPage_Click(object sender, RoutedEventArgs e) => StepPage(_panes[0], -1);
    private void LeftNextPage_Click(object sender, RoutedEventArgs e) => StepPage(_panes[0], +1);
    private void RightPrevPage_Click(object sender, RoutedEventArgs e) => StepPage(_panes[1], -1);
    private void RightNextPage_Click(object sender, RoutedEventArgs e) => StepPage(_panes[1], +1);

    private void StepPage(PaneState pane, int delta)
    {
        if (pane.CurrentCharacter == null) return;
        int next = ((pane.CurrentPageNumber - 1 + delta + 10) % 10) + 1;
        pane.CmbPage.SelectedIndex = next - 1;
    }

    // ---------- ドラッグ＆ドロップ処理 ----------

    private void Slot_PreviewMouseLeftButtonDown(int paneIndex, int slotIndex, MouseButtonEventArgs e)
    {
        if (_panes[paneIndex].GetCurrentPage() == null) return;

        _dragStartPoint = e.GetPosition(this);
        _dragOrigin = (paneIndex, slotIndex);
        _selectedSlot = (paneIndex, slotIndex);
        RefreshPaneVisuals(_panes[0]);
        RefreshPaneVisuals(_panes[1]);
        ShowPreview(paneIndex, slotIndex);
    }

    private void Slot_MouseMove(int paneIndex, int slotIndex, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragOrigin != (paneIndex, slotIndex)) return;

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var page = _panes[paneIndex].GetCurrentPage();
        if (page == null) return;

        var payload = new SlotDragPayload(paneIndex, page, slotIndex);
        var data = new DataObject(typeof(SlotDragPayload), payload);
        _dragOrigin = null;

        DragDrop.DoDragDrop(_panes[paneIndex].SlotBorders[slotIndex], data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void Slot_DragOver(int targetPaneIndex, int targetSlotIndex, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SlotDragPayload)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var targetPage = _panes[targetPaneIndex].GetCurrentPage();
        var payload = (SlotDragPayload)e.Data.GetData(typeof(SlotDragPayload));

        if (targetPage == null || (ReferenceEquals(payload.Page, targetPage) && payload.SlotIndex == targetSlotIndex))
        {
            e.Effects = DragDropEffects.None;
            ResetSlotVisual(targetPaneIndex, targetSlotIndex);
            e.Handled = true;
            return;
        }

        bool isCopy = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
        e.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;

        var border = _panes[targetPaneIndex].SlotBorders[targetSlotIndex];
        border.Background = isCopy ? BrushDropCopyBg : BrushDropSwapBg;
        border.BorderBrush = isCopy ? BrushDropCopyBorder : BrushDropSwapBorder;
        e.Handled = true;
    }

    private void Slot_Drop(int targetPaneIndex, int targetSlotIndex, DragEventArgs e)
    {
        ResetSlotVisual(targetPaneIndex, targetSlotIndex);

        if (!e.Data.GetDataPresent(typeof(SlotDragPayload))) return;

        var targetPage = _panes[targetPaneIndex].GetCurrentPage();
        var payload = (SlotDragPayload)e.Data.GetData(typeof(SlotDragPayload));
        if (targetPage == null) return;
        if (ReferenceEquals(payload.Page, targetPage) && payload.SlotIndex == targetSlotIndex) return;

        bool isCopy = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);

        if (isCopy)
        {
            byte[] srcBytes = payload.Page.Slots[payload.SlotIndex].CloneBytes();
            string desc = $"上書きコピー: {MacroSlot.GetKeyLabel(payload.SlotIndex)} → {MacroSlot.GetKeyLabel(targetSlotIndex)}";
            _undoManager.Execute(new OverwriteSlotAction(targetPage, targetSlotIndex, srcBytes, desc));
            TxtStatusMessage.Text = desc;
        }
        else
        {
            var action = new SwapSlotAction(payload.Page, payload.SlotIndex, targetPage, targetSlotIndex);
            _undoManager.Execute(action);
            TxtStatusMessage.Text = action.Description;
        }

        _selectedSlot = (targetPaneIndex, targetSlotIndex);
        RefreshAllUi();
        ShowPreview(targetPaneIndex, targetSlotIndex);
        e.Handled = true;
    }

    // ---------- 右クリック操作 ----------

    private void CopySlotToClipboard(int paneIndex, int slotIndex)
    {
        var page = _panes[paneIndex].GetCurrentPage();
        if (page == null) return;
        _slotClipboard = page.Slots[slotIndex].CloneBytes();
        TxtStatusMessage.Text = $"コピーしました: {MacroSlot.GetKeyLabel(slotIndex)} ({page.Slots[slotIndex].Title})";
    }

    private void PasteSlotFromClipboard(int paneIndex, int slotIndex)
    {
        var page = _panes[paneIndex].GetCurrentPage();
        if (page == null || _slotClipboard == null) return;

        string desc = $"貼り付け: {MacroSlot.GetKeyLabel(slotIndex)}";
        _undoManager.Execute(new OverwriteSlotAction(page, slotIndex, _slotClipboard, desc));
        TxtStatusMessage.Text = desc;
        RefreshAllUi();
        ShowPreview(paneIndex, slotIndex);
    }

    private void ClearSlot(int paneIndex, int slotIndex)
    {
        var page = _panes[paneIndex].GetCurrentPage();
        if (page == null) return;

        var empty = new byte[MacroBinaryCodec.SlotSize];
        string desc = $"クリア: {MacroSlot.GetKeyLabel(slotIndex)}";
        _undoManager.Execute(new OverwriteSlotAction(page, slotIndex, empty, desc));
        TxtStatusMessage.Text = desc;
        RefreshAllUi();
        ShowPreview(paneIndex, slotIndex);
    }

    // ---------- ブック単位コピー ----------

    private void BtnCopyBookLeftToRight_Click(object sender, RoutedEventArgs e) => ExecuteBookCopy(_panes[0], _panes[1]);
    private void BtnCopyBookRightToLeft_Click(object sender, RoutedEventArgs e) => ExecuteBookCopy(_panes[1], _panes[0]);

    private void ExecuteBookCopy(PaneState srcPane, PaneState dstPane)
    {
        if (srcPane.CurrentCharacter == null || dstPane.CurrentCharacter == null) return;
        if (ReferenceEquals(srcPane.CurrentCharacter, dstPane.CurrentCharacter) && srcPane.CurrentBook == dstPane.CurrentBook)
        {
            return;
        }

        string srcLabel = $"{srcPane.CurrentCharacter.DisplayName} / Book {srcPane.CurrentBook:D2} ({srcPane.CurrentCharacter.GetBookTitle(srcPane.CurrentBook)})";
        string dstLabel = $"{dstPane.CurrentCharacter.DisplayName} / Book {dstPane.CurrentBook:D2} ({dstPane.CurrentCharacter.GetBookTitle(dstPane.CurrentBook)})";

        var (confirmed, copyTitle) = ShowBookCopyDialog(srcLabel, dstLabel);
        if (!confirmed) return;

        var action = new CopyBookAction(
            srcPane.CurrentCharacter, srcPane.CurrentBook,
            dstPane.CurrentCharacter, dstPane.CurrentBook,
            copyTitle);

        _undoManager.Execute(action);

        // ブック名変更を両ペインのコンボボックスへ反映
        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = $"{action.Description} を実行しました（未保存状態・Ctrl+Zで取り消し可能）";
    }

    private (bool Confirmed, bool CopyTitle) ShowBookCopyDialog(string srcLabel, string dstLabel)
    {
        var dlg = new Window
        {
            Title = "ブック単位コピーの確認",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "コピー先ブックの全10ページ（200マクロ）を上書きします。\nよろしいですか？",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        root.Children.Add(new TextBlock { Text = $"コピー元: {srcLabel}", Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new TextBlock { Text = $"コピー先: {dstLabel}", Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28)), Margin = new Thickness(0, 0, 0, 14) });

        var chkCopyTitle = new CheckBox
        {
            Content = "ブック名も一緒にコピーする",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(chkCopyTitle);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Button { Content = "キャンセル", Width = 90, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var btnOk = new Button
        {
            Content = "コピー実行",
            Width = 100,
            Padding = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsDefault = true
        };
        btnOk.Click += (_, _) => dlg.DialogResult = true;

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);
        dlg.Content = root;

        bool result = dlg.ShowDialog() == true;
        return (result, chkCopyTitle.IsChecked == true);
    }

    // ---------- Undo / Redo / 保存 ----------

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undoManager.CanUndo) return;
        string? desc = _undoManager.NextUndoDescription;
        _undoManager.Undo();
        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = $"元に戻しました: {desc}";
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undoManager.CanRedo) return;
        string? desc = _undoManager.NextRedoDescription;
        _undoManager.Redo();
        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = $"やり直しました: {desc}";
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        UpdatePolStatus();
        bool ignoreLock = ChkIgnorePolLock.IsChecked == true;
        string backupDir = Path.Combine(AppContext.BaseDirectory, "Backups");

        var result = _repo.SaveAll(backupDir, ignoreLock);
        if (result.Success)
        {
            RefreshAllUi();
            string msg = result.SavedFileCount > 0
                ? $"{result.Message}\n\nバックアップ先:\n{result.BackupPath}"
                : result.Message;
            TxtStatusMessage.Text = result.Message;
            MessageBox.Show(msg, "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(result.Message, "保存中止", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- UI更新・プレビュー・pol.exe監視 ----------

    private void RefreshAllUi()
    {
        RefreshPaneVisuals(_panes[0]);
        RefreshPaneVisuals(_panes[1]);

        BtnUndo.IsEnabled = _undoManager.CanUndo;
        BtnUndo.ToolTip = _undoManager.NextUndoDescription;
        BtnRedo.IsEnabled = _undoManager.CanRedo;
        BtnRedo.ToolTip = _undoManager.NextRedoDescription;

        bool canBookCopy = _panes[0].CurrentCharacter != null &&
                           _panes[1].CurrentCharacter != null &&
                           !(ReferenceEquals(_panes[0].CurrentCharacter, _panes[1].CurrentCharacter) &&
                             _panes[0].CurrentBook == _panes[1].CurrentBook);

        BtnCopyBookLeftToRight.IsEnabled = canBookCopy;
        BtnCopyBookRightToLeft.IsEnabled = canBookCopy;

        UpdateSaveButtonState();

        if (_selectedSlot.HasValue)
        {
            ShowPreview(_selectedSlot.Value.PaneIndex, _selectedSlot.Value.SlotIndex);
        }
    }

    private void RefreshPaneVisuals(PaneState pane)
    {
        var page = pane.GetCurrentPage();
        if (page == null)
        {
            pane.TxtDirty.Text = "";
            for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
            {
                pane.SlotTitleTexts[i].Text = "";
                pane.SlotBorders[i].Background = BrushEmptyBg;
                pane.SlotBorders[i].BorderBrush = BrushNormalBorder;
            }
            return;
        }

        pane.TxtDirty.Text = page.IsDirty ? "* 未保存の変更あり" : "";

        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            var slot = page.Slots[i];
            pane.SlotTitleTexts[i].Text = slot.IsEmpty ? "" : (string.IsNullOrEmpty(slot.Title) ? "(無題)" : slot.Title);
            ResetSlotVisual(pane.PaneIndex, i);
        }
    }

    private void ResetSlotVisual(int paneIndex, int slotIndex)
    {
        var page = _panes[paneIndex].GetCurrentPage();
        var border = _panes[paneIndex].SlotBorders[slotIndex];
        if (page == null)
        {
            border.Background = BrushEmptyBg;
            border.BorderBrush = BrushNormalBorder;
            return;
        }

        bool isEmpty = page.Slots[slotIndex].IsEmpty;
        bool isSelected = _selectedSlot == (paneIndex, slotIndex);

        border.Background = isEmpty ? BrushEmptyBg : BrushFilledBg;
        border.BorderBrush = isSelected
            ? BrushSelectedBorder
            : (isEmpty ? BrushNormalBorder : BrushFilledBorder);
    }

    private void ShowPreview(int paneIndex, int slotIndex)
    {
        var pane = _panes[paneIndex];
        var page = pane.GetCurrentPage();
        if (page == null || pane.CurrentCharacter == null) return;

        var slot = page.Slots[slotIndex];
        string bookTitle = pane.CurrentCharacter.GetBookTitle(pane.CurrentBook);
        string titleText = slot.IsEmpty ? "(空スロット)" : (string.IsNullOrEmpty(slot.Title) ? "(無題)" : slot.Title);

        TxtPreviewHeader.Text =
            $"[{pane.Label}] {pane.CurrentCharacter.DisplayName} / Book {pane.CurrentBook:D2} ({bookTitle}) / Page {pane.CurrentPageNumber} / {MacroSlot.GetKeyLabel(slotIndex)}  —  {titleText}";

        string[] lines = slot.GetLines();
        TxtPreviewBody.Text = string.Join(Environment.NewLine, lines.Select((line, idx) => $"{idx + 1}: {line}"));
    }

    private void UpdatePolStatus()
    {
        _isPolRunning = MacroRepository.IsPolRunning();
        if (_isPolRunning)
        {
            PolStatusBadge.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226));
            PolStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165));
            TxtPolStatus.Foreground = new SolidColorBrush(Color.FromRgb(153, 27, 27));
            TxtPolStatus.Text = "● FF11起動中 (保存ロック)";
        }
        else
        {
            PolStatusBadge.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            PolStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
            TxtPolStatus.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
            TxtPolStatus.Text = "● FF11未起動 (保存可能)";
        }

        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        int dirtyCount = _repo.CountDirtyFiles();
        bool locked = _isPolRunning && ChkIgnorePolLock.IsChecked != true;

        BtnSave.Content = dirtyCount > 0 ? $"変更を保存 ({dirtyCount}件)" : "変更を保存";
        BtnSave.IsEnabled = dirtyCount > 0 && !locked;
        BtnSave.Background = BtnSave.IsEnabled
            ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
            : new SolidColorBrush(Color.FromRgb(229, 231, 235));
        BtnSave.Foreground = BtnSave.IsEnabled
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(107, 114, 128));
        BtnSave.Opacity = 1.0;
    }

    private void ChkIgnorePolLock_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSaveButtonState();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        if (e.Key == Key.Z && BtnUndo.IsEnabled)
        {
            BtnUndo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Y && BtnRedo.IsEnabled)
        {
            BtnRedo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.S && BtnSave.IsEnabled)
        {
            BtnSave_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_repo.HasUnsavedChanges)
        {
            var res = MessageBox.Show(
                "未保存の変更があります。破棄して終了しますか？",
                "終了確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _polTimer.Stop();
    }
}