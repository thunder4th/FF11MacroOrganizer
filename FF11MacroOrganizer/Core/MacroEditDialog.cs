using FF11MacroOrganizer.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace FF11MacroOrganizer;

public sealed class MacroEditDialog : Window
{
    private const int MaxTitleBytes = 8;
    private const int MaxLineBytes = 60;
    private const double BaseContentWidth = 984.0;
    private const double BaseContentHeight = 521.0;

    // セッション中は前回閉じたサイズ・最大化状態を記憶して再利用する
    private static double _lastWidth = 1000;
    private static double _lastHeight = 560;
    private static WindowState _lastWindowState = WindowState.Normal;

    private static readonly SolidColorBrush BrushCountOk = new(Color.FromRgb(75, 85, 99));
    private static readonly SolidColorBrush BrushCountError = new(Color.FromRgb(185, 28, 28));
    private static readonly SolidColorBrush BrushInputErrorBg = new(Color.FromRgb(254, 242, 242));
    private static readonly SolidColorBrush BrushCategoryFg = new(Color.FromRgb(107, 114, 128));
    private static readonly SolidColorBrush BrushAmbiguousFg = new(Color.FromRgb(180, 83, 9));

    private readonly byte[] _originalBytes;
    private readonly ScaleTransform _rootScale = new(1.0, 1.0);
    private readonly TextBox _txtTitle;
    private readonly TextBlock _lblTitleCount;
    private readonly TextBox[] _txtLines = new TextBox[6];
    private readonly TextBlock[] _lblLineCounts = new TextBlock[6];
    private readonly TextBlock _txtValidationMessage;
    private readonly Button _btnApply;

    private readonly ComboBox _cmbCategory;
    private readonly TextBox _txtSearch;
    private readonly TextBlock _lblSearchCount;
    private readonly ListBox _lstResults;
    private readonly Button _btnInsert;

    private TextBox _activeInput;
    private bool _isInitializing = true;
    private bool _isBatchUpdating;

    public byte[]? ResultSlotBytes { get; private set; }

    public MacroEditDialog(string slotHeaderLabel, byte[] originalSlotBytes)
    {
        var loc = Loc.Instance;
        _originalBytes = (byte[])originalSlotBytes.Clone();

        Title = loc["DlgEditTitle"];
        Width = _lastWidth;
        Height = _lastHeight;
        MinWidth = 840;
        MinHeight = 480;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(249, 250, 251));

        var outerGrid = new Grid();
        var rootGrid = new Grid
        {
            Margin = new Thickness(14),
            LayoutTransform = _rootScale
        };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

        // ============================================================
        // 左ペイン：マクロ編集（タイトル＋6行＋バイト数カウンタ）
        // ============================================================
        var leftCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14)
        };

        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // ヘッダ
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // タイトル
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 本文見出し
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6行入力
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 検証・ヒント
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 下部ボタン

        var lblHeader = new TextBlock
        {
            Text = slotHeaderLabel,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(lblHeader, 0);
        leftGrid.Children.Add(lblHeader);

        // タイトル行
        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        titleRow.Children.Add(new TextBlock
        {
            Text = loc["DlgEditLblTitle"],
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        });

        _txtTitle = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Meiryo UI"),
            FontSize = 14,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _txtTitle.GotFocus += (_, _) => _activeInput = _txtTitle;
        _txtTitle.TextChanged += (_, _) => ValidateAllInputs();
        _txtTitle.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _txtLines[0].Focus();
                e.Handled = true;
            }
        };
        Grid.SetColumn(_txtTitle, 1);
        titleRow.Children.Add(_txtTitle);

        _lblTitleCount = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = BrushCountOk
        };
        Grid.SetColumn(_lblTitleCount, 2);
        titleRow.Children.Add(_lblTitleCount);

        var lblTitleHint = new TextBlock
        {
            Text = loc["DlgEditTitleHint"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            Foreground = BrushCategoryFg
        };
        Grid.SetColumn(lblTitleHint, 3);
        titleRow.Children.Add(lblTitleHint);

        Grid.SetRow(titleRow, 1);
        leftGrid.Children.Add(titleRow);

        // 本文見出し
        var lblBodyHeader = new TextBlock
        {
            Text = loc["DlgEditBodyHeader"],
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(lblBodyHeader, 2);
        leftGrid.Children.Add(lblBodyHeader);

        // 本文6行
        var linesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        for (int i = 0; i < 6; i++)
        {
            int lineIndex = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            var lblNo = new TextBlock
            {
                Text = $"{i + 1}:",
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = BrushCategoryFg
            };
            row.Children.Add(lblNo);

            var tb = new TextBox
            {
                FontFamily = new FontFamily("Consolas, Meiryo UI"),
                FontSize = 14,
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            tb.GotFocus += (_, _) => _activeInput = tb;
            tb.TextChanged += (_, _) => ValidateAllInputs();
            tb.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && lineIndex < 5)
                {
                    _txtLines[lineIndex + 1].Focus();
                    e.Handled = true;
                }
            };
            DataObject.AddPastingHandler(tb, (_, e) => OnLinePasting(lineIndex, e));
            Grid.SetColumn(tb, 1);
            row.Children.Add(tb);
            _txtLines[i] = tb;

            var lblCount = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = BrushCountOk
            };
            Grid.SetColumn(lblCount, 2);
            row.Children.Add(lblCount);
            _lblLineCounts[i] = lblCount;

            linesPanel.Children.Add(row);
        }
        Grid.SetRow(linesPanel, 3);
        leftGrid.Children.Add(linesPanel);

        // 検証メッセージ／操作ヒント
        _txtValidationMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(2, 4, 2, 10)
        };
        Grid.SetRow(_txtValidationMessage, 4);
        leftGrid.Children.Add(_txtValidationMessage);

        // 左ペイン下部ボタン
        var bottomDock = new DockPanel { LastChildFill = false };

        var btnClearAll = new Button
        {
            Content = loc["DlgEditBtnClearAll"],
            Padding = new Thickness(10, 6, 10, 6)
        };
        btnClearAll.Click += (_, _) =>
        {
            bool hasAnyText = !string.IsNullOrEmpty(_txtTitle.Text) ||
                              _txtLines.Any(tb => !string.IsNullOrEmpty(tb.Text));
            if (!hasAnyText)
            {
                _txtLines[0].Focus();
                return;
            }

            var confirm = MessageBox.Show(
                Loc.Instance["DlgEditClearConfirmMsg"],
                Loc.Instance["DlgEditClearConfirmTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _isBatchUpdating = true;
            try
            {
                _txtTitle.Text = string.Empty;
                foreach (var tb in _txtLines) tb.Text = string.Empty;
            }
            finally
            {
                _isBatchUpdating = false;
            }

            ValidateAllInputs();
            _txtLines[0].Focus();
        };
        DockPanel.SetDock(btnClearAll, Dock.Left);
        bottomDock.Children.Add(btnClearAll);

        _btnApply = new Button
        {
            Content = loc["DlgEditBtnApply"],
            Width = 110,
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        _btnApply.Click += BtnApply_Click;
        DockPanel.SetDock(_btnApply, Dock.Right);
        bottomDock.Children.Add(_btnApply);

        var btnCancel = new Button
        {
            Content = loc["DlgCancel"],
            Width = 96,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        DockPanel.SetDock(btnCancel, Dock.Right);
        bottomDock.Children.Add(btnCancel);

        Grid.SetRow(bottomDock, 5);
        leftGrid.Children.Add(bottomDock);

        leftCard.Child = leftGrid;
        Grid.SetColumn(leftCard, 0);
        rootGrid.Children.Add(leftCard);

        // ============================================================
        // 右ペイン：定型文・アイテム検索＆挿入ピッカー
        // ============================================================
        var rightCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14)
        };

        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 見出し
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // カテゴリ選択
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 検索ボックス
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // ヒット件数
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 結果リスト
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 挿入ボタン

        var dict = MacroBinaryCodec.Dictionary;
        var lblPickerTitle = new TextBlock
        {
            Text = loc.Format("DlgEditPickerTitle", dict.Count),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(lblPickerTitle, 0);
        rightGrid.Children.Add(lblPickerTitle);

        // カテゴリ絞り込み
        var catRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var lblCat = new TextBlock
        {
            Text = loc["DlgEditLblCategory"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        DockPanel.SetDock(lblCat, Dock.Left);
        catRow.Children.Add(lblCat);

        _cmbCategory = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        _cmbCategory.Items.Add(loc["DlgEditCategoryAll"]);
        foreach (string cat in dict.Categories)
        {
            _cmbCategory.Items.Add(cat);
        }
        _cmbCategory.SelectedIndex = 0;
        _cmbCategory.SelectionChanged += (_, _) => RefreshSearchResults();
        catRow.Children.Add(_cmbCategory);

        Grid.SetRow(catRow, 1);
        rightGrid.Children.Add(catRow);

        // 検索ボックス
        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var lblSearch = new TextBlock
        {
            Text = loc["DlgEditLblSearch"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        DockPanel.SetDock(lblSearch, Dock.Left);
        searchRow.Children.Add(lblSearch);

        _txtSearch = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            ToolTip = loc["DlgEditSearchTooltip"]
        };
        _txtSearch.TextChanged += (_, _) => RefreshSearchResults();
        _txtSearch.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Down && _lstResults.Items.Count > 0)
            {
                _lstResults.SelectedIndex = 0;
                _lstResults.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _lstResults.Items.Count > 0)
            {
                if (_lstResults.SelectedIndex < 0) _lstResults.SelectedIndex = 0;
                InsertSelectedDictionaryEntry();
                e.Handled = true;
            }
        };
        searchRow.Children.Add(_txtSearch);

        Grid.SetRow(searchRow, 2);
        rightGrid.Children.Add(searchRow);

        // ヒット件数＆ヒント
        _lblSearchCount = new TextBlock
        {
            FontSize = 11,
            Foreground = BrushCategoryFg,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(_lblSearchCount, 3);
        rightGrid.Children.Add(_lblSearchCount);

        // 結果リスト
        _lstResults = new ListBox
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _lstResults.MouseDoubleClick += (_, _) => InsertSelectedDictionaryEntry();
        _lstResults.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                InsertSelectedDictionaryEntry();
                e.Handled = true;
            }
        };
        _lstResults.SelectionChanged += (_, _) => _btnInsert.IsEnabled = _lstResults.SelectedItem != null;
        Grid.SetRow(_lstResults, 4);
        rightGrid.Children.Add(_lstResults);

        // 挿入ボタン
        _btnInsert = new Button
        {
            Content = loc["DlgEditBtnInsert"],
            Padding = new Thickness(10, 6, 10, 6),
            IsEnabled = false
        };
        _btnInsert.Click += (_, _) => InsertSelectedDictionaryEntry();
        Grid.SetRow(_btnInsert, 5);
        rightGrid.Children.Add(_btnInsert);

        rightCard.Child = rightGrid;
        Grid.SetColumn(rightCard, 2);
        rootGrid.Children.Add(rightCard);

        outerGrid.Children.Add(rootGrid);
        outerGrid.SizeChanged += (_, e) => UpdateScale(e.NewSize.Width, e.NewSize.Height);
        Content = outerGrid;

        // Ctrl+Space で検索ボックスへジャンプ、Ctrl+Enter で適用
        PreviewKeyDown += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Space)
            {
                _txtSearch.Focus();
                _txtSearch.SelectAll();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter && _btnApply.IsEnabled)
            {
                BtnApply_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };

        // 初期値を流し込み
        var (title, lines) = MacroBinaryCodec.DecodeSlotForEditing(_originalBytes);
        _txtTitle.Text = title;
        for (int i = 0; i < 6; i++)
        {
            _txtLines[i].Text = lines[i];
        }

        _activeInput = _txtLines[0];
        _isInitializing = false;

        ValidateAllInputs();
        RefreshSearchResults();

        Loaded += (_, _) =>
        {
            if (_lastWindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            UpdateScale(outerGrid.ActualWidth, outerGrid.ActualHeight);
            _txtLines[0].Focus();
            _txtLines[0].CaretIndex = _txtLines[0].Text.Length;
        };

        Closing += (_, _) =>
        {
            _lastWindowState = WindowState;
            if (WindowState == WindowState.Normal)
            {
                _lastWidth = Width;
                _lastHeight = Height;
            }
            else if (RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
            {
                _lastWidth = RestoreBounds.Width;
                _lastHeight = RestoreBounds.Height;
            }
        };
    }

    private void UpdateScale(double contentWidth, double contentHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return;

        double scaleX = contentWidth / BaseContentWidth;
        double scaleY = contentHeight / BaseContentHeight;
        double scale = Math.Clamp(Math.Min(scaleX, scaleY), 0.85, 2.2);

        if (Math.Abs(_rootScale.ScaleX - scale) > 0.01)
        {
            _rootScale.ScaleX = scale;
            _rootScale.ScaleY = scale;
        }
    }

    private void ValidateAllInputs()
    {
        if (_isInitializing || _isBatchUpdating) return;

        var loc = Loc.Instance;
        var errors = new List<string>();

        // タイトル検証
        bool titleOk = MacroBinaryCodec.TryEncodeFf11Text(_txtTitle.Text, MaxTitleBytes, out _, out int titleBytes, out string? titleErr);
        _lblTitleCount.Text = $"{titleBytes} / {MaxTitleBytes} B";
        _lblTitleCount.Foreground = titleOk ? BrushCountOk : BrushCountError;
        _txtTitle.Background = titleOk ? Brushes.White : BrushInputErrorBg;
        if (!titleOk && titleErr != null)
        {
            errors.Add(loc.Format("ErrSlotTitlePrefix", titleErr));
        }

        // 本文6行検証
        for (int i = 0; i < 6; i++)
        {
            bool ok = MacroBinaryCodec.TryEncodeFf11Text(_txtLines[i].Text, MaxLineBytes, out _, out int lineBytes, out string? lineErr);
            _lblLineCounts[i].Text = $"{lineBytes,2} / {MaxLineBytes} B";
            _lblLineCounts[i].Foreground = ok ? BrushCountOk : BrushCountError;
            _txtLines[i].Background = ok ? Brushes.White : BrushInputErrorBg;
            if (!ok && lineErr != null)
            {
                errors.Add(loc.Format("ErrSlotLinePrefix", i + 1, lineErr));
            }
        }

        if (errors.Count > 0)
        {
            _txtValidationMessage.Foreground = BrushCountError;
            _txtValidationMessage.Text = string.Join(Environment.NewLine, errors.Take(3));
            _btnApply.IsEnabled = false;
            _btnApply.Opacity = 0.5;
        }
        else
        {
            _txtValidationMessage.Foreground = BrushCategoryFg;
            _txtValidationMessage.Text = loc["DlgEditHint"];
            _btnApply.IsEnabled = true;
            _btnApply.Opacity = 1.0;
        }
    }

    private void RefreshSearchResults()
    {
        if (_isInitializing) return;

        var loc = Loc.Instance;
        string query = _txtSearch.Text;
        string? category = _cmbCategory.SelectedIndex <= 0 ? null : _cmbCategory.SelectedItem as string;
        var dict = MacroBinaryCodec.Dictionary;

        var hits = dict.Search(query, category, maxResults: 200);
        _lstResults.Items.Clear();

        foreach (var entry in hits)
        {
            bool ambiguous = dict.IsAmbiguous(entry.Code);
            string insertToken = dict.FormatForEditing(entry.Code);

            var dock = new DockPanel { LastChildFill = true };

            var lblCat = new TextBlock
            {
                Text = entry.Category,
                FontSize = 11,
                Foreground = BrushCategoryFg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(lblCat, Dock.Right);
            dock.Children.Add(lblCat);

            if (ambiguous)
            {
                var lblAmb = new TextBlock
                {
                    Text = $"#{entry.Code:X8}",
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = BrushAmbiguousFg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                DockPanel.SetDock(lblAmb, Dock.Right);
                dock.Children.Add(lblAmb);
            }

            var lblWord = new TextBlock
            {
                Text = entry.Text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            dock.Children.Add(lblWord);

            string tip = string.IsNullOrEmpty(entry.Reading)
                ? loc.Format("DlgEditTipNoReading", insertToken, entry.Category, entry.Code)
                : loc.Format("DlgEditTipWithReading", insertToken, entry.Reading, entry.Category, entry.Code);

            var item = new ListBoxItem
            {
                Content = dock,
                Tag = entry,
                Padding = new Thickness(6, 4, 6, 4),
                ToolTip = tip
            };
            _lstResults.Items.Add(item);
        }

        _lblSearchCount.Text = hits.Count >= 200
            ? loc["DlgEditSearchLimit"]
            : loc.Format("DlgEditSearchMatchCount", hits.Count);
        _btnInsert.IsEnabled = _lstResults.SelectedItem != null;
    }

    private void InsertSelectedDictionaryEntry()
    {
        if (_lstResults.SelectedItem is not ListBoxItem item || item.Tag is not AutoTranslateEntry entry)
        {
            return;
        }

        string token = MacroBinaryCodec.Dictionary.FormatForEditing(entry.Code);
        TextBox target = _activeInput ?? _txtLines[0];

        int selStart = target.SelectionStart;
        int selLen = target.SelectionLength;
        string current = target.Text ?? string.Empty;

        target.Text = current.Remove(selStart, selLen).Insert(selStart, token);
        target.Focus();
        target.CaretIndex = selStart + token.Length;
    }

    private void OnLinePasting(int startLineIndex, DataObjectPastingEventArgs e)
    {
        string? pastedText = null;
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            pastedText = e.DataObject.GetData(DataFormats.UnicodeText) as string;
        }
        else if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            pastedText = e.DataObject.GetData(DataFormats.Text) as string;
        }

        if (string.IsNullOrEmpty(pastedText)) return;

        // 改行を含まない通常の1行ペーストは標準動作に任せる
        if (!pastedText.Contains('\n') && !pastedText.Contains('\r')) return;

        e.CancelCommand();

        string normalized = pastedText.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        // 行末の改行を巻き込んでコピーした場合、末尾の空要素1つだけ除外する
        if (lines.Length > 1 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        // 末尾改行付きで実質1行だけコピーしていた場合は、カーソル位置へ普通に挿入
        if (lines.Length == 1)
        {
            TextBox target = _txtLines[startLineIndex];
            int selStart = target.SelectionStart;
            int selLen = target.SelectionLength;
            string current = target.Text ?? string.Empty;

            target.Text = current.Remove(selStart, selLen).Insert(selStart, lines[0]);
            target.CaretIndex = selStart + lines[0].Length;
            return;
        }

        // 隠し機能: 外部からコピーした行番号付き複数行（1: 〜 / 1. 〜）の行番号を自動除去
        static bool HasLineNumberPrefix(string line) =>
            Regex.IsMatch(line, @"^\s*[1-6]\s*[:：.]\s?");

        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (nonEmptyLines.Length >= 2 && nonEmptyLines.All(HasLineNumberPrefix))
        {
            lines = lines
                .Select(l => Regex.Replace(l, @"^\s*[1-6]\s*[:：.]\s?", string.Empty))
                .ToArray();
        }

        // 複数行ペースト: 貼り付けた行から下に向かって順番に流し込む（最大6行目まで）
        _isBatchUpdating = true;
        int lastWrittenIndex = startLineIndex;
        try
        {
            for (int i = 0; i < lines.Length && startLineIndex + i < _txtLines.Length; i++)
            {
                int targetIndex = startLineIndex + i;
                _txtLines[targetIndex].Text = lines[i];
                lastWrittenIndex = targetIndex;
            }
        }
        finally
        {
            _isBatchUpdating = false;
        }

        ValidateAllInputs();

        _txtLines[lastWrittenIndex].Focus();
        _txtLines[lastWrittenIndex].CaretIndex = _txtLines[lastWrittenIndex].Text.Length;
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        string[] lines = _txtLines.Select(tb => tb.Text).ToArray();
        if (!MacroBinaryCodec.TryEncodeSlot(_originalBytes, _txtTitle.Text, lines, out byte[] newBytes, out string? error))
        {
            MessageBox.Show(
                error ?? Loc.Instance["DlgEditInputErrorFallback"],
                Loc.Instance["DlgEditInputErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ResultSlotBytes = newBytes;
        DialogResult = true;
    }
}