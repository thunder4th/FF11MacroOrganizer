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

namespace FF11MacroOrganizer;

public partial class MainWindow : Window
{
    private sealed class PaneState
    {
        public int PaneIndex { get; init; }
        public string Label => PaneIndex == 0 ? Loc.Instance["PaneLabelLeft"] : Loc.Instance["PaneLabelRight"];
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
        public required Button BtnRenameChar { get; init; }
        public required Button BtnRenameBook { get; init; }
        public required Button BtnClear { get; init; }
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
    private readonly CharacterNameStore _charNameStore = new();
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
    private static readonly string LanguageSettingPath = Path.Combine(AppContext.BaseDirectory, "language.txt");

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
        // XAML バインディング初回評価の前に初期言語を確定させる
        Loc.Instance.SetLanguage(Loc.ResolveInitialLanguage(LoadSavedLanguage()));

        InitializeComponent();

        // スロット以外の領域ではドロップ不可カーソルにする
        RootGrid.DragOver += RootGrid_DragOver;

        _panes[0] = InitializePane(
            0, CmbLeftChar, CmbLeftBook, CmbLeftPage, TxtLeftDirty, GridLeftSlots,
            LeftPreviewBorder, TxtLeftPreviewHeader, TxtLeftLineNumbers, TxtLeftPreviewBody,
            BtnEditLeftSlot, BtnRenameLeftChar, BtnRenameLeftBook, BtnClearLeft);

        _panes[1] = InitializePane(
            1, CmbRightChar, CmbRightBook, CmbRightPage, TxtRightDirty, GridRightSlots,
            RightPreviewBorder, TxtRightPreviewHeader, TxtRightLineNumbers, TxtRightPreviewBody,
            BtnEditRightSlot, BtnRenameRightChar, BtnRenameRightBook, BtnClearRight);

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
        MacroBinaryCodec.Dictionary.TryLoadFromHintPath(null, GetActiveDictionaryLanguage());
        ApplyPaneMode(LoadPaneMode(), isStartup: true);
        RefreshPanePreview(0);
        RefreshPanePreview(1);
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
        Button btnRenameChar,
        Button btnRenameBook,
        Button btnClear)
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

        btnClear.ContextMenu = CreateBulkClearContextMenu(paneIndex);

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
            BtnRenameChar = btnRenameChar,
            BtnRenameBook = btnRenameBook,
            BtnClear = btnClear,
        };
    }

    private ContextMenu CreateSlotContextMenu(int paneIndex, int slotIndex)
    {
        var menu = new ContextMenu();

        var miEdit = new MenuItem { FontWeight = FontWeights.Bold };
        miEdit.Click += (_, _) => OpenSlotEditor(paneIndex, slotIndex);

        var miCopy = new MenuItem();
        miCopy.Click += (_, _) => CopySlotToClipboard(paneIndex, slotIndex);

        var miPaste = new MenuItem();
        miPaste.Click += (_, _) => PasteSlotFromClipboard(paneIndex, slotIndex);

        var miClear = new MenuItem();
        miClear.Click += (_, _) => ClearSlot(paneIndex, slotIndex);

        menu.Opened += (_, _) =>
        {
            var loc = Loc.Instance;
            miEdit.Header = loc["MenuEditSlot"];
            miCopy.Header = loc["MenuCopySlot"];
            miPaste.Header = loc["MenuPasteSlot"];
            miClear.Header = loc["MenuClearSlot"];

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

    // ---------- 言語切替（UI + 定型文辞書連動） ----------

    private static DictionaryLanguage GetActiveDictionaryLanguage()
        => Loc.Instance.IsJapanese ? DictionaryLanguage.Japanese : DictionaryLanguage.English;

    private void BtnLangSwitch_Click(object sender, RoutedEventArgs e)
    {
        string nextLang = Loc.Instance.IsJapanese ? "en" : "ja";
        Loc.Instance.SetLanguage(nextLang);
        SaveLanguage(nextLang);

        var dictLang = GetActiveDictionaryLanguage();
        if (!string.IsNullOrEmpty(MacroBinaryCodec.Dictionary.LoadedFf11Root))
        {
            MacroBinaryCodec.Dictionary.SwitchLanguage(dictLang);
        }
        else
        {
            MacroBinaryCodec.Dictionary.TryLoadFromHintPath(_repo.RootPath, dictLang);
        }

        UpdateLeftPaneTitle();
        UpdatePolStatus();
        RefreshAllUi();

        string dictNote = MacroBinaryCodec.Dictionary.Count > 0
            ? Loc.Instance.Format("StatusDictLoaded", MacroBinaryCodec.Dictionary.Count)
            : "";
        TxtStatusMessage.Text = Loc.Instance.Format("StatusLanguageChanged", Loc.Instance["LangName"], dictNote);
    }

    private static string? LoadSavedLanguage()
    {
        try
        {
            if (!File.Exists(LanguageSettingPath)) return null;
            string code = File.ReadAllText(LanguageSettingPath).Trim();
            return string.IsNullOrEmpty(code) ? null : code;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveLanguage(string code)
    {
        try
        {
            File.WriteAllText(LanguageSettingPath, code);
        }
        catch
        {
            // 設定ファイルの保存失敗は無視して動作を継続
        }
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
                Loc.Instance["ConfirmDiscardAndOpen"],
                Loc.Instance["ConfirmTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = Loc.Instance["OpenFolderDialogTitle"]
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
            _charNameStore.ApplyTo(_repo.Characters);
            MacroBinaryCodec.Dictionary.TryLoadFromHintPath(folderPath, GetActiveDictionaryLanguage());
            _undoManager.Clear();
            _panes[0].SelectedSlotIndex = null;
            _panes[1].SelectedSlotIndex = null;
            _activePaneIndex = 0;

            if (_repo.Characters.Count == 0)
            {
                if (!isAutoLoad)
                {
                    MessageBox.Show(
                        Loc.Instance["MsgNoMacroFilesFound"],
                        Loc.Instance["LoadFailedTitle"],
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
                ? Loc.Instance.Format("StatusDictLoaded", MacroBinaryCodec.Dictionary.Count)
                : "";
            TxtStatusMessage.Text = Loc.Instance.Format("StatusLoadComplete", _repo.Characters.Count, _repo.RootPath ?? "", dictNote);
        }
        catch (Exception ex)
        {
            if (!isAutoLoad)
            {
                MessageBox.Show(
                    Loc.Instance.Format("MsgLoadError", ex.Message),
                    Loc.Instance["ErrorTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                TxtStatusMessage.Text = Loc.Instance.Format("StatusAutoLoadSkipped", ex.Message);
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

    private void RefreshCharComboBox(PaneState pane)
    {
        bool prevUpdating = pane.IsUpdating;
        pane.IsUpdating = true;
        try
        {
            int selectedIdx = pane.CurrentCharacter != null
                ? _repo.Characters.IndexOf(pane.CurrentCharacter)
                : -1;

            pane.CmbChar.Items.Clear();
            foreach (var ch in _repo.Characters)
            {
                pane.CmbChar.Items.Add(ch.DisplayName);
            }

            if (selectedIdx >= 0 && selectedIdx < _repo.Characters.Count)
            {
                pane.CmbChar.SelectedIndex = selectedIdx;
            }
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

    private void UpdateLeftPaneTitle()
    {
        TxtLeftPaneTitle.Text = Loc.Instance[_isSinglePane ? "PaneTitleSingle" : "PaneTitleLeft"];
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

            UpdateLeftPaneTitle();
            _activePaneIndex = 0;

            MinWidth = 840;
            if (WindowState == WindowState.Normal)
            {
                Width = Math.Max(860, Math.Round(_twoPaneWindowWidth * 0.65));
            }
        }
        else
        {
            CenterCopyPanel.Visibility = Visibility.Visible;
            RightPaneBorder.Visibility = Visibility.Visible;
            RightPreviewBorder.Visibility = Visibility.Visible;

            ColCenter.Width = new GridLength(98);
            ColRight.Width = new GridLength(1, GridUnitType.Star);
            ColPreviewGap.Width = new GridLength(10);
            ColPreviewRight.Width = new GridLength(1, GridUnitType.Star);

            UpdateLeftPaneTitle();

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
        TxtStatusMessage.Text = Loc.Instance.Format("StatusAppliedUndoable", action.Description);
    }

    private void BtnRenameLeftChar_Click(object sender, RoutedEventArgs e) => OpenCharacterNameDialog(_panes[0]);
    private void BtnRenameRightChar_Click(object sender, RoutedEventArgs e) => OpenCharacterNameDialog(_panes[1]);

    private void OpenCharacterNameDialog(PaneState pane)
    {
        var ch = pane.CurrentCharacter;
        if (ch == null) return;

        var dlg = new CharacterNameDialog(ch.CharacterId, ch.CustomCharacterName, ch.DetectedCharacterName)
        {
            Owner = this,
            Icon = Icon
        };
        if (dlg.ShowDialog() != true) return;

        // 自動検出名と全く同じ文字列が入力された場合、元々カスタム未設定なら不要な保存をしない
        string? newCustomName = dlg.CustomName;
        if (newCustomName != null &&
            string.IsNullOrEmpty(ch.CustomCharacterName) &&
            string.Equals(newCustomName, ch.DetectedCharacterName, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(ch.CustomCharacterName, newCustomName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _charNameStore.SetName(ch.CharacterId, newCustomName);
            ch.SetCustomCharacterName(newCustomName);

            RefreshCharComboBox(_panes[0]);
            RefreshCharComboBox(_panes[1]);
            RefreshAllUi();

            TxtStatusMessage.Text = newCustomName != null
                ? Loc.Instance.Format("StatusCharNameSet", ch.DisplayName)
                : Loc.Instance.Format("StatusCharNameReset", ch.DisplayName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.Instance.Format("MsgCharNameSaveError", ex.Message),
                Loc.Instance["SaveErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
            string desc = Loc.Instance.Format(
                "UndoOverwriteCopy",
                MacroSlot.GetKeyLabel(payload.SlotIndex),
                MacroSlot.GetKeyLabel(targetSlotIndex));
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
        var slot = page.Slots[slotIndex];
        _slotClipboard = slot.CloneBytes();
        string titleLabel = slot.IsEmpty
            ? Loc.Instance["SlotEmpty"]
            : (string.IsNullOrEmpty(slot.Title) ? Loc.Instance["SlotUntitled"] : slot.Title);
        TxtStatusMessage.Text = Loc.Instance.Format("StatusCopiedSlot", MacroSlot.GetKeyLabel(slotIndex), titleLabel);
    }

    private void PasteSlotFromClipboard(int paneIndex, int slotIndex)
    {
        var page = _panes[paneIndex].GetCurrentPage();
        if (page == null || _slotClipboard == null) return;

        string desc = Loc.Instance.Format("UndoPaste", MacroSlot.GetKeyLabel(slotIndex));
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
        string desc = Loc.Instance.Format("UndoClearSlot", MacroSlot.GetKeyLabel(slotIndex));
        _undoManager.Execute(new OverwriteSlotAction(page, slotIndex, empty, desc));
        _activePaneIndex = paneIndex;
        _panes[paneIndex].SelectedSlotIndex = slotIndex;
        TxtStatusMessage.Text = Loc.Instance.Format("StatusClearedSlot", desc);
        RefreshAllUi();
    }

    private void OpenSlotEditor(int paneIndex, int slotIndex)
    {
        var pane = _panes[paneIndex];
        var page = pane.GetCurrentPage();
        if (page == null || pane.CurrentCharacter == null) return;

        var slot = page.Slots[slotIndex];
        string bookTitle = pane.CurrentCharacter.GetBookTitle(pane.CurrentBook);
        string prefix = _isSinglePane && paneIndex == 0 ? "" : $"[{pane.Label}] ";
        string headerLabel =
            $"{prefix}{pane.CurrentCharacter.DisplayName} / Book {pane.CurrentBook:D2} ({bookTitle}) / Page {pane.CurrentPageNumber} / {MacroSlot.GetKeyLabel(slotIndex)}";

        byte[] beforeBytes = slot.CloneBytes();
        var dlg = new MacroEditDialog(headerLabel, beforeBytes) { Owner = this, Icon = Icon };
        if (dlg.ShowDialog() != true || dlg.ResultSlotBytes == null) return;

        if (beforeBytes.AsSpan().SequenceEqual(dlg.ResultSlotBytes))
        {
            return;
        }

        string desc = Loc.Instance.Format(
            "UndoEditSlot",
            pane.CurrentCharacter.DisplayName,
            pane.CurrentBook,
            pane.CurrentPageNumber,
            MacroSlot.GetKeyLabel(slotIndex));
        _undoManager.Execute(new OverwriteSlotAction(page, slotIndex, dlg.ResultSlotBytes, desc));
        _activePaneIndex = paneIndex;
        pane.SelectedSlotIndex = slotIndex;
        RefreshAllUi();
        TxtStatusMessage.Text = Loc.Instance.Format("StatusAppliedUndoable", desc);
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

        var loc = Loc.Instance;
        string srcLabel = $"{srcPane.CurrentCharacter.DisplayName} / Book {srcPane.CurrentBook:D2} ({srcPane.CurrentCharacter.GetBookTitle(srcPane.CurrentBook)}) / Page {srcPane.CurrentPageNumber}";
        string dstLabel = $"{dstPane.CurrentCharacter.DisplayName} / Book {dstPane.CurrentBook:D2} ({dstPane.CurrentCharacter.GetBookTitle(dstPane.CurrentBook)}) / Page {dstPane.CurrentPageNumber}";

        var (confirmed, _) = ShowActionConfirmDialog(
            windowTitle: loc["DlgCopyPageTitle"],
            headline: loc["DlgCopyPageHeadline"],
            sourceLine: loc.Format("DlgCopySource", srcLabel),
            targetLine: loc.Format("DlgCopyDest", dstLabel),
            noteLine: loc["DlgCopyNoteUndo"],
            confirmButtonText: loc["DlgBtnCopyExec"],
            isDestructive: false);

        if (!confirmed) return;

        var action = new CopyPageAction(
            srcPane.CurrentCharacter, srcPane.CurrentBook, srcPane.CurrentPageNumber,
            dstPane.CurrentCharacter, dstPane.CurrentBook, dstPane.CurrentPageNumber);

        _undoManager.Execute(action);
        RefreshAllUi();
        TxtStatusMessage.Text = loc.Format("StatusExecutedUndoable", action.Description);
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

        var loc = Loc.Instance;
        string srcLabel = $"{srcPane.CurrentCharacter.DisplayName} / Book {srcPane.CurrentBook:D2} ({srcPane.CurrentCharacter.GetBookTitle(srcPane.CurrentBook)})";
        string dstLabel = $"{dstPane.CurrentCharacter.DisplayName} / Book {dstPane.CurrentBook:D2} ({dstPane.CurrentCharacter.GetBookTitle(dstPane.CurrentBook)})";

        var (confirmed, copyTitle) = ShowActionConfirmDialog(
            windowTitle: loc["DlgCopyBookTitle"],
            headline: loc["DlgCopyBookHeadline"],
            sourceLine: loc.Format("DlgCopySource", srcLabel),
            targetLine: loc.Format("DlgCopyDest", dstLabel),
            noteLine: loc["DlgCopyNoteUndo"],
            confirmButtonText: loc["DlgBtnCopyExec"],
            isDestructive: false,
            checkBoxText: loc["DlgCopyBookIncludeTitle"],
            checkBoxDefaultChecked: true);

        if (!confirmed) return;

        var action = new CopyBookAction(
            srcPane.CurrentCharacter, srcPane.CurrentBook,
            dstPane.CurrentCharacter, dstPane.CurrentBook,
            copyTitle);

        _undoManager.Execute(action);

        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = loc.Format("StatusExecutedUndoable", action.Description);
    }

    // ---------- パレット / ブック 一括クリア ----------

    private ContextMenu CreateBulkClearContextMenu(int paneIndex)
    {
        var menu = new ContextMenu();

        var miClearPage = new MenuItem();
        miClearPage.Click += (_, _) => ExecuteClearPage(_panes[paneIndex]);

        var miClearBook = new MenuItem();
        miClearBook.Click += (_, _) => ExecuteClearBook(_panes[paneIndex]);

        menu.Opened += (_, _) =>
        {
            var loc = Loc.Instance;
            miClearPage.Header = loc["MenuClearPage"];
            miClearBook.Header = loc["MenuClearBook"];

            bool hasChar = _panes[paneIndex].CurrentCharacter != null;
            miClearPage.IsEnabled = hasChar;
            miClearBook.IsEnabled = hasChar;
        };

        menu.Items.Add(miClearPage);
        menu.Items.Add(new Separator());
        menu.Items.Add(miClearBook);
        return menu;
    }

    private void BtnClearLeft_Click(object sender, RoutedEventArgs e) => OpenBulkClearMenu(BtnClearLeft);
    private void BtnClearRight_Click(object sender, RoutedEventArgs e) => OpenBulkClearMenu(BtnClearRight);

    private static void OpenBulkClearMenu(Button button)
    {
        if (button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void ExecuteClearPage(PaneState pane)
    {
        var ch = pane.CurrentCharacter;
        var page = pane.GetCurrentPage();
        if (ch == null || page == null) return;

        var loc = Loc.Instance;
        int filledCount = page.Slots.Count(s => !s.IsEmpty);
        if (filledCount == 0)
        {
            TxtStatusMessage.Text = loc.Format("StatusPageAlreadyEmpty", pane.CurrentBook, pane.CurrentPageNumber);
            return;
        }

        string targetLabel =
            $"{ch.DisplayName} / Book {pane.CurrentBook:D2} ({ch.GetBookTitle(pane.CurrentBook)}) / Page {pane.CurrentPageNumber}";

        var (confirmed, _) = ShowActionConfirmDialog(
            windowTitle: loc["DlgClearPageTitle"],
            headline: loc["DlgClearPageHeadline"],
            sourceLine: null,
            targetLine: loc.Format("DlgTarget", targetLabel),
            noteLine: loc.Format("DlgClearPageNote", filledCount),
            confirmButtonText: loc["DlgBtnClearExec"],
            isDestructive: true);

        if (!confirmed) return;

        var action = new ClearPageAction(ch, pane.CurrentBook, pane.CurrentPageNumber);
        _undoManager.Execute(action);
        RefreshAllUi();
        TxtStatusMessage.Text = loc.Format("StatusExecutedUndoable", action.Description);
    }

    private void ExecuteClearBook(PaneState pane)
    {
        var ch = pane.CurrentCharacter;
        if (ch == null) return;

        var loc = Loc.Instance;
        int bookNum = pane.CurrentBook;
        int filledSlots = 0;
        int filledPages = 0;
        for (int p = 1; p <= 10; p++)
        {
            int count = ch.GetPage(bookNum, p).Slots.Count(s => !s.IsEmpty);
            if (count > 0)
            {
                filledSlots += count;
                filledPages++;
            }
        }

        byte[] currentTitleBytes = ch.CloneBookTitleBytes(bookNum);
        bool hasCustomTitle = !string.IsNullOrEmpty(MacroBinaryCodec.DecodeFf11Text(currentTitleBytes));

        if (filledSlots == 0 && !hasCustomTitle)
        {
            TxtStatusMessage.Text = loc.Format("StatusBookAlreadyEmpty", bookNum);
            return;
        }

        string targetLabel = $"{ch.DisplayName} / Book {bookNum:D2} ({ch.GetBookTitle(bookNum)})";
        var (confirmed, clearTitle) = ShowActionConfirmDialog(
            windowTitle: loc["DlgClearBookTitle"],
            headline: loc["DlgClearBookHeadline"],
            sourceLine: null,
            targetLine: loc.Format("DlgTarget", targetLabel),
            noteLine: loc.Format("DlgClearBookNote", filledPages, filledSlots),
            confirmButtonText: loc["DlgBtnClearExec"],
            isDestructive: true,
            checkBoxText: loc["DlgClearBookIncludeTitle"],
            checkBoxDefaultChecked: false,
            checkBoxEnabled: hasCustomTitle);

        if (!confirmed) return;

        if (filledSlots == 0 && !clearTitle)
        {
            TxtStatusMessage.Text = loc.Format("StatusBookSlotsAlreadyEmpty", bookNum);
            return;
        }

        var action = new ClearBookAction(ch, bookNum, clearTitle);
        _undoManager.Execute(action);

        if (clearTitle)
        {
            RefreshBookComboBox(_panes[0]);
            RefreshBookComboBox(_panes[1]);
        }
        RefreshAllUi();
        TxtStatusMessage.Text = loc.Format("StatusExecutedUndoable", action.Description);
    }

    /// <summary>
    /// パレット／ブックのコピー・一括クリアで共通使用する確認ダイアログ
    /// </summary>
    private (bool Confirmed, bool OptionChecked) ShowActionConfirmDialog(
        string windowTitle,
        string headline,
        string? sourceLine,
        string targetLine,
        string? noteLine,
        string confirmButtonText,
        bool isDestructive,
        string? checkBoxText = null,
        bool checkBoxDefaultChecked = false,
        bool checkBoxEnabled = true)
    {
        var dlg = new Window
        {
            Title = windowTitle,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = this,
            Icon = Icon,
            Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB))
        };

        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = headline,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (!string.IsNullOrEmpty(sourceLine))
        {
            root.Children.Add(new TextBlock
            {
                Text = sourceLine,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        root.Children.Add(new TextBlock
        {
            Text = targetLine,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(noteLine) ? 14 : 6)
        });

        if (!string.IsNullOrEmpty(noteLine))
        {
            root.Children.Add(new TextBlock
            {
                Text = noteLine,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        CheckBox? chkOption = null;
        if (!string.IsNullOrEmpty(checkBoxText))
        {
            chkOption = new CheckBox
            {
                Content = checkBoxText,
                IsChecked = checkBoxDefaultChecked,
                IsEnabled = checkBoxEnabled,
                Margin = new Thickness(0, 0, 0, 16)
            };
            root.Children.Add(chkOption);
        }

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var primaryBg = isDestructive
            ? Color.FromRgb(0xDC, 0x26, 0x26)
            : Color.FromRgb(0x25, 0x63, 0xEB);

        var btnOk = new Button
        {
            Content = confirmButtonText,
            Width = 100,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(primaryBg),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            IsDefault = true
        };
        btnOk.Click += (_, _) => dlg.DialogResult = true;

        var btnCancel = new Button
        {
            Content = Loc.Instance["DlgCancel"],
            Width = 90,
            Height = 28,
            Cursor = Cursors.Hand,
            IsCancel = true
        };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        root.Children.Add(btnPanel);

        dlg.Content = root;

        bool result = dlg.ShowDialog() == true;
        return (result, chkOption?.IsChecked == true);
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
        TxtStatusMessage.Text = Loc.Instance.Format("StatusUndone", desc ?? "");
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undoManager.CanRedo) return;
        string? desc = _undoManager.NextRedoDescription;
        _undoManager.Redo();
        RefreshBookComboBox(_panes[0]);
        RefreshBookComboBox(_panes[1]);
        RefreshAllUi();
        TxtStatusMessage.Text = Loc.Instance.Format("StatusRedone", desc ?? "");
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        UpdatePolStatus();
        string backupDir = Path.Combine(AppContext.BaseDirectory, "Backups");
        var loc = Loc.Instance;

        try
        {
            var result = _repo.SaveAll(backupDir, ignorePolLock: false);
            if (result.Success)
            {
                RefreshAllUi();
                string serverNote = loc["MsgSaveServerNote"];
                string msg = result.SavedFileCount > 0
                    ? loc.Format("MsgSaveBackupInfo", result.Message, result.BackupPath ?? "", serverNote)
                    : result.Message;
                TxtStatusMessage.Text = loc.Format("StatusSaved", result.Message);
                MessageBox.Show(msg, loc["SaveDoneTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(result.Message, loc["SaveAbortedTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(
                loc.Format("MsgSavePermissionError", ex.Message),
                loc["PermissionErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                loc.Format("MsgSaveError", ex.Message),
                loc["SaveErrorTitle"],
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
        bool hasChar = pane.CurrentCharacter != null;
        pane.BtnRenameChar.IsEnabled = hasChar;
        pane.BtnRenameBook.IsEnabled = hasChar;
        pane.BtnClear.IsEnabled = hasChar;

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

        pane.TxtDirty.Text = (page.IsDirty || isTitleDirty) ? Loc.Instance["DirtyMarker"] : "";

        string untitled = Loc.Instance["SlotUntitled"];
        for (int i = 0; i < MacroBinaryCodec.SlotsPerPage; i++)
        {
            var slot = page.Slots[i];
            pane.SlotTitleTexts[i].Text = slot.IsEmpty ? "" : (string.IsNullOrEmpty(slot.Title) ? untitled : slot.Title);
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
            pane.TxtPreviewHeader.Text = Loc.Instance.Format("PreviewSelectPrompt", prefix);
            pane.TxtPreviewBody.Text = "";
        }
    }

    private void ShowPreview(int paneIndex, int slotIndex, bool isHover)
    {
        var pane = _panes[paneIndex];
        var page = pane.GetCurrentPage();
        if (page == null || pane.CurrentCharacter == null) return;

        var loc = Loc.Instance;
        var slot = page.Slots[slotIndex];
        string bookTitle = pane.CurrentCharacter.GetBookTitle(pane.CurrentBook);
        string titleText = slot.IsEmpty
            ? loc["SlotEmpty"]
            : (string.IsNullOrEmpty(slot.Title) ? loc["SlotUntitled"] : slot.Title);
        string prefix = _isSinglePane && paneIndex == 0 ? "" : $"[{pane.Label}] ";
        string hoverMark = isHover && pane.SelectedSlotIndex != slotIndex ? loc["PreviewHoverSuffix"] : "";

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
            TxtPolStatus.Text = Loc.Instance["PolRunning"];
        }
        else
        {
            PolStatusBadge.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            PolStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
            TxtPolStatus.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
            TxtPolStatus.Text = Loc.Instance["PolNotRunning"];
        }

        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        int dirtyCount = _repo.CountDirtyFiles();
        bool locked = _isPolRunning;

        BtnSave.Content = dirtyCount > 0
            ? Loc.Instance.Format("BtnSaveCount", dirtyCount)
            : Loc.Instance["BtnSave"];
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
                Loc.Instance["ConfirmDiscardAndExit"],
                Loc.Instance["ExitConfirmTitle"],
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