using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FF11MacroOrganizer.Core;

namespace FF11MacroOrganizer;

public partial class BookRenameDialog : Window
{
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

    public byte[] EncodedTitleBytes { get; private set; } = new byte[MacroBinaryCodec.TitleRecordSize];
    public string NewTitleText { get; private set; } = string.Empty;

    public BookRenameDialog(string targetLabel, string currentTitle)
    {
        InitializeComponent();

        TxtTargetInfo.Text = targetLabel;
        TxtBookTitle.Text = currentTitle;

        Loaded += (_, _) =>
        {
            TxtBookTitle.Focus();
            TxtBookTitle.SelectAll();
        };

        ValidateInput();
    }

    private void TxtBookTitle_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private void ValidateInput()
    {
        string text = TxtBookTitle.Text;
        bool ok = MacroBinaryCodec.TryEncodeBookTitle(
            text,
            out byte[] recordBytes,
            out int byteCount,
            out string? errorMessage);

        TxtByteCount.Text = $"{byteCount} / {MacroBinaryCodec.MaxBookTitleBytes} バイト";
        TxtByteCount.Foreground = ok ? NormalBrush : ErrorBrush;
        TxtError.Text = ok ? string.Empty : (errorMessage ?? "入力内容が不正です。");
        BtnOk.IsEnabled = ok;

        if (ok)
        {
            EncodedTitleBytes = recordBytes;
            NewTitleText = text;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (!BtnOk.IsEnabled) return;
        DialogResult = true;
    }
}