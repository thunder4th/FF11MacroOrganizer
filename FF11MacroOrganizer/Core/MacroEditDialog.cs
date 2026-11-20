using FF11MacroOrganizer.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FF11MacroOrganizer;

public sealed class MacroEditDialog : Window
{
    private const int MaxTitleBytes = 8;
    private const int MaxLineBytes = 60;

    private static readonly SolidColorBrush BrushCountOk = new(Color.FromRgb(75, 85, 99));
    private static readonly SolidColorBrush BrushCountError = new(Color.FromRgb(185, 28, 28));
    private static readonly SolidColorBrush BrushInputErrorBg = new(Color.FromRgb(254, 242, 242));
    private static readonly SolidColorBrush BrushCategoryFg = new(Color.FromRgb(107, 114, 128));
    private static readonly SolidColorBrush BrushAmbiguousFg = new(Color.FromRgb(180, 83, 9));

    private readonly byte[] _originalBytes;
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

    public byte[]? ResultSlotBytes { get; private set; }

    public MacroEditDialog(string slotHeaderLabel, byte[] originalSlotBytes)
    {
        _originalBytes = (byte[])originalSlotBytes.Clone();

        Title = "マクロ編集";
        Width = 1000;
        Height = 560;
        MinWidth = 840;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(249, 250, 251));

        var rootGrid = new Grid { Margin = new Thickness(14) };
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
            Text = "タイトル:",
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
            Text = "（全角4文字 / 半角8文字まで）",
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
            Text = "マクロ本文（各行 最大60バイト・定型文/アイテムは6バイト換算）",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
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
            Content = "すべてクリア",
            Padding = new Thickness(10, 6, 10, 6)
        };
        btnClearAll.Click += (_, _) =>
        {
            _txtTitle.Text = string.Empty;
            foreach (var tb in _txtLines) tb.Text = string.Empty;
            _txtLines[0].Focus();
        };
        DockPanel.SetDock(btnClearAll, Dock.Left);
        bottomDock.Children.Add(btnClearAll);

        _btnApply = new Button
        {
            Content = "適用 (反映)",
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
            Content = "キャンセル",
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
            Text = $"定型文・アイテム検索挿入（辞書 {dict.Count:N0} 件）",
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
            Text = "カテゴリ:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        DockPanel.SetDock(lblCat, Dock.Left);
        catRow.Children.Add(lblCat);

        _cmbCategory = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        _cmbCategory.Items.Add("すべて");
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
            Text = "検索:",
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
            ToolTip = "ひらがな・カタカナ・漢字・英数で検索できます（例: とまほ / けある / びくとり）"
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
            Content = "← カーソル位置へ挿入（ダブルクリック / Enter）",
            Padding = new Thickness(10, 6, 10, 6),
            IsEnabled = false
        };
        _btnInsert.Click += (_, _) => InsertSelectedDictionaryEntry();
        Grid.SetRow(_btnInsert, 5);
        rightGrid.Children.Add(_btnInsert);

        rightCard.Child = rightGrid;
        Grid.SetColumn(rightCard, 2);
        rootGrid.Children.Add(rightCard);

        Content = rootGrid;

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
            _txtLines[0].Focus();
            _txtLines[0].CaretIndex = _txtLines[0].Text.Length;
        };
    }

    private void ValidateAllInputs()
    {
        if (_isInitializing) return;

        var errors = new List<string>();

        // タイトル検証
        bool titleOk = MacroBinaryCodec.TryEncodeFf11Text(_txtTitle.Text, MaxTitleBytes, out _, out int titleBytes, out string? titleErr);
        _lblTitleCount.Text = $"{titleBytes} / {MaxTitleBytes} B";
        _lblTitleCount.Foreground = titleOk ? BrushCountOk : BrushCountError;
        _txtTitle.Background = titleOk ? Brushes.White : BrushInputErrorBg;
        if (!titleOk && titleErr != null)
        {
            errors.Add($"タイトル: {titleErr}");
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
                errors.Add($"{i + 1}行目: {lineErr}");
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
            _txtValidationMessage.Text = "ヒント: Ctrl+Space で右側の辞書検索へ移動 / Ctrl+Enter で適用\n※同名衝突がある単語は自動的に【名称#コード】形式で安全に挿入されます。";
            _btnApply.IsEnabled = true;
            _btnApply.Opacity = 1.0;
        }
    }

    private void RefreshSearchResults()
    {
        if (_isInitializing) return;

        string query = _txtSearch.Text;
        string? category = _cmbCategory.SelectedItem as string;
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
                ? $"挿入表記: {insertToken}\nカテゴリ: {entry.Category}\nコード: {entry.Code:X8}"
                : $"挿入表記: {insertToken}\n読み: {entry.Reading}\nカテゴリ: {entry.Category}\nコード: {entry.Code:X8}";

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
            ? "上位 200 件を表示中（文字を追加するとさらに絞り込めます）"
            : $"一致: {hits.Count} 件";
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

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        string[] lines = _txtLines.Select(tb => tb.Text).ToArray();
        if (!MacroBinaryCodec.TryEncodeSlot(_originalBytes, _txtTitle.Text, lines, out byte[] newBytes, out string? error))
        {
            MessageBox.Show(error ?? "入力内容にエラーがあります。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultSlotBytes = newBytes;
        DialogResult = true;
    }
}