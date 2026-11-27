using FF11MacroOrganizer.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static FF11MacroOrganizer.Core.CopyBookAction;

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
        public required Border PreviewBorder { get; init; }
        public required TextBlock TxtPreviewHeader { get; init; }
        public required TextBlock TxtLineNumbers { get; init; }
        public required TextBox TxtPreviewBody { get; init; }
        public required Button BtnEditSlot { get; init; }
        public required Button BtnRenameBook { get; init; }
        public bool IsUpdating { get; set; }

        public CharacterMacroStore? CurrentCharacter { get; set; }
        public int CurrentBook { get; set; } = 1;
        public int CurrentPageNumber { get; set; } = 1;
        public int? SelectedSlotIndex { get; set; }

        public MacroPage? GetCurrentPage() => CurrentCharacter?.GetPage(CurrentBook, CurrentPageNumber);
    }

    private sealed record SlotDragPayload(int PaneIndex, MacroPage Page, int SlotIndex);

    private sealed class DragAdorner : Adorner
    {
        private readonly VisualBrush _brush;
        private readonly Size _size;
        private readonly Point _offset;
        private Point _location;

        public DragAdorner(UIElement adornedElement, UIElement draggedElement, Point offset, Point initialLocation)
            : base(adornedElement)
        {
            _brush = new VisualBrush(draggedElement) { Opacity = 0.65 };
            _size = draggedElement.RenderSize;
            _offset = offset;
            _location = initialLocation;
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point location)
        {
            _location = location;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(
                _location.X - _offset.X,
                _location.Y - _offset.Y,
                _size.Width,
                _size.Height);
            drawingContext.DrawRectangle(_brush, null, rect);
        }
    }

    private readonly MacroRepository _repo = new();
    private readonly UndoManager _undoManager = new();
    private readonly PaneState[] _panes = new PaneState[2];
    private readonly DispatcherTimer _polTimer = new();

    private byte[]? _slotClipboard;
    private Point _dragStartPoint;
    private Point _dragElementOffset;
    private (int PaneIndex, int SlotIndex)? _dragOrigin;
    private DragAdorner? _dragAdorner;
    private int _activePaneIndex;
    private bool _isPolRunning;
    private bool _isSinglePane;
    private double _twoPaneWindowWidth = 1320;
    private static readonly string LastFolderSettingPath = Path.Combine(AppContext.BaseDirectory, "last_folder.txt");
    private static readonly string PaneModeSettingPath = Path.Combine(AppContext.BaseDirectory, "pane_mode.txt");

    // 配色定義
    private static readonly SolidColorBrush BrushEmptyBg = new(Color.FromRgb(249, 250, 251));
    private static readonly SolidColorBrush BrushFilledBg = new(Color.FromRgb(239, 246, 255));
    private static readonly SolidColorBrush BrushActiveSelectedBg = new(Color.FromRgb(37, 99, 235));
    private static readonly SolidColorBrush BrushActiveSelectedBorder = new(Color.FromRgb(30, 58, 138));
    private static readonly SolidColorBrush BrushInactiveSelectedBg = new(Color.FromRgb(226, 232, 240));
    private static readonly SolidColorBrush BrushInactiveSelectedBorder = new(Color.FromRgb(100, 116, 139));
    private static readonly SolidColorBrush BrushNormalBorder = new(Color.FromRgb(209, 213, 219));
    private static readonly SolidColorBrush BrushFilledBorder = new(Color.FromRgb(147, 197, 253));
    private static readonly SolidColorBrush BrushNormalTitleFg = new(Color.FromRgb(17, 24, 39));
    private static readonly SolidColorBrush BrushNormalKeyFg = new(Color.FromRgb(107, 114, 128));
    private static readonly SolidColorBrush BrushInactiveSelectedKeyFg = new(Color.FromRgb(71, 85, 105));
    private static readonly SolidColorBrush BrushSelectedTitleFg = Brushes.White;
    private static readonly SolidColorBrush BrushSelectedKeyFg = new(Color.FromRgb(219, 234, 254));
    private static readonly SolidColorBrush BrushDropSwapBg = new(Color.FromRgb(191, 219, 254));
    private static readonly SolidColorBrush BrushDropSwapBorder = new(Color.FromRgb(37, 99, 235));
    private static readonly SolidColorBrush BrushDropCopyBg = new(Color.FromRgb(254, 215, 170));
    private static readonly SolidColorBrush BrushDropCopyBorder = new(Color.FromRgb(234, 88, 12));
    private static readonly SolidColorBrush BrushActivePreviewBorder = new(Color.FromRgb(37, 99, 235));
    private static readonly SolidColorBrush BrushInactivePreviewHeaderFg = new(Color.FromRgb(100, 116, 139));

    public MainWindow()
    {
        InitializeComponent();

        // スロット以外の領域ではドロップ不可カーソルにする
        RootGrid.DragOver += RootGrid_DragOver;

        _panes[0] = InitializePane(
                    0, CmbLeftChar, CmbLeftBook, CmbLeftPage, TxtLeftDirty, GridLeftSlots,
                    LeftPreviewBorder, TxtLeftPreviewHeader, TxtLeftLineNumbers, TxtLeftPreviewBody,
                    BtnEditLeftSlot, BtnRenameLeftBook);

        _panes[1] = InitializePane(
            1, CmbRightChar, CmbRightBook, CmbRightPage, TxtRightDirty, GridRightSlots,
            RightPreviewBorder, TxtRightPreviewHeader, TxtRightLineNumbers, TxtRightPreviewBody,
            BtnEditRightSlot, BtnRenameRightBook);
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
        MacroBinaryCodec.Dictionary.TryLoadFromHintPath(null);
        ApplyPaneMode(LoadPaneMode(), isStartup: true);
        Loaded += MainWindow_Loaded;
    }

    private PaneState InitializePane(
        int paneIndex,
        ComboBox cmbChar,
        ComboBox cmbBook,
        ComboBox cmbPage,
        TextBlock txtDirty,
        UniformGrid grid,
        Border previewBorder,
        TextBlock txtPreviewHeader,
        TextBlock txtLineNumbers,
        TextBox txtPreviewBody,
        Button btnEditSlot,
        Button btnRenameBook)
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
                Foreground = BrushNormalKeyFg,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };

            var titleText = new TextBlock
            {
                Text = "",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushNormalTitleFg,
                IsHitTestVisible = false
            };

            var titleBox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = titleText
            };

            var innerGrid = new Grid { IsHitTestVisible = false };
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(keyText, 0);
            Grid.SetRow(titleBox, 1);
            innerGrid.Children.Add(keyText);
            innerGrid.Children.Add(titleBox);

            var border = new Border
            {
                Margin = new Thickness(2),
                Padding = new Thickness(4, 3, 4, 4),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1.5),
                Background = BrushEmptyBg,
                BorderBrush = BrushNormalBorder,
                AllowDrop = true,
                Cursor = Cursors.Hand,
                Child = innerGrid,
                ContextMenu = CreateSlotContextMenu(paneIndex, slotIndex)
            };

            border.PreviewMouseLeftButtonDown += (s, e) => Slot_PreviewMouseLeftButtonDown(paneIndex, slotIndex, e);
            border.MouseMove += (s, e) => Slot_MouseMove(paneIndex, slotIndex, e);
            border.MouseEnter += (_, _) => ShowPreview(paneIndex, slotIndex, isHover: true);
            border.MouseLeave += (_, _) => RefreshPanePreview(paneIndex);
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
            SlotTitleTexts = titleTexts,
            PreviewBorder = previewBorder,
            TxtPreviewHeader = txtPreviewHeader,
            TxtLineNumbers = txtLineNumbers,
            TxtPreviewBody = txtPreviewBody,
            BtnEditSlot = btnEditSlot,
            BtnRenameBook = btnRenameBook,
        };
    }

    private ContextMenu CreateSlotContextMenu(int paneIndex, int slotIndex)
    {
        var menu = new ContextMenu();

        var miEdit = new MenuItem { Header = "編集... (ダブルクリック / F2)", FontWeight = FontWeights.Bold };
        miEdit.Click += (_, _) => OpenSlotEditor(paneIndex, slotIndex);

        var miCopy = new MenuItem { Header = "コピー" };
        miCopy.Click += (_, _) => CopySlotToClipboard(paneIndex, slotIndex);

        var miPaste = new MenuItem { Header = "貼り付け（上書き）" };
        miPaste.Click += (_, _) => PasteSlotFromClipboard(paneIndex, slotIndex);

        var miClear = new MenuItem { Header = "クリア（空にする） (Delete)" };
        miClear.Click += (_, _) => ClearSlot(paneIndex, slotIndex);

        menu.Opened += (_, _) =>
        {
            bool hasPage = _panes[paneIndex].GetCurrentPage() != null;
            miEdit.IsEnabled = hasPage;
            miCopy.IsEnabled = hasPage;
            miPaste.IsEnabled = hasPage && _slotClipboard != null;
            miClear.IsEnabled = hasPage;
        };

        menu.Items.Add(miEdit);
        menu.Items.Add(new Separator());
        menu.Items.Add(miCopy);
        menu.Items.Add(miPaste);
        menu.Items.Add(new Separator());
        menu.Items.Add(miClear);
        return menu;
    }

    // ---------- フォルダ読み込み ----------

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        string? initialDir = LoadLastFolderPath();

        if (string.IsNullOrEmpty(initialDir) || !Directory.Exists(initialDir))
        {
            initialDir = Ff11InstallDetector.DetectDefaultUserDir();
        }

        if (!string.IsNullOrEmpty(initialDir))
        {
            OpenMacroFolder(initialDir, isAutoLoad: true);
        }
    }

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

        string? startDir = _repo.RootPath;
        if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
        {
            startDir = Ff11InstallDetector.DetectDefaultUserDir();
        }
        if (!string.IsNullOrEmpty(startDir))
        {
            dialog.InitialDirectory = startDir;
        }

        if (dialog.ShowDialog() != true) return;

        OpenMacroFolder(dialog.FolderName, isAutoLoad: false);
    }

    private void OpenMacroFolder(string folderPath, bool isAutoLoad)
    {
        try
        {
            _repo.LoadFolder(folderPath);
            MacroBinaryCodec.Dictionary.TryLoadFromHintPath(folderPath);
            _undoManager.Clear();
            _panes[0].SelectedSlotIndex = null;
            _panes[1].SelectedSlotIndex = null;
            _activePaneIndex = 0;

            if (_repo.Characters.Count == 0)
            {
                if (!isAutoLoad)
                {
                    MessageBox.Show(
                        "指定されたフォルダ内に FF11 のマクロファイル（mcr.ttl / mcr*.dat）が見つかりませんでした。",
                        "読み込み失敗",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            BindCharactersToPane(_panes[0], 0);
            BindCharactersToPane(_panes[1], _repo.Characters.Count > 1 ? 1 : 0);

            RefreshAllUi();
            SaveLastFolderPath(folderPath);

            string dictNote = MacroBinaryCodec.Dictionary.Count > 0
                ? $" / 定型文辞書 {MacroBinaryCodec.Dictionary.Count} 件読込済"
                : "";
            TxtStatusMessage.Text = $"読み込み完了: {_repo.Characters.Count} キャラ検出 ({_repo.RootPath}){dictNote}";
        }
        catch (Exception ex)
        {
            if (!isAutoLoad)
            {
                MessageBox.Show($"読み込み中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                TxtStatusMessage.Text = $"自動読み込みをスキップしました: {ex.Message}";
            }
        }
    }

    private static string? LoadLastFolderPath()
    {
        try
        {
            if (!File.Exists(LastFolderSettingPath)) return null;
            string path = File.ReadAllText(LastFolderSettingPath).Trim();
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveLastFolderPath(string folderPath)
    {
        try
        {
            File.WriteAllText(LastFolderSettingPath, folderPath);
        }
        catch
        {
            // 設定ファイルの保存失敗は無視して動作を継続
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

    // ---------- 1ペイン / 2ペイン モード切替 ----------

    private void RbSinglePane_Click(object sender, RoutedEventArgs e)
    {
        if (_isSinglePane) return;
        ApplyPaneMode(singlePane: true, isStartup: false);
        SavePaneMode(true);
    }

    private void RbDualPane_Click(object sender, RoutedEventArgs e)
    {
        if (!_isSinglePane) return;
        ApplyPaneMode(singlePane: false, isStartup: false);
        SavePaneMode(false);
    }

    private void ApplyPaneMode(bool singlePane, bool isStartup)
    {
        _isSinglePane = singlePane;
        RbSinglePane.IsChecked = singlePane;
        RbDualPane.IsChecked = !singlePane;

        if (singlePane)
        {
            if (!isStartup && WindowState == WindowState.Normal)
            {
                _twoPaneWindowWidth = Width;
            }

            CenterCopyPanel.Visibility = Visibility.Collapsed;
            RightPaneBorder.Visibility = Visibility.Collapsed;
            RightPreviewBorder.Visibility = Visibility.Collapsed;

            ColCenter.Width = new GridLength(0);
            ColRight.Width = new GridLength(0);
            ColPreviewGap.Width = new GridLength(0);
            ColPreviewRight.Width = new GridLength(0);

            TxtLeftPaneTitle.Text = "マクロパレット";
            _activePaneIndex = 0;

            MinWidth = 700;
            if (WindowState == WindowState.Normal)
            {
                Width = Math.Max(760, Math.Round(_twoPaneWindowWidth * 0.56));
            }
        }
        else
        {
            CenterCopyPanel.Visibility = Visibility.Visible;
            RightPaneBorder.Visibility = Visibility.Visible;
            RightPreviewBorder.Visibility = Visibility.Visible;

            ColCenter.Width = new GridLength(122);
            ColRight.Width = new GridLength(1, GridUnitType.Star);
            ColPreviewGap.Width = new GridLength(10);
            ColPreviewRight.Width = new GridLength(1, GridUnitType.Star);

            TxtLeftPaneTitle.Text = "左パレット";

            MinWidth = 1080;
            if (!isStartup && WindowState == WindowState.Normal)
            {
                Width = Math.Max(1080, _twoPaneWindowWidth);
            }
        }

        if (!isStartup)
        {
            RefreshAllUi();
        }
    }

    private static bool LoadPaneMode()
    {
        try
        {
            if (!File.Exists(PaneModeSettingPath)) return false;
            return string.Equals(File.ReadAllText(PaneModeSettingPath).Trim(), "single", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SavePaneMode(bool isSinglePane)
    {
        try
        {
            File.WriteAllText(PaneModeSettingPath, isSinglePane ? "single" : "dual");
        }
        catch
        {
            // 設定ファイルの保存失敗は無視して動作を継続
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

    private void BtnRenameLeftBook_Click(object sender, RoutedEventArgs e) => OpenBookRenameDialog(_panes[0]);
    private void BtnRenameRightBook_Click(object sender, RoutedEventArgs e) => OpenBookRenameDialog(_panes[1]);

    private void OpenBookRenameDialog(PaneState pane)
    {
        var ch = pane.CurrentCharacter;
        if (ch == null) return;

        int bookNum = pane.CurrentBook;
        byte[] beforeBytes = ch.CloneBookTitleBytes(bookNum);
        string currentRawTitle = MacroBinaryCodec.DecodeFf11Text(beforeBytes);
        string targetLabel = $"{ch.DisplayName} / Book {bookNum:D2}";

        var dlg = new BookRenameDialog(targetLabel, currentRawTitle)
        {
            Owner = this,
            Icon = Icon
        };
        if (dlg.ShowDialog() != true) return;

        // 変更がない場合は Undo 履歴・Dirty フラグを汚さない
        if (beforeBytes.AsSpan().SequenceEqual(dlg.EncodedTitleBytes)) return;

        var action = new RenameBookAction(ch, bookNum, dlg.EncodedTitleBytes, dlg.NewTitleText);
        _undoManager.Execute(action);

        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = $"{action.Description} を反映しました（未保存状態・Ctrl+Zで取り消し可能）";
    }

    // ---------- 選択管理＆ドラッグ＆ドロップ処理 ----------

    private void SelectSlot(int paneIndex, int slotIndex)
    {
        _activePaneIndex = paneIndex;
        _panes[paneIndex].SelectedSlotIndex = slotIndex;
        RefreshPaneVisuals(_panes[0]);
        RefreshPaneVisuals(_panes[1]);
        RefreshPanePreview(0);
        RefreshPanePreview(1);
    }

    private void Slot_PreviewMouseLeftButtonDown(int paneIndex, int slotIndex, MouseButtonEventArgs e)
    {
        if (_panes[paneIndex].GetCurrentPage() == null) return;

        SelectSlot(paneIndex, slotIndex);

        if (e.ClickCount == 2)
        {
            _dragOrigin = null;
            e.Handled = true;
            OpenSlotEditor(paneIndex, slotIndex);
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragElementOffset = e.GetPosition(_panes[paneIndex].SlotBorders[slotIndex]);
        _dragOrigin = (paneIndex, slotIndex);
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

        var dragBorder = _panes[paneIndex].SlotBorders[slotIndex];
        var payload = new SlotDragPayload(paneIndex, page, slotIndex);
        var data = new DataObject(typeof(SlotDragPayload), payload);
        _dragOrigin = null;

        var adornerLayer = AdornerLayer.GetAdornerLayer(RootGrid);
        if (adornerLayer != null)
        {
            _dragAdorner = new DragAdorner(RootGrid, dragBorder, _dragElementOffset, e.GetPosition(RootGrid));
            adornerLayer.Add(_dragAdorner);
        }

        dragBorder.Opacity = 0.35;
        try
        {
            DragDrop.DoDragDrop(dragBorder, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            dragBorder.Opacity = 1.0;
            if (adornerLayer != null && _dragAdorner != null)
            {
                adornerLayer.Remove(_dragAdorner);
                _dragAdorner = null;
            }
        }
    }

    private void RootGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (_dragAdorner != null)
        {
            _dragAdorner.UpdatePosition(e.GetPosition(RootGrid));
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        // スロット上で処理されなかったドラッグ位置はドロップ不可とする
        e.Effects = DragDropEffects.None;
        e.Handled = true;
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

        var pane = _panes[targetPaneIndex];
        var border = pane.SlotBorders[targetSlotIndex];
        border.Background = isCopy ? BrushDropCopyBg : BrushDropSwapBg;
        border.BorderBrush = isCopy ? BrushDropCopyBorder : BrushDropSwapBorder;
        pane.SlotTitleTexts[targetSlotIndex].Foreground = BrushNormalTitleFg;
        pane.SlotKeyTexts[targetSlotIndex].Foreground = BrushNormalKeyFg;
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

        _activePaneIndex = targetPaneIndex;
        _panes[targetPaneIndex].SelectedSlotIndex = targetSlotIndex;
        RefreshAllUi();
        e.Handled = true;
    }

    // ---------- 右クリック / 編集ボタン操作 ----------

    private void BtnEditLeftSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_panes[0].SelectedSlotIndex is int slotIndex)
        {
            _activePaneIndex = 0;
            OpenSlotEditor(0, slotIndex);
        }
    }

    private void BtnEditRightSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_panes[1].SelectedSlotIndex is int slotIndex)
        {
            _activePaneIndex = 1;
            OpenSlotEditor(1, slotIndex);
        }
    }

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
        _activePaneIndex = paneIndex;
        _panes[paneIndex].SelectedSlotIndex = slotIndex;
        TxtStatusMessage.Text = desc;
        RefreshAllUi();
    }

    private void ClearSlot(int paneIndex, int slotIndex)
    {
        var page = _panes[paneIndex].GetCurrentPage();
        if (page == null) return;

        var empty = new byte[MacroBinaryCodec.SlotSize];
        string desc = $"クリア: {MacroSlot.GetKeyLabel(slotIndex)}";
        _undoManager.Execute(new OverwriteSlotAction(page, slotIndex, empty, desc));
        _activePaneIndex = paneIndex;
        _panes[paneIndex].SelectedSlotIndex = slotIndex;
        TxtStatusMessage.Text = $"{desc}（Ctrl+Zで元に戻せます）";
        RefreshAllUi();
    }

    private void OpenSlotEditor(int paneIndex, int slotIndex)
    {
        var pane = _panes[paneIndex];
        var page = pane.GetCurrentPage();
        if (page == null || pane.CurrentCharacter == null) return;

        var slot = page.Slots[slotIndex];
        string bookTitle = pane.CurrentCharacter.GetBookTitle(pane.CurrentBook);
        string headerLabel =
            $"[{pane.Label}] {pane.CurrentCharacter.DisplayName} / Book {pane.CurrentBook:D2} ({bookTitle}) / Page {pane.CurrentPageNumber} / {MacroSlot.GetKeyLabel(slotIndex)}";

        byte[] beforeBytes = slot.CloneBytes();
        var dlg = new MacroEditDialog(headerLabel, beforeBytes) { Owner = this, Icon = Icon };
        if (dlg.ShowDialog() != true || dlg.ResultSlotBytes == null) return;

        if (beforeBytes.AsSpan().SequenceEqual(dlg.ResultSlotBytes))
        {
            return;
        }

        string desc = $"編集: {pane.CurrentCharacter.DisplayName} B{pane.CurrentBook:D2}-P{pane.CurrentPageNumber} {MacroSlot.GetKeyLabel(slotIndex)}";
        _undoManager.Execute(new OverwriteSlotAction(page, slotIndex, dlg.ResultSlotBytes, desc));
        _activePaneIndex = paneIndex;
        pane.SelectedSlotIndex = slotIndex;
        RefreshAllUi();
        TxtStatusMessage.Text = $"{desc} を反映しました（未保存状態・Ctrl+Zで取り消し可能）";
    }

    // ---------- パレット（ページ）単位コピー ----------

    private void BtnCopyPageLeftToRight_Click(object sender, RoutedEventArgs e) => ExecutePageCopy(_panes[0], _panes[1]);
    private void BtnCopyPageRightToLeft_Click(object sender, RoutedEventArgs e) => ExecutePageCopy(_panes[1], _panes[0]);

    private void ExecutePageCopy(PaneState srcPane, PaneState dstPane)
    {
        var srcPage = srcPane.GetCurrentPage();
        var dstPage = dstPane.GetCurrentPage();
        if (srcPane.CurrentCharacter == null || dstPane.CurrentCharacter == null || srcPage == null || dstPage == null) return;
        if (ReferenceEquals(srcPage, dstPage)) return;

        string srcLabel = $"{srcPane.CurrentCharacter.DisplayName} / Book {srcPane.CurrentBook:D2} ({srcPane.CurrentCharacter.GetBookTitle(srcPane.CurrentBook)}) / Page {srcPane.CurrentPageNumber}";
        string dstLabel = $"{dstPane.CurrentCharacter.DisplayName} / Book {dstPane.CurrentBook:D2} ({dstPane.CurrentCharacter.GetBookTitle(dstPane.CurrentBook)}) / Page {dstPane.CurrentPageNumber}";

        var confirm = MessageBox.Show(
            $"コピー先パレットの全20スロットを上書きします。\nよろしいですか？\n\nコピー元: {srcLabel}\nコピー先: {dstLabel}",
            "パレット単位コピーの確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var action = new CopyPageAction(
            srcPane.CurrentCharacter, srcPane.CurrentBook, srcPane.CurrentPageNumber,
            dstPane.CurrentCharacter, dstPane.CurrentBook, dstPane.CurrentPageNumber);

        _undoManager.Execute(action);
        RefreshAllUi();
        TxtStatusMessage.Text = $"{action.Description} を実行しました（未保存状態・Ctrl+Zで取り消し可能）";
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

        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = $"{action.Description} を実行しました（未保存状態・Ctrl+Zで取り消し可能）";
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
                _slotActions.Add(new OverwriteSlotAction(dstPage, i, srcBytes, $"パレット複製 Slot {i}"));
            }

            Description =
                $"パレットコピー: {srcChar.DisplayName} B{srcBook:D2}-P{srcPageNum} → {dstChar.DisplayName} B{dstBook:D2}-P{dstPageNum}";
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
            for (int i = _slotActions.Count - 1; i >= 0; i--)
            {
                _slotActions[i].Undo();
            }
        }
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
        string backupDir = Path.Combine(AppContext.BaseDirectory, "Backups");

        try
        {
            var result = _repo.SaveAll(backupDir, ignorePolLock: false);
            if (result.Success)
            {
                RefreshAllUi();
                string serverNote = "\n\n※必要に応じて、FF11起動後にキャラクター選択画面からマクロの「サーバー保存」を行ってください。";
                string msg = result.SavedFileCount > 0
                    ? $"{result.Message}\n\nバックアップ先:\n{result.BackupPath}{serverNote}"
                    : result.Message;
                TxtStatusMessage.Text = $"{result.Message}（必要に応じてゲーム側でサーバー保存を行ってください）";
                MessageBox.Show(msg, "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(result.Message, "保存中止", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(
                $"ファイルへの書き込み権限がありません。\nアプリを「管理者として実行」してください。\n\n詳細: {ex.Message}",
                "権限エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"保存中にエラーが発生しました:\n{ex.Message}",
                "保存エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ---------- UI更新・左右独立プレビュー・pol.exe監視 ----------

    private void RefreshAllUi()
    {
        RefreshPaneVisuals(_panes[0]);
        RefreshPaneVisuals(_panes[1]);
        RefreshPanePreview(0);
        RefreshPanePreview(1);

        BtnUndo.IsEnabled = _undoManager.CanUndo;
        BtnUndo.ToolTip = _undoManager.NextUndoDescription;
        BtnRedo.IsEnabled = _undoManager.CanRedo;
        BtnRedo.ToolTip = _undoManager.NextRedoDescription;

        bool canBookCopy = _panes[0].CurrentCharacter != null &&
                           _panes[1].CurrentCharacter != null &&
                           !(ReferenceEquals(_panes[0].CurrentCharacter, _panes[1].CurrentCharacter) &&
                             _panes[0].CurrentBook == _panes[1].CurrentBook);

        var leftPage = _panes[0].GetCurrentPage();
        var rightPage = _panes[1].GetCurrentPage();
        bool canPageCopy = leftPage != null && rightPage != null && !ReferenceEquals(leftPage, rightPage);
        BtnCopyPageLeftToRight.IsEnabled = canPageCopy;
        BtnCopyPageRightToLeft.IsEnabled = canPageCopy;

        BtnCopyBookLeftToRight.IsEnabled = canBookCopy;
        BtnCopyBookRightToLeft.IsEnabled = canBookCopy;

        UpdateSaveButtonState();
    }

    private void RefreshPaneVisuals(PaneState pane)
    {
        var page = pane.GetCurrentPage();
        pane.BtnRenameBook.IsEnabled = pane.CurrentCharacter != null;

        if (page == null)
        {
            pane.TxtDirty.Text = "";
            for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
            {
                pane.SlotTitleTexts[i].Text = "";
                pane.SlotBorders[i].Background = BrushEmptyBg;
                pane.SlotBorders[i].BorderBrush = BrushNormalBorder;
                pane.SlotBorders[i].BorderThickness = new Thickness(1.5);
                pane.SlotTitleTexts[i].Foreground = BrushNormalTitleFg;
                pane.SlotKeyTexts[i].Foreground = BrushNormalKeyFg;
            }
            return;
        }

        bool isTitleDirty = pane.CurrentCharacter != null &&
            (pane.CurrentBook <= MacroBinaryCodec.BooksPerTitleFile
                ? pane.CurrentCharacter.TitleFile1.IsDirty
                : pane.CurrentCharacter.TitleFile2.IsDirty);

        pane.TxtDirty.Text = (page.IsDirty || isTitleDirty) ? "* 未保存の変更あり" : "";

        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            var slot = page.Slots[i];
            pane.SlotTitleTexts[i].Text = slot.IsEmpty ? "" : (string.IsNullOrEmpty(slot.Title) ? "(無題)" : slot.Title);
            ResetSlotVisual(pane.PaneIndex, i);
        }
    }

    private void ResetSlotVisual(int paneIndex, int slotIndex)
    {
        var pane = _panes[paneIndex];
        var page = pane.GetCurrentPage();
        var border = pane.SlotBorders[slotIndex];
        var titleText = pane.SlotTitleTexts[slotIndex];
        var keyText = pane.SlotKeyTexts[slotIndex];

        if (page == null)
        {
            border.Background = BrushEmptyBg;
            border.BorderBrush = BrushNormalBorder;
            border.BorderThickness = new Thickness(1.5);
            titleText.Foreground = BrushNormalTitleFg;
            keyText.Foreground = BrushNormalKeyFg;
            return;
        }

        bool isEmpty = page.Slots[slotIndex].IsEmpty;
        bool isSelectedInPane = pane.SelectedSlotIndex == slotIndex;
        bool isActivePane = _activePaneIndex == paneIndex;

        if (isSelectedInPane && isActivePane)
        {
            border.Background = BrushActiveSelectedBg;
            border.BorderBrush = BrushActiveSelectedBorder;
            border.BorderThickness = new Thickness(2.5);
            titleText.Foreground = BrushSelectedTitleFg;
            keyText.Foreground = BrushSelectedKeyFg;
        }
        else if (isSelectedInPane)
        {
            border.Background = BrushInactiveSelectedBg;
            border.BorderBrush = BrushInactiveSelectedBorder;
            border.BorderThickness = new Thickness(2.0);
            titleText.Foreground = BrushNormalTitleFg;
            keyText.Foreground = BrushInactiveSelectedKeyFg;
        }
        else
        {
            border.Background = isEmpty ? BrushEmptyBg : BrushFilledBg;
            border.BorderBrush = isEmpty ? BrushNormalBorder : BrushFilledBorder;
            border.BorderThickness = new Thickness(1.5);
            titleText.Foreground = BrushNormalTitleFg;
            keyText.Foreground = BrushNormalKeyFg;
        }
    }

    private void RefreshPanePreview(int paneIndex)
    {
        var pane = _panes[paneIndex];
        bool isActive = _activePaneIndex == paneIndex;

        pane.PreviewBorder.BorderBrush = isActive ? BrushActivePreviewBorder : BrushNormalBorder;
        pane.PreviewBorder.BorderThickness = new Thickness(isActive ? 2.0 : 1.0);
        pane.TxtPreviewHeader.Foreground = isActive ? BrushNormalTitleFg : BrushInactivePreviewHeaderFg;

        if (pane.SelectedSlotIndex is int slotIndex && pane.GetCurrentPage() != null)
        {
            pane.BtnEditSlot.IsEnabled = true;
            ShowPreview(paneIndex, slotIndex, isHover: false);
        }
        else
        {
            pane.BtnEditSlot.IsEnabled = false;
            pane.TxtLineNumbers.Visibility = Visibility.Hidden;
            string prefix = _isSinglePane && paneIndex == 0 ? "" : $"[{pane.Label}] ";
            pane.TxtPreviewHeader.Text = $"{prefix}スロットを選択すると内容が表示されます";
            pane.TxtPreviewBody.Text = "";
        }
    }

    private void ShowPreview(int paneIndex, int slotIndex, bool isHover)
    {
        var pane = _panes[paneIndex];
        var page = pane.GetCurrentPage();
        if (page == null || pane.CurrentCharacter == null) return;

        var slot = page.Slots[slotIndex];
        string bookTitle = pane.CurrentCharacter.GetBookTitle(pane.CurrentBook);
        string titleText = slot.IsEmpty ? "(空スロット)" : (string.IsNullOrEmpty(slot.Title) ? "(無題)" : slot.Title);
        string prefix = _isSinglePane && paneIndex == 0 ? "" : $"[{pane.Label}] ";
        string hoverMark = isHover && pane.SelectedSlotIndex != slotIndex ? " (プレビュー)" : "";

        pane.TxtPreviewHeader.Text =
            $"{prefix}{pane.CurrentCharacter.DisplayName} / B{pane.CurrentBook:D2} ({bookTitle}) / P{pane.CurrentPageNumber} / {MacroSlot.GetKeyLabel(slotIndex)} — {titleText}{hoverMark}";

        pane.TxtLineNumbers.Visibility = Visibility.Visible;
        string[] lines = slot.GetLines();
        pane.TxtPreviewBody.Text = string.Join(Environment.NewLine, lines);
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
        bool locked = _isPolRunning;

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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            var activePane = _panes[_activePaneIndex];
            if (e.Key == Key.F2 && activePane.SelectedSlotIndex is int editIndex)
            {
                OpenSlotEditor(_activePaneIndex, editIndex);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && activePane.SelectedSlotIndex is int delIndex)
            {
                // 編集可能なテキスト入力中のみ除外（読み取り専用の黒板フォーカス中はDeleteを効かせる）
                if (Keyboard.FocusedElement is TextBoxBase { IsReadOnly: false })
                    return;

                var page = activePane.GetCurrentPage();
                if (page != null && !page.Slots[delIndex].IsEmpty)
                {
                    ClearSlot(_activePaneIndex, delIndex);
                    e.Handled = true;
                }
                return;
            }
            return;
        }

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

    private void SlotGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_panes[0] == null || _panes[1] == null) return;
        if (GridLeftSlots.ActualWidth <= 0) return;

        double slotWidth = GridLeftSlots.ActualWidth / 10.0;

        double targetSlotHeight = Math.Clamp(slotWidth * 0.90, 70.0, 115.0);
        double targetGridHeight = targetSlotHeight * 2.0;

        if (Math.Abs(GridLeftSlots.Height - targetGridHeight) > 1.0)
        {
            GridLeftSlots.Height = targetGridHeight;
            GridRightSlots.Height = targetGridHeight;
        }

        double titleFontSize = Math.Clamp(slotWidth * 0.24, 13.0, 26.0);
        double keyFontSize = Math.Clamp(titleFontSize * 0.60, 10.0, 15.0);

        foreach (var pane in _panes)
        {
            for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
            {
                pane.SlotTitleTexts[i].FontSize = titleFontSize;
                pane.SlotKeyTexts[i].FontSize = keyFontSize;
            }
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ActualHeight <= 0 || TxtLeftPreviewBody == null || TxtRightPreviewBody == null ||
            TxtLeftLineNumbers == null || TxtRightLineNumbers == null) return;

        double bodyFontSize = Math.Clamp(ActualHeight / 48.0, 14.0, 24.0);
        double headerFontSize = Math.Clamp(ActualHeight / 58.0, 13.0, 20.0);
        double lineHeight = Math.Round(bodyFontSize * 1.45);

        TxtLeftPreviewBody.FontSize = bodyFontSize;
        TxtRightPreviewBody.FontSize = bodyFontSize;
        TxtLeftLineNumbers.FontSize = bodyFontSize;
        TxtRightLineNumbers.FontSize = bodyFontSize;

        TextBlock.SetLineHeight(TxtLeftPreviewBody, lineHeight);
        TextBlock.SetLineHeight(TxtRightPreviewBody, lineHeight);
        TextBlock.SetLineHeight(TxtLeftLineNumbers, lineHeight);
        TextBlock.SetLineHeight(TxtRightLineNumbers, lineHeight);

        TxtLeftPreviewHeader.FontSize = headerFontSize;
        TxtRightPreviewHeader.FontSize = headerFontSize;
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
        SavePaneMode(_isSinglePane);
        _polTimer.Stop();
    }
}