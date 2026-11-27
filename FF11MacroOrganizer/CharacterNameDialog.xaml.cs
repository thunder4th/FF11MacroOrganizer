using System.Windows;

namespace FF11MacroOrganizer;

public partial class CharacterNameDialog : Window
{
    public string? CustomName { get; private set; }

    public CharacterNameDialog(string characterId, string? currentCustomName, string? detectedName)
    {
        InitializeComponent();

        TxtTargetInfo.Text = $"キャラ ID: {characterId}";

        if (!string.IsNullOrWhiteSpace(detectedName))
        {
            TxtDetectedInfo.Text = $"フォルダ内 .txt からの自動検出: {detectedName}";
            TxtDetectedInfo.Visibility = Visibility.Visible;
        }

        // カスタム名があればそれを、未設定なら自動検出名を初期値として編集しやすくする
        TxtCharacterName.Text = !string.IsNullOrWhiteSpace(currentCustomName)
            ? currentCustomName
            : (detectedName ?? string.Empty);

        Loaded += (_, _) =>
        {
            TxtCharacterName.Focus();
            TxtCharacterName.SelectAll();
        };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string trimmed = TxtCharacterName.Text.Trim();
        CustomName = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        DialogResult = true;
    }
}