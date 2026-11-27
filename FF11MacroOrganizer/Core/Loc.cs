using System.ComponentModel;
using System.Globalization;

namespace FF11MacroOrganizer.Core;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private Dictionary<string, string> _current = Ja;

    public bool IsJapanese { get; private set; } = true;
    public string LanguageCode => IsJapanese ? "ja" : "en";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
        => _current.TryGetValue(key, out var val) ? val : key;

    public string Format(string key, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public static string ResolveInitialLanguage(string? saved)
    {
        if (string.Equals(saved, "ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (string.Equals(saved, "en", StringComparison.OrdinalIgnoreCase)) return "en";
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    }

    public void SetLanguage(string code)
    {
        bool isJa = string.Equals(code, "ja", StringComparison.OrdinalIgnoreCase);
        IsJapanese = isJa;
        _current = isJa ? Ja : En;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsJapanese)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageCode)));
    }

    private static readonly Dictionary<string, string> Ja = new()
    {
        // Language toggle
        ["BtnLangSwitch"] = "EN",
        ["BtnLangSwitchTooltip"] = "表示言語と定型文辞書を英語に切り替えます",
        ["LangName"] = "日本語",

        // Toolbar
        ["BtnOpenFolder"] = "フォルダを開く...",
        ["BtnUndo"] = "元に戻す (Ctrl+Z)",
        ["BtnRedo"] = "やり直し (Ctrl+Y)",
        ["BtnSave"] = "変更を保存",
        ["BtnSaveCount"] = "変更を保存 ({0}件)",
        ["PaneModeTooltip"] = "1ペイン（編集集中）と2ペイン（整理・複製）を切り替えます",
        ["RbSinglePane"] = "1ペイン",
        ["RbDualPane"] = "2ペイン",
        ["PolRunning"] = "● FF11起動中 (保存ロック)",
        ["PolNotRunning"] = "● FF11未起動 (保存可能)",

        // Pane headers & center copy
        ["PaneTitleSingle"] = "マクロパレット",
        ["PaneTitleLeft"] = "左パレット",
        ["PaneTitleRight"] = "右パレット",
        ["PaneLabelLeft"] = "左",
        ["PaneLabelRight"] = "右",
        ["DirtyMarker"] = "* 未保存の変更あり",
        ["BtnClear"] = "クリア ▾",
        ["BtnClearTooltip"] = "現在のパレットまたはブックを一括クリア",
        ["LblChar"] = "キャラ:",
        ["BtnRenameCharTooltip"] = "キャラ表示名を設定",
        ["LblBook"] = "ブック:",
        ["BtnRenameBookTooltip"] = "ブック名を変更",
        ["LblPage"] = "ページ:",
        ["BtnPrevPageTooltip"] = "前のページへ",
        ["BtnNextPageTooltip"] = "次のページへ",
        ["CopyPageHeader"] = "パレット複製",
        ["CopyPageSub"] = "1ページ",
        ["CopyPageL2RTooltip"] = "左のパレット（20スロット）を右へ上書きコピー",
        ["CopyPageR2LTooltip"] = "右のパレット（20スロット）を左へ上書きコピー",
        ["CopyBookHeader"] = "ブック複製",
        ["CopyBookSub"] = "全10ページ",
        ["CopyBookL2RTooltip"] = "左のブック全体（10ページ・200スロット）を右へ上書きコピー",
        ["CopyBookR2LTooltip"] = "右のブック全体（10ページ・200スロット）を左へ上書きコピー",

        // Preview & footer
        ["BtnEdit"] = "編集...",
        ["PreviewSelectPrompt"] = "{0}スロットを選択すると内容が表示されます",
        ["SlotEmpty"] = "(空スロット)",
        ["SlotUntitled"] = "(無題)",
        ["PreviewHoverSuffix"] = " (プレビュー)",
        ["FooterHint"] = "操作: ダブルクリック = 編集 / Delete = クリア / ドラッグ = 入れ替え / Ctrl+ドラッグ = コピー / 右クリック = メニュー",

        // Context menus
        ["MenuEditSlot"] = "編集... (ダブルクリック / F2)",
        ["MenuCopySlot"] = "コピー",
        ["MenuPasteSlot"] = "貼り付け（上書き）",
        ["MenuClearSlot"] = "クリア（空にする） (Delete)",
        ["MenuClearPage"] = "現在のパレットをクリア（20スロット）",
        ["MenuClearBook"] = "現在のブックをクリア（全10ページ・200スロット）...",

        // Common titles & messages
        ["ConfirmTitle"] = "確認",
        ["ErrorTitle"] = "エラー",
        ["LoadFailedTitle"] = "読み込み失敗",
        ["ExitConfirmTitle"] = "終了確認",
        ["SaveDoneTitle"] = "保存完了",
        ["SaveAbortedTitle"] = "保存中止",
        ["PermissionErrorTitle"] = "権限エラー",
        ["SaveErrorTitle"] = "保存エラー",
        ["OpenFolderDialogTitle"] = "FF11 の USER フォルダ（またはキャラフォルダ）を選択",
        ["ConfirmDiscardAndOpen"] = "未保存の変更があります。破棄して別フォルダを読み込みますか？",
        ["ConfirmDiscardAndExit"] = "未保存の変更があります。破棄して終了しますか？",
        ["MsgNoMacroFilesFound"] = "指定されたフォルダ内に FF11 のマクロファイル（mcr.ttl / mcr*.dat）が見つかりませんでした。",
        ["MsgLoadError"] = "読み込み中にエラーが発生しました:\n{0}",
        ["StatusAutoLoadSkipped"] = "自動読み込みをスキップしました: {0}",
        ["StatusLoadComplete"] = "読み込み完了: {0} キャラ検出 ({1}){2}",
        ["StatusDictLoaded"] = " / 定型文辞書 {0} 件読込済",
        ["StatusCopiedSlot"] = "コピーしました: {0} ({1})",
        ["StatusClearedSlot"] = "{0}（Ctrl+Zで元に戻せます）",
        ["StatusAppliedUndoable"] = "{0} を反映しました（未保存状態・Ctrl+Zで取り消し可能）",
        ["StatusExecutedUndoable"] = "{0} を実行しました（未保存状態・Ctrl+Zで取り消し可能）",
        ["StatusUndone"] = "元に戻しました: {0}",
        ["StatusRedone"] = "やり直しました: {0}",
        ["StatusCharNameSet"] = "キャラ表示名を設定しました: {0}",
        ["StatusCharNameReset"] = "キャラ表示名を既定に戻しました: {0}",
        ["MsgCharNameSaveError"] = "キャラ表示名の保存に失敗しました:\n{0}",
        ["StatusPageAlreadyEmpty"] = "B{0:D2}-P{1} はすでに空です。",
        ["StatusBookAlreadyEmpty"] = "Book {0:D2} はすでに空です。",
        ["StatusBookSlotsAlreadyEmpty"] = "Book {0:D2} のスロットはすでに空です。",
        ["StatusLanguageChanged"] = "表示言語を切り替えました: {0}{1}",
        ["StatusInitialPrompt"] = "「フォルダを開く...」から FF11 の USER フォルダ（またはテスト用フォルダ）を選択してください。",
        ["MsgSaveServerNote"] = "\n\n※必要に応じて、FF11起動後にキャラクター選択画面からマクロの「サーバー保存」を行ってください。",
        ["MsgSaveBackupInfo"] = "{0}\n\nバックアップ先:\n{1}{2}",
        ["StatusSaved"] = "{0}（必要に応じてゲーム側でサーバー保存を行ってください）",
        ["MsgSavePermissionError"] = "ファイルへの書き込み権限がありません。\nアプリを「管理者として実行」してください。\n\n詳細: {0}",
        ["MsgSaveError"] = "保存中にエラーが発生しました:\n{0}",

        // Confirm dialogs (Copy / Clear)
        ["DlgCancel"] = "キャンセル",
        ["DlgCopyPageTitle"] = "パレット単位コピーの確認",
        ["DlgCopyPageHeadline"] = "コピー先パレットの全20スロットを上書きします。\nよろしいですか？",
        ["DlgCopyBookTitle"] = "ブック単位コピーの確認",
        ["DlgCopyBookHeadline"] = "コピー先ブックの全10ページ（200マクロ）を上書きします。\nよろしいですか？",
        ["DlgCopySource"] = "コピー元: {0}",
        ["DlgCopyDest"] = "コピー先: {0}",
        ["DlgCopyNoteUndo"] = "※実行後も Ctrl+Z で元に戻せます。",
        ["DlgCopyBookIncludeTitle"] = "ブック名も一緒にコピーする",
        ["DlgBtnCopyExec"] = "コピー実行",
        ["DlgClearPageTitle"] = "パレット一括クリアの確認",
        ["DlgClearPageHeadline"] = "現在のパレット内の全20スロットをクリアします。\nよろしいですか？",
        ["DlgClearBookTitle"] = "ブック一括クリアの確認",
        ["DlgClearBookHeadline"] = "対象ブックの全10ページ（200マクロ）をクリアします。\nよろしいですか？",
        ["DlgTarget"] = "対象: {0}",
        ["DlgClearPageNote"] = "クリア対象: {0} スロット使用中（※Ctrl+Zで取り消し可能）",
        ["DlgClearBookNote"] = "クリア対象: {0} ページ / {1} スロット使用中（※Ctrl+Zで取り消し可能）",
        ["DlgClearBookIncludeTitle"] = "ブック名もクリアする（無題に戻す）",
        ["DlgBtnClearExec"] = "クリア実行",

        // BookRenameDialog
        ["DlgRenameBookTitle"] = "ブック名の変更",
        ["DlgRenameBookPrompt"] = "新しいブック名（最大 15 バイト：半角15文字 / 全角7文字）",
        ["DlgByteCountFormat"] = "{0} / {1} バイト",
        ["DlgInvalidInput"] = "入力内容が不正です。",
        ["DlgBtnRename"] = "変更",

        // CharacterNameDialog
        ["DlgCharNameTitle"] = "キャラ表示名の設定",
        ["DlgCharIdFormat"] = "キャラ ID: {0}",
        ["DlgCharDetectedFormat"] = "フォルダ内 .txt からの自動検出: {0}",
        ["DlgCharNamePrompt"] = "アプリ内の表示名（空欄で保存すると既定表示に戻ります）",
        ["DlgCharNameNote"] = "※アプリ内の表示専用です（FF11のファイルは変更しません）",
        ["DlgBtnSet"] = "設定",

        // MacroEditDialog
        ["DlgEditTitle"] = "マクロ編集",
        ["DlgEditLblTitle"] = "タイトル:",
        ["DlgEditTitleHint"] = "（全角4文字 / 半角8文字まで）",
        ["DlgEditBodyHeader"] = "マクロ本文（各行 最大60バイト・定型文/アイテムは6バイト換算・複数行ペースト対応）",
        ["DlgEditBtnClearAll"] = "すべてクリア",
        ["DlgEditClearConfirmMsg"] = "タイトルと本文6行の入力内容をすべてクリアしますか？",
        ["DlgEditClearConfirmTitle"] = "すべてクリアの確認",
        ["DlgEditBtnApply"] = "適用 (反映)",
        ["DlgEditPickerTitle"] = "定型文・アイテム検索挿入（辞書 {0:N0} 件）",
        ["DlgEditLblCategory"] = "カテゴリ:",
        ["DlgEditCategoryAll"] = "すべて",
        ["DlgEditLblSearch"] = "検索:",
        ["DlgEditSearchTooltip"] = "ひらがな・カタカナ・漢字・英数で検索できます（例: とまほ / けある / びくとり）",
        ["DlgEditBtnInsert"] = "← カーソル位置へ挿入（ダブルクリック / Enter）",
        ["DlgEditHint"] = "ヒント: Ctrl+Space で右側の辞書検索へ移動 / Ctrl+Enter で適用\n※同名衝突がある単語は自動的に【名称#コード】形式で安全に挿入されます。",
        ["DlgEditSearchLimit"] = "上位 200 件を表示中（文字を追加するとさらに絞り込めます）",
        ["DlgEditSearchMatchCount"] = "一致: {0} 件",
        ["DlgEditTipNoReading"] = "挿入表記: {0}\nカテゴリ: {1}\nコード: {2:X8}",
        ["DlgEditTipWithReading"] = "挿入表記: {0}\n読み: {1}\nカテゴリ: {2}\nコード: {3:X8}",
        ["DlgEditInputErrorTitle"] = "入力エラー",
        ["DlgEditInputErrorFallback"] = "入力内容にエラーがあります。",

        // Core (MacroBinaryCodec / MacroRepository / UndoManager)
        ["ErrAmbiguousToken"] = "「【{0}】」は同名の定型文が複数あるため、定型文挿入から選択してください。",
        ["ErrInvalidSjisChar"] = "FF11 (Shift_JIS) で使用できない文字が含まれています: '{0}'",
        ["ErrControlCharNotAllowed"] = "改行や制御文字はブック名に使用できません。",
        ["ErrByteLimitExceeded"] = "文字数が上限を超えています ({0} / {1} バイト)",
        ["ErrSlotTitlePrefix"] = "タイトル: {0}",
        ["ErrSlotLinePrefix"] = "{0}行目: {1}",
        ["ErrInvalidFileSize"] = "ファイルサイズが不正です ({0} bytes): {1}",
        ["ErrInvalidTitleFileSize"] = "タイトルファイルサイズが不正です ({0} bytes): {1}",
        ["ErrFolderNotFound"] = "フォルダが見つかりません: {0}",
        ["RepoPolRunning"] = "pol.exe が起動しているため保存を中止しました。FF11を終了してから保存してください。",
        ["RepoNoChanges"] = "保存が必要な変更はありません。",
        ["RepoExternalModified"] = "読み込み後に外部で更新されたファイルがあるため保存を中止しました:\n{0}",
        ["RepoSavedCount"] = "{0} 件のファイルを保存しました。",
        ["UndoSwap"] = "入れ替え: {0} ⇄ {1}",
        ["UndoOverwriteCopy"] = "上書きコピー: {0} → {1}",
        ["UndoPaste"] = "貼り付け: {0}",
        ["UndoClearSlot"] = "クリア: {0}",
        ["UndoEditSlot"] = "編集: {0} B{1:D2}-P{2} {3}",
        ["UndoCopyBook"] = "ブックコピー: {0} B{1:D2} → {2} B{3:D2}",
        ["UndoCopyPage"] = "パレットコピー: {0} B{1:D2}-P{2} → {3} B{4:D2}-P{5}",
        ["UndoRenameBook"] = "ブック名変更: {0} B{1:D2} → {2}",
        ["UndoClearPage"] = "パレット一括クリア: {0} B{1:D2}-P{2}",
        ["UndoClearBook"] = "ブック一括クリア: {0} B{1:D2}{2}",
        ["UndoClearBookTitleSuffix"] = "（ブック名含む）",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        // Language toggle
        ["BtnLangSwitch"] = "日本語",
        ["BtnLangSwitchTooltip"] = "Switch UI language and Auto-Translate dictionary to Japanese",
        ["LangName"] = "English",

        // Toolbar
        ["BtnOpenFolder"] = "Open Folder...",
        ["BtnUndo"] = "Undo (Ctrl+Z)",
        ["BtnRedo"] = "Redo (Ctrl+Y)",
        ["BtnSave"] = "Save Changes",
        ["BtnSaveCount"] = "Save Changes ({0})",
        ["PaneModeTooltip"] = "Switch between Single Pane (editing) and Dual Pane (organizing/copying)",
        ["RbSinglePane"] = "1 Pane",
        ["RbDualPane"] = "2 Panes",
        ["PolRunning"] = "● FF11 Running (Locked)",
        ["PolNotRunning"] = "● FF11 Closed (Ready)",

        // Pane headers & center copy
        ["PaneTitleSingle"] = "Macro Palette",
        ["PaneTitleLeft"] = "Left Palette",
        ["PaneTitleRight"] = "Right Palette",
        ["PaneLabelLeft"] = "L",
        ["PaneLabelRight"] = "R",
        ["DirtyMarker"] = "* Unsaved changes",
        ["BtnClear"] = "Clear ▾",
        ["BtnClearTooltip"] = "Bulk clear current palette or book",
        ["LblChar"] = "Char:",
        ["BtnRenameCharTooltip"] = "Set character display name",
        ["LblBook"] = "Book:",
        ["BtnRenameBookTooltip"] = "Rename book",
        ["LblPage"] = "Page:",
        ["BtnPrevPageTooltip"] = "Previous page",
        ["BtnNextPageTooltip"] = "Next page",
        ["CopyPageHeader"] = "Copy Palette",
        ["CopyPageSub"] = "1 Page",
        ["CopyPageL2RTooltip"] = "Overwrite right palette (20 slots) with left",
        ["CopyPageR2LTooltip"] = "Overwrite left palette (20 slots) with right",
        ["CopyBookHeader"] = "Copy Book",
        ["CopyBookSub"] = "All 10 Pages",
        ["CopyBookL2RTooltip"] = "Overwrite right book (10 pages / 200 slots) with left",
        ["CopyBookR2LTooltip"] = "Overwrite left book (10 pages / 200 slots) with right",

        // Preview & footer
        ["BtnEdit"] = "Edit...",
        ["PreviewSelectPrompt"] = "{0}Select a slot to view its contents",
        ["SlotEmpty"] = "(Empty)",
        ["SlotUntitled"] = "(Untitled)",
        ["PreviewHoverSuffix"] = " (Preview)",
        ["FooterHint"] = "Controls: Double-click = Edit / Delete = Clear / Drag = Swap / Ctrl+Drag = Copy / Right-click = Menu",

        // Context menus
        ["MenuEditSlot"] = "Edit... (Double-click / F2)",
        ["MenuCopySlot"] = "Copy",
        ["MenuPasteSlot"] = "Paste (Overwrite)",
        ["MenuClearSlot"] = "Clear Slot (Delete)",
        ["MenuClearPage"] = "Clear Current Palette (20 slots)",
        ["MenuClearBook"] = "Clear Current Book (10 pages / 200 slots)...",

        // Common titles & messages
        ["ConfirmTitle"] = "Confirm",
        ["ErrorTitle"] = "Error",
        ["LoadFailedTitle"] = "Load Failed",
        ["ExitConfirmTitle"] = "Confirm Exit",
        ["SaveDoneTitle"] = "Save Complete",
        ["SaveAbortedTitle"] = "Save Aborted",
        ["PermissionErrorTitle"] = "Permission Error",
        ["SaveErrorTitle"] = "Save Error",
        ["OpenFolderDialogTitle"] = "Select FFXI USER folder (or character folder)",
        ["ConfirmDiscardAndOpen"] = "You have unsaved changes. Discard them and open another folder?",
        ["ConfirmDiscardAndExit"] = "You have unsaved changes. Discard them and exit?",
        ["MsgNoMacroFilesFound"] = "No FFXI macro files (mcr.ttl / mcr*.dat) were found in the selected folder.",
        ["MsgLoadError"] = "An error occurred while loading:\n{0}",
        ["StatusAutoLoadSkipped"] = "Skipped auto-load: {0}",
        ["StatusLoadComplete"] = "Loaded: {0} character(s) found ({1}){2}",
        ["StatusDictLoaded"] = " / Auto-Translate dict: {0} entries",
        ["StatusCopiedSlot"] = "Copied: {0} ({1})",
        ["StatusClearedSlot"] = "{0} (Ctrl+Z to undo)",
        ["StatusAppliedUndoable"] = "Applied: {0} (Unsaved - Ctrl+Z to undo)",
        ["StatusExecutedUndoable"] = "Executed: {0} (Unsaved - Ctrl+Z to undo)",
        ["StatusUndone"] = "Undone: {0}",
        ["StatusRedone"] = "Redone: {0}",
        ["StatusCharNameSet"] = "Character display name set: {0}",
        ["StatusCharNameReset"] = "Character display name reset to default: {0}",
        ["MsgCharNameSaveError"] = "Failed to save character display name:\n{0}",
        ["StatusPageAlreadyEmpty"] = "B{0:D2}-P{1} is already empty.",
        ["StatusBookAlreadyEmpty"] = "Book {0:D2} is already empty.",
        ["StatusBookSlotsAlreadyEmpty"] = "All slots in Book {0:D2} are already empty.",
        ["StatusLanguageChanged"] = "Language switched: {0}{1}",
        ["StatusInitialPrompt"] = "Click \"Open Folder...\" to select your FFXI USER folder (or a test folder).",
        ["MsgSaveServerNote"] = "\n\n*If needed, please upload your macros to the server from the FFXI character select screen.",
        ["MsgSaveBackupInfo"] = "{0}\n\nBackup location:\n{1}{2}",
        ["StatusSaved"] = "{0} (Upload to server in-game if needed)",
        ["MsgSavePermissionError"] = "Write permission denied.\nPlease run the app as Administrator.\n\nDetails: {0}",
        ["MsgSaveError"] = "An error occurred while saving:\n{0}",

        // Confirm dialogs (Copy / Clear)
        ["DlgCancel"] = "Cancel",
        ["DlgCopyPageTitle"] = "Confirm Palette Copy",
        ["DlgCopyPageHeadline"] = "Overwrite all 20 slots in the destination palette?\nAre you sure?",
        ["DlgCopyBookTitle"] = "Confirm Book Copy",
        ["DlgCopyBookHeadline"] = "Overwrite all 10 pages (200 macros) in the destination book?\nAre you sure?",
        ["DlgCopySource"] = "Source: {0}",
        ["DlgCopyDest"] = "Destination: {0}",
        ["DlgCopyNoteUndo"] = "*You can undo this action with Ctrl+Z.",
        ["DlgCopyBookIncludeTitle"] = "Also copy book title",
        ["DlgBtnCopyExec"] = "Copy",
        ["DlgClearPageTitle"] = "Confirm Clear Palette",
        ["DlgClearPageHeadline"] = "Clear all 20 slots in the current palette?\nAre you sure?",
        ["DlgClearBookTitle"] = "Confirm Clear Book",
        ["DlgClearBookHeadline"] = "Clear all 10 pages (200 macros) in the target book?\nAre you sure?",
        ["DlgTarget"] = "Target: {0}",
        ["DlgClearPageNote"] = "Used slots to clear: {0} (*Ctrl+Z to undo)",
        ["DlgClearBookNote"] = "To clear: {0} page(s) / {1} used slot(s) (*Ctrl+Z to undo)",
        ["DlgClearBookIncludeTitle"] = "Also clear book title (reset to untitled)",
        ["DlgBtnClearExec"] = "Clear",

        // BookRenameDialog
        ["DlgRenameBookTitle"] = "Rename Book",
        ["DlgRenameBookPrompt"] = "New book name (max 15 bytes: 15 ASCII / 7 full-width)",
        ["DlgByteCountFormat"] = "{0} / {1} bytes",
        ["DlgInvalidInput"] = "Invalid input.",
        ["DlgBtnRename"] = "Rename",

        // CharacterNameDialog
        ["DlgCharNameTitle"] = "Set Character Display Name",
        ["DlgCharIdFormat"] = "Character ID: {0}",
        ["DlgCharDetectedFormat"] = "Auto-detected from .txt file: {0}",
        ["DlgCharNamePrompt"] = "Display name in app (leave blank to reset to default)",
        ["DlgCharNameNote"] = "*For display within this app only (FFXI files are not modified).",
        ["DlgBtnSet"] = "Set",

        // MacroEditDialog
        ["DlgEditTitle"] = "Edit Macro",
        ["DlgEditLblTitle"] = "Title:",
        ["DlgEditTitleHint"] = "(max 8 bytes: 8 ASCII / 4 full-width)",
        ["DlgEditBodyHeader"] = "Macro Lines (max 60 B/line, AT/Item = 6 B, multi-line paste)",
        ["DlgEditBtnClearAll"] = "Clear All",
        ["DlgEditClearConfirmMsg"] = "Clear the title and all 6 macro lines?",
        ["DlgEditClearConfirmTitle"] = "Confirm Clear All",
        ["DlgEditBtnApply"] = "Apply",
        ["DlgEditPickerTitle"] = "Auto-Translate & Item Picker ({0:N0} entries)",
        ["DlgEditLblCategory"] = "Category:",
        ["DlgEditCategoryAll"] = "All",
        ["DlgEditLblSearch"] = "Search:",
        ["DlgEditSearchTooltip"] = "Search by name (e.g., cure / mighty / berserk)",
        ["DlgEditBtnInsert"] = "← Insert at cursor (Double-click / Enter)",
        ["DlgEditHint"] = "Tip: Ctrl+Space to jump to search / Ctrl+Enter to apply\n*Ambiguous names are automatically formatted as 【Name#Code】.",
        ["DlgEditSearchLimit"] = "Showing top 200 results (type more to narrow down)",
        ["DlgEditSearchMatchCount"] = "Matches: {0}",
        ["DlgEditTipNoReading"] = "Insert token: {0}\nCategory: {1}\nCode: {2:X8}",
        ["DlgEditTipWithReading"] = "Insert token: {0}\nReading: {1}\nCategory: {2}\nCode: {3:X8}",
        ["DlgEditInputErrorTitle"] = "Input Error",
        ["DlgEditInputErrorFallback"] = "There is an error in the input.",

        // Core (MacroBinaryCodec / MacroRepository / UndoManager)
        ["ErrAmbiguousToken"] = "\"【{0}】\" matches multiple entries. Please insert it from the picker.",
        ["ErrInvalidSjisChar"] = "Contains a character unsupported by FFXI (Shift_JIS): '{0}'",
        ["ErrControlCharNotAllowed"] = "Line breaks and control characters are not allowed in book names.",
        ["ErrByteLimitExceeded"] = "Length exceeds limit ({0} / {1} bytes)",
        ["ErrSlotTitlePrefix"] = "Title: {0}",
        ["ErrSlotLinePrefix"] = "Line {0}: {1}",
        ["ErrInvalidFileSize"] = "Invalid file size ({0} bytes): {1}",
        ["ErrInvalidTitleFileSize"] = "Invalid title file size ({0} bytes): {1}",
        ["ErrFolderNotFound"] = "Folder not found: {0}",
        ["RepoPolRunning"] = "Save aborted because pol.exe is running. Please exit FFXI before saving.",
        ["RepoNoChanges"] = "No changes to save.",
        ["RepoExternalModified"] = "Save aborted because the following file(s) were modified externally after loading:\n{0}",
        ["RepoSavedCount"] = "Saved {0} file(s).",
        ["UndoSwap"] = "Swap: {0} ⇄ {1}",
        ["UndoOverwriteCopy"] = "Copy: {0} → {1}",
        ["UndoPaste"] = "Paste: {0}",
        ["UndoClearSlot"] = "Clear: {0}",
        ["UndoEditSlot"] = "Edit: {0} B{1:D2}-P{2} {3}",
        ["UndoCopyBook"] = "Copy Book: {0} B{1:D2} → {2} B{3:D2}",
        ["UndoCopyPage"] = "Copy Palette: {0} B{1:D2}-P{2} → {3} B{4:D2}-P{5}",
        ["UndoRenameBook"] = "Rename Book: {0} B{1:D2} → {2}",
        ["UndoClearPage"] = "Clear Palette: {0} B{1:D2}-P{2}",
        ["UndoClearBook"] = "Clear Book: {0} B{1:D2}{2}",
        ["UndoClearBookTitleSuffix"] = " (incl. title)",
    };
}