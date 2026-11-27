# FF11 Macro Organizer

[日本語](#日本語) | [English](#english)

---

<a id="日本語"></a>

## 日本語

FINAL FANTASY XI（FF11）のローカルマクロファイル（`mcr*.dat` / `mcr.ttl`）を、Windows 上で視覚的に整理・編集するためのデスクトップツールです。

編集に集中できる **1ペインモード** と、キャラクター間・ブック間・パレット間の比較・ドラッグ＆ドロップ整理に便利な **2ペインモード** の切り替えに対応。さらに、ローカルの FF11 クライアント（DAT ファイル）から日本語・英語の定型文／アイテム辞書を自動読み込みし、検索しながらマクロ本文を編集できます。

### 主な機能

#### 1. 1ペイン / 2ペイン切り替えによるマクロ整理
- **表示モード切り替え**：ツールバー右上の `[1ペイン | 2ペイン]` から、用途に合わせて画面構成をワンクリックで切り替えられます（終了時のモードを記憶し、次回起動時に自動復元します）。
- **左右独立プレビュー**：2ペイン時は左右それぞれの選択スロットを同時に並べて中身を比較できます。操作対象（アクティブ側）は青、非アクティブ側はグレーで区別されます。
- **ドラッグ＆ドロップ整理**：同じパレット内はもちろん、別ページ・別ブック・別キャラクター間でもスロットを直感的に移動できます（ドラッグ中は半透明プレビューがカーソルに追従します）。
  - **ドラッグ**：スロットの入れ替え（Swap）
  - **`Ctrl` + ドラッグ**：上書きコピー
- **パレット単位コピー / ブック単位コピー**：中央の矢印ボタンから、表示中のパレット（全20スロット）またはブック全体（全10ページ / 200スロット、ブック名の同時コピー選択可）を左右ペイン間で丸ごと複製できます。
- **パレット / ブック一括クリア**：各ペイン右上の **「クリア ▾」** ボタンから、現在のパレット（20スロット）またはブック全体（全10ページ / 200スロット、ブック名クリア選択可）を一括で空にできます。
- **ブック名の直接編集**：ブック選択欄の右にある **`✎`** ボタンから、ブック名（最大15バイト：半角15文字 / 全角7文字）を直接編集・クリアできます。
- **キャラ表示名のアプリ内設定**：キャラ選択欄の右にある **`✎`** ボタンから、英数字のキャラ ID に対してアプリ内専用の表示名を設定・保存できます（FF11 のローカルファイルは変更しません）。
- **右クリックメニュー / ショートカット**：スロット単位での編集・コピー・貼り付け・クリアに加え、選択スロットの `Delete` キーによる即時クリアにも対応しています。
- **Undo / Redo**：スロット編集・移動・クリア・ブック名変更・一括コピー／一括クリアなどすべての編集操作を `Ctrl+Z` / `Ctrl+Y` で取り消し・やり直しできます。

#### 2. マクロテキスト編集と定型文・アイテム検索挿入（日英対応）
- **専用編集ウィンドウ**：スロットのダブルクリック、プレビュー右上の **「編集...」** ボタン、右クリック →「編集...」、または選択中の `F2` キーで開きます。
- **画面サイズ連動の拡大縮小**：メイン画面・編集ウィンドウともに、ウィンドウのリサイズや最大化に合わせて文字や入力欄が拡大縮小されるため、視認性を確保しながら快適に編集できます（編集ウィンドウのサイズは起動中記憶されます）。
- **複数行ペースト対応**：クリップボードの複数行テキストを行頭から下の行へ一括貼り付けできます。
- **リアルタイム容量チェック**：タイトル（最大8バイト）と本文6行（各行最大60バイト）を、Shift_JIS および定型文6バイト固定換算でリアルタイムに計測し、超過や使用不可文字を検知します。
- **定型文・アイテム検索ピッカー（日本語 / 英語）**：ローカルの FF11 クライアント（`ROM` 配下）から、通常定型文・アビリティ/WS・魔法・エリア・ジョブ・アイテム名を読み込み、カテゴリ絞り込みやキーワード検索でカーソル位置へ差し込めます。
  - ひらがな・カタカナ・全角半角・大文字小文字の揺れを吸収して検索できます。
  - 同名の定型文・アイテムが存在する場合は、自動的に `【名称#コード】` 形式で挿入・保持し、保存時の別コード化けを防ぎます。

#### 3. UI 多言語切り替え（日本語 / English）
- ツールバー右上の **`EN`** / **`日本語`** ボタンから、アプリの再起動なしで UI 言語と定型文・アイテム辞書の両方を即座に切り替えられます（選択言語は保存され、初回起動時は OS 言語に合わせて自動選択されます）。
- 日本語版・北米版・欧州版（JP / US / EU）の FF11 インストールフォルダ自動検出に対応しています。

#### 4. 安全性・データ保護機能
- **FF11 起動検知ロック**：`pol.exe` の起動を2秒ごとに監視し、ゲーム起動中は誤上書き事故を防ぐため保存ボタンを自動でロックします。
- **自動バックアップ**：保存を実行するたび、上書き前の対象ファイルをアプリと同じフォルダ内の `Backups\タイムスタンプ\` へ自動退避します。
- **バイナリ整合性の維持**：FF11 のマクロファイル形式（ヘッダ・MD5チェックサム・定型文バイナリ）を維持したまま安全に書き出します。

---

### 動作環境

- **OS**：Windows 10 / 11（64bit）
- **ゲームクライアント**：FINAL FANTASY XI（Windows 日本語版 / 北米版 / 欧州版）
- **ランタイム**：.NET 10 デスクトップランタイム（x64）
  - ※ランタイム同梱版（`self-contained`）をご利用の場合は不要です。
  - ※軽量版（`framework-dependent`）をご利用の場合のみ必要です。未導入の場合は Microsoft 公式より「.NET Desktop Runtime 10.0 (x64)」をインストールしてください。

---

### 導入と使い方

#### 起動方法
1. 任意の専用フォルダ（例：`D:\Tools\FF11MacroOrganizer\`）を作成し、`FF11MacroOrganizer.exe` を配置します。
   - ※同じフォルダに自動バックアップ（`Backups\`）や設定ファイル（`last_folder.txt` / `pane_mode.txt` / `language.txt` / `character_names.json`）が生成されるため、デスクトップ直下ではなく専用フォルダへの配置を推奨します。
2. `FF11MacroOrganizer.exe` をダブルクリックして起動します。
   - **管理者権限（UAC）の確認について**：既定のインストール先である `C:\Program Files (x86)\PlayOnline\...` 配下のマクロファイルを書き換えるために管理者権限が必要です。
3. 起動時に「前回開いたフォルダ」または「既定の FF11 `USER` フォルダ」と定型文辞書を自動で読み込みます。別のフォルダを読み込む場合は左上の **「フォルダを開く...」** から指定してください。

#### 初回起動時に「Windows によって PC が保護されました」と表示される場合

本ツールは個人製作の無料ツールであり、商用の電子署名（コードサイニング証明書）を付与していないため、初回起動時に Windows SmartScreen の警告画面が表示されることがあります。

内容をご確認のうえ実行する場合は、以下の手順で起動してください。

1. 青い警告画面の左側に表示される **「詳細情報」** をクリックします。
2. 画面下部に表示される **「実行」** ボタンをクリックします。
3. 続けて管理者権限（UAC）の確認画面（「不明な発行元」）が表示された場合は、ファイル名等を確認のうえ **「はい」** を選択します。

※初回のみこの操作が必要ですが、2回目以降は SmartScreen の警告は表示されなくなります。

#### キャラクター表示名の設定（2通りの方法）

複数キャラクター・複数アカウントを識別しやすくするため、以下の方法でキャラ名を表示できます（優先順位：**①アプリ内設定 ＞ ②フォルダ内 `.txt` ＞ ③キャラ ID のみ**）。

1. **アプリ内から設定（おすすめ）**：キャラ選択欄の右にある **`✎`** ボタンをクリックし、表示名を入力して「設定」を押します。設定はアプリと同じフォルダの `character_names.json` にキャラ ID ごとに保存され、FF11 のファイルには一切書き込みません。
2. **`.txt` ファイルによる自動検出（従来互換）**：各キャラの英数字フォルダ（例：`USER\1a2b3c\`）内に `キャラクター名.txt`（中身は空で可）を配置しておくと、自動的に `キャラクター名 (1a2b3c)` と表示されます。

#### 操作一覧

**メイン画面**
| 操作 | 動作 |
| :--- | :--- |
| クリック | スロットを選択（該当ペインの下部プレビューに内容を表示） |
| マウスオーバー | カーソルを乗せたスロットを一時プレビュー（外すと選択中スロットへ復帰） |
| ダブルクリック / `F2` / プレビュー「編集...」 | 選択中スロットの編集ウィンドウを開く |
| `Delete` | 選択中スロットを即座にクリア（`Ctrl+Z` で元に戻せます） |
| ドラッグ＆ドロップ | スロット同士の入れ替え |
| `Ctrl` + ドラッグ＆ドロップ | スロットの上書きコピー |
| 右クリック | 編集・コピー・貼り付け・クリアメニュー |
| キャラ欄 / ブック欄の `✎` ボタン | キャラ表示名の設定 / ブック名の変更 |
| ペイン右上の「クリア ▾」ボタン | 現在のパレット（20スロット）またはブック（200スロット）を一括クリア |
| `▲` / `▼` ボタン | 前後のページ（パレット）へ移動 |
| ツールバー右上 `EN` / `日本語` ボタン | UI 表示言語および定型文・アイテム辞書を即時切り替え |
| `Ctrl+Z` / `Ctrl+Y` | 元に戻す / やり直し |
| `Ctrl+S` | 変更を保存（未保存変更があり、FF11 未起動時のみ） |

**編集ウィンドウ**
| 操作 | 動作 |
| :--- | :--- |
| `Enter`（タイトル・1〜5行目） | 次の入力欄へ移動 |
| `Ctrl+Space` | 右側の定型文・アイテム検索欄へフォーカス移動 |
| 検索結果のダブルクリック / `Enter` | 選択した定型文・アイテムを直前の入力欄のカーソル位置へ挿入 |
| ウィンドウ端のドラッグ / 最大化 | 編集画面全体（文字・入力欄・検索リスト）を拡大縮小 |
| `Ctrl+Enter` | 編集内容を適用してウィンドウを閉じる |
| `Esc` | キャンセルして閉じる |

---

### 編集画面における定型文の表記ルール

- **通常の定型文・アイテム**：`【ビクトリースマイト】`、`【Ｔ．トマホーク】` のように隅付き括弧で表示されます。
- **同名衝突がある単語**：アビリティとアイテムで名称が重複する場合など、一意に特定できない単語は `【トマホーク#02011F69】` のように 8桁の16進コード付きで表示されます。`#コード` 部分を消さずに編集すれば、元の定型文コードを正確に保持したまま保存されます。
- **辞書未登録のコード**：日本語モードでは `【定型文:XXXXXXXX】`、英語モードでは `【AT:XXXXXXXX】` の形式で保持されます。

---

### 保存とバックアップについて

- 画面上での移動・コピー・編集・ブック名変更・一括クリアは、左上の **「変更を保存」** を押すまで実ファイルには書き込まれません（キャラ表示名設定のみアプリ専用設定として即時保存されます）。
- 保存を実行すると、変更があったファイル（`mcr*.dat` / `mcr.ttl`）のみが書き出され、直前の状態が `Backups\` フォルダに保存されます。
- 保存後、FF11 を起動した際は必要に応じてキャラクター選択画面からマクロの **「サーバー保存」** を行ってください。
- 万が一元に戻したい場合は、FF11 を終了した状態で `Backups\` 内の該当ファイルを元のキャラフォルダへ上書きコピーしてください。

---

### 更新履歴

- **v2.2.0**
  - UI の多言語化（日本語 / English）に対応し、ツールバーの `EN` / `日本語` ボタンから UI と定型文・アイテム辞書を再起動なしで即時切り替え可能に（選択言語を `language.txt` に保存）
  - 英語版の定型文・アイテム DAT 読み込みおよび JP / US / EU リージョンの FF11 インストールフォルダ自動検出に対応
  - ブック名の直接編集機能を追加（ブック欄右の `✎` ボタン、リアルタイム15バイト制限チェック、Undo/Redo 対応）
  - キャラ表示名のアプリ内設定・永続化機能を追加（キャラ欄右の `✎` ボタン、`character_names.json` へキャラ ID 基準で保存）
  - パレット（20スロット）／ブック（全10ページ・200スロット）の一括クリア機能を追加（Undo/Redo・ブック名クリア選択対応）
  - 中央のパレット／ブック複製ボタンを方向別カラー付きベクター矢印デザインへ刷新
  - パレット・ブックのコピー／一括クリア確認ダイアログのデザインを統一
  - 1ペイン表示時のツールバー余白およびヘッダ折り返しレイアウトを改善
- **v2.1.1**
  - マクロ編集画面で複数行テキストの一括ペーストに対応
  - メイン画面プレビュー欄の行番号をコピー対象から分離し、本文のみ綺麗にコピーできるよう改善
- **v2.1.0**
  - ツールバーに `[1ペイン | 2ペイン]` モード切り替えを追加（終了時のモードを記憶・自動復元）
  - プレビューエリア（黒板）を左右独立化し、アクティブ側の強調表示および「編集...」ボタンを追加
  - 編集ウィンドウのリサイズ・最大化に連動した全体スケーリングに対応（セッション中のサイズ記憶）
  - ドラッグ＆ドロップ中の半透明スロットプレビュー追従表示を実装
  - 選択中スロットの `Delete` キーによる即時クリアを追加（Undo 対応）
  - ページ送り矢印を上下（`▲` / `▼`）に変更、下部操作ヒントを更新
  - 安全性向上のため「ロック一時解除」機能を廃止し、編集画面「すべてクリア」に確認ダイアログを追加
  - 保存完了時のメッセージにサーバー保存の案内を追記
- **v2.0.0**
  - マクロテキスト編集ウィンドウおよびリアルタイム 6バイト換算カウンタを追加
  - 日本語アイテム DAT の読み込みに対応し、定型文・アイテム検索挿入ピッカーを実装
  - パレット単位コピー機能を追加
  - マウスオーバー解除時に選択中スロットのプレビューへ戻るよう改善
- **v1.0.0**
  - 初版リリース（2ペイン D&D 整理、ブック単位コピー、Undo/Redo、通常定型文展開、`pol.exe` 起動監視、自動バックアップ）

---

### 注意事項・免責事項

- 本ツールは非公式の個人製作ツールであり、株式会社スクウェア・エニックスとは一切関係ありません。
- 本ツールはオフライン環境でローカルのマクロファイル（`mcr*.dat` / `mcr.ttl`）を読み書きし、参照用にクライアント内の DAT ファイルから辞書データを読み取るものです。ゲームのメモリ改変や通信への介入は行いませんが、**非公式ツールの使用がゲームの利用規約に抵触する可能性を完全に否定するものではありません。**
- **本ツールの使用（または使用不能）に起因して生じたアカウントの停止・BAN、データ破損・消失、その他いかなる直接的・間接的損害についても、製作者は一切の責任を負いません。** 利用規約をご確認のうえ、すべて利用者ご自身の責任においてご使用ください。
- FF11 の起動中（キャラクターログイン中）にマクロファイルを書き換えてもゲーム内には即座に反映されず、ログアウト時にゲーム側のデータで上書きされる場合があります。必ず **FF11（PlayOnline）を終了した状態で保存** してください。
- ゲームのバージョンアップ等によりファイル仕様が変更された場合、正常に動作しなくなる可能性があります。初回利用時や大幅な整理の前には、ご自身でも `USER` フォルダ全体のバックアップを取ることをおすすめします。

---

### バグ報告・ご要望・応援メッセージ

- **バグ報告・機能要望（GitHub アカウントをお持ちの方）**
  [GitHub Issues](https://github.com/thunder4th/FF11MacroOrganizer/issues) よりお寄せください。
- **バグ報告・機能要望（匿名・アカウント不要）**
  [フィードバックフォーム（Google フォーム）](https://forms.gle/YdbgECE2FRJJP4on8) よりお送りいただけます（ログイン不要・完全匿名）。
- **感想・応援メッセージ**
  [マシュマロ](https://marshmallow-qa.com/tomqjx7vldwzhn1) よりお気軽にどうぞ（※不具合報告や機能要望は上記フォームまたは Issues へお願いします）。

---

### 開発支援（投げ銭）

本ツールは無料・オープンソースで公開しています。もし日々のマクロ整理のお役に立ちましたら、開発継続の励みとして以下より応援いただけると嬉しいです（機能追加やサポートの対価ではなく、任意の応援として受け付けております）。

- [OFUSE で応援する](https://ofuse.me/dc1806a8)
- [Ko-fi で応援する](https://ko-fi.com/thunder4th)

---

### ライセンス

本ソフトウェアは **MIT License** のもとで公開しています。全文は同梱の [`LICENSE`](./LICENSE) ファイルをご参照ください。

Copyright (c) 2026 thunder4th

- 本ツール内で参照・表示されるゲームデータ（定型文・アイテム名等）の著作権は、すべて株式会社スクウェア・エニックスに帰属します（本ツールにゲームデータ自体は同梱していません）。

---

<a id="english"></a>

## English

A Windows desktop tool for visually organizing and editing local **FINAL FANTASY XI (FFXI)** macro files (`mcr*.dat` / `mcr.ttl`).

Switch effortlessly between **Single Pane mode** for focused editing and **Dual Pane mode** for side-by-side comparison and drag-and-drop organization across characters, books, and palettes. It automatically loads both **English and Japanese Auto-Translate & Item dictionaries** directly from your local FFXI client DAT files so you can search and insert entries while editing.

### Key Features

#### 1. Macro Organization (Single Pane / Dual Pane)
- **Pane Mode Switch**: Toggle between `[1 Pane | 2 Panes]` in the top-right toolbar (your last mode is remembered and restored on startup).
- **Independent Side-by-Side Previews**: In Dual Pane mode, inspect selected slots from both panes simultaneously. The active pane is highlighted in blue and the inactive pane in gray.
- **Drag & Drop**: Move or copy slots seamlessly within a palette or across pages, books, and characters with a translucent slot preview following the cursor.
  - **Drag**: Swap slots
  - **`Ctrl` + Drag**: Overwrite copy
- **Palette Copy / Book Copy**: Use the directional vector arrow buttons in the center panel to copy an entire palette (20 slots) or an entire book (10 pages / 200 slots, with an option to copy the book title).
- **Bulk Clear (Palette / Book)**: Use the **Clear ▾** button at the top-right of each pane to clear all 20 slots in the current palette or all 200 slots in the current book (with an option to also clear the book title).
- **Direct Book Renaming**: Click the **`✎`** button next to the book selector to rename or reset book titles directly (up to 15 bytes, with Undo/Redo support).
- **Custom Character Display Names**: Click the **`✎`** button next to the character selector to assign custom display names to alphanumeric character IDs. Names are stored in `character_names.json` within the app folder without modifying any FFXI files.
- **Context Menu & Shortcuts**: Edit, copy, paste, or clear slots via right-click, or press `Delete` to clear the selected slot immediately.
- **Full Undo / Redo**: Every edit, swap, copy, clear, book rename, and bulk operation can be undone/redone with `Ctrl+Z` / `Ctrl+Y`.

#### 2. Macro Editor & Auto-Translate / Item Picker (EN & JP)
- **Dedicated Macro Editor**: Double-click a slot, click **Edit...** above the preview, right-click → **Edit...**, or press `F2`.
- **Responsive Scaling**: Both the main window and the editor scale smoothly when resized or maximized (editor window size is remembered during the session).
- **Multi-Line Paste**: Paste multi-line macro text from the clipboard across consecutive lines automatically.
- **Real-Time Byte Counter**: Measures title (max 8 bytes) and all 6 macro lines (max 60 bytes each) in real time using Shift_JIS encoding and 6-byte Auto-Translate/Item calculation, flagging overflows or unsupported characters immediately.
- **Auto-Translate & Item Picker**: Loads standard Auto-Translate phrases, job abilities/weapon skills, spells, areas, jobs, and item names directly from your local FFXI `ROM` DAT files.
  - Supports category filtering and case/width/kana-insensitive keyword search.
  - Ambiguous entries with identical names are automatically formatted as `【Name#Code】` to prevent code collisions on save.

#### 3. Instant Bilingual UI & Dictionary Switching (English / Japanese)
- Click the **`日本語`** / **`EN`** button in the toolbar to switch both the UI language and the Auto-Translate/Item dictionary instantaneously without restarting (saved to `language.txt`; auto-detects OS language on first launch).
- Automatically detects FFXI installation paths for **JP, US, and EU** client regions.

#### 4. Safety & Data Protection
- **FFXI Running Lock**: Polls for `pol.exe` every 2 seconds and automatically locks the Save button while FFXI is running to prevent overwrite conflicts.
- **Automatic Backups**: Every save copies the previous files to `Backups\<timestamp>\` inside the app folder before writing.
- **Binary Integrity**: Preserves FFXI macro headers, MD5 checksums, and binary Auto-Translate sequences accurately.

---

### Requirements

- **OS**: Windows 10 / 11 (64-bit)
- **Game Client**: FINAL FANTASY XI (Windows JP / US / EU)
- **Runtime**: .NET 10 Desktop Runtime (x64)
  - Not required for the `self-contained` release.
  - Required only for the `framework-dependent` build (download ".NET Desktop Runtime 10.0 (x64)" from Microsoft).

---

### Setup & Usage

#### Getting Started
1. Create a dedicated folder (e.g., `D:\Tools\FF11MacroOrganizer\`) and place `FF11MacroOrganizer.exe` inside.
   - Placing it in its own folder is recommended since backups (`Backups\`) and settings (`last_folder.txt`, `pane_mode.txt`, `language.txt`, `character_names.json`) are stored alongside the executable.
2. Double-click `FF11MacroOrganizer.exe` to launch.
   - **Administrator Privileges (UAC)**: Required to save macro files under the default `C:\Program Files (x86)\PlayOnline\...` directory.
3. On startup, the app automatically loads your last opened folder (or the detected default FFXI `USER` folder) and the Auto-Translate dictionary. To open a different folder, click **Open Folder...**.

#### If Windows SmartScreen Shows "Windows protected your PC"

Because this is a free, independently developed tool without a commercial code-signing certificate, Windows SmartScreen may display a warning on first launch.

To run the application after verifying the file:
1. Click **More info** on the warning dialog.
2. Click **Run anyway** at the bottom.
3. If prompted by User Account Control (UAC), verify the filename and click **Yes**.

This is only required on the first launch.

#### Character Display Names (2 Options)

To easily distinguish multiple characters/accounts, display names are resolved in the following order (**1. App Setting > 2. `.txt` File > 3. Character ID**):

1. **In-App Setting (Recommended)**: Click the **`✎`** button next to the character dropdown and enter a display name. Saved per character ID in `character_names.json` without touching FFXI files.
2. **Legacy `.txt` Auto-Detection**: Placing an empty `<CharacterName>.txt` file inside a character folder (e.g., `USER\1a2b3c\YourName.txt`) displays `YourName (1a2b3c)`.

#### Controls

**Main Window**
| Action | Behavior |
| :--- | :--- |
| Click | Select slot (displays contents in the pane's preview area) |
| Mouse Hover | Temporarily preview hovered slot (reverts to selected slot on leave) |
| Double-click / `F2` / **Edit...** | Open Macro Editor for the selected slot |
| `Delete` | Immediately clear the selected slot (`Ctrl+Z` to undo) |
| Drag & Drop | Swap two slots |
| `Ctrl` + Drag & Drop | Overwrite copy slot |
| Right-click | Context menu (Edit / Copy / Paste / Clear) |
| `✎` next to Char / Book | Set character display name / Rename book |
| **Clear ▾** button | Bulk clear current palette (20 slots) or book (200 slots) |
| `▲` / `▼` buttons | Switch to previous / next page (palette) |
| **`日本語`** / **`EN`** button | Switch UI language & Auto-Translate dictionary instantly |
| `Ctrl+Z` / `Ctrl+Y` | Undo / Redo |
| `Ctrl+S` | Save changes (when unsaved changes exist and FFXI is closed) |

**Macro Editor**
| Action | Behavior |
| :--- | :--- |
| `Enter` (Title / Lines 1–5) | Move focus to the next line |
| `Ctrl+Space` | Jump to the Auto-Translate & Item search box |
| Double-click / `Enter` on result | Insert selected entry at cursor position |
| Resize / Maximize window | Scale editor text, inputs, and search list proportionally |
| `Ctrl+Enter` | Apply changes and close editor |
| `Esc` | Cancel and close |

---

### Auto-Translate Notation in the Editor

- **Standard Auto-Translate / Items**: Displayed in lenticular brackets such as `【Aggressor】` or `【Berserk】`.
- **Ambiguous Names**: Entries sharing the same name across categories are shown with an 8-digit hex code, e.g., `【Area#02020811】`. Keep the `#Code` suffix intact to preserve the exact original code when saving.
- **Unresolved Codes**: Preserved as `【AT:XXXXXXXX】` in English mode (or `【定型文:XXXXXXXX】` in Japanese mode).

---

### Saving & Backups

- No macro files are modified until you click **Save Changes** (only character display names are saved immediately to the app's own `character_names.json`).
- Only modified files (`mcr*.dat` / `mcr.ttl`) are written, and previous versions are backed up to `Backups\`.
- After launching FFXI, remember to upload your macros to the server from the character select screen (**Save to Server**) if needed.
- To restore a backup, exit FFXI and copy the files from `Backups\` back into the corresponding character folder.

---

### Changelog

- **v2.2.0**
  - Added full English / Japanese UI localization with instant switching via the `EN` / `日本語` toolbar button (persisted in `language.txt`)
  - Added English Auto-Translate & Item DAT parsing and automatic installation path detection for JP / US / EU FFXI clients
  - Added direct book renaming (`✎` button next to book selector, 15-byte validation, Undo/Redo support)
  - Added in-app character display name settings (`✎` button next to character selector, persisted by character ID in `character_names.json`)
  - Added bulk clear for current palette (20 slots) and current book (10 pages / 200 slots, optional title clear, Undo/Redo support)
  - Redesigned center palette/book copy buttons with color-coded directional vector arrows
  - Unified confirmation dialogs across all copy and bulk-clear actions
  - Improved toolbar spacing in Single Pane mode and header wrapping behavior
- **v2.1.1**
  - Added multi-line clipboard paste support in the Macro Editor
  - Separated line numbers from macro body text in the preview area for clean copy-pasting
- **v2.1.0**
  - Added `[1 Pane | 2 Panes]` mode switcher with automatic state persistence
  - Split preview area into independent left/right panels with active indicator and Edit button
  - Added dynamic scaling on resize/maximize for the Macro Editor
  - Added translucent drag-and-drop slot preview adorner
  - Added `Delete` key shortcut to clear the selected slot (Undo supported)
  - Changed page navigation arrows to vertical (`▲` / `▼`) and updated footer hints
  - Removed lock override for safety and added confirmation dialog to "Clear All" in editor
  - Added reminder about in-game server macro upload on save completion
- **v2.0.0**
  - Added Macro Editor window with real-time 6-byte Auto-Translate counter
  - Added Japanese item DAT loading and Auto-Translate & Item search picker
  - Added palette-level copy feature
  - Restored selected slot preview when mouse hover leaves a slot
- **v1.0.0**
  - Initial release (Dual-pane D&D organization, book copy, Undo/Redo, Auto-Translate decoding, `pol.exe` lock detection, automatic backups)

---

### Notes & Disclaimer

- This is an unofficial, fan-made tool and is not affiliated with or endorsed by SQUARE ENIX CO., LTD.
- This tool operates offline on local macro files (`mcr*.dat` / `mcr.ttl`) and reads client DAT files strictly for dictionary lookup. It does not modify game memory or intercept network traffic; however, **the possibility that unofficial tools may conflict with the game's Terms of Service cannot be completely ruled out.**
- **The author assumes no responsibility for any account suspension/ban, data loss, corruption, or any direct or indirect damages resulting from the use or inability to use this tool.** Use entirely at your own risk after reviewing the game's Terms of Service.
- Modifying macro files while logged into FFXI will not take effect immediately and may be overwritten upon logout. Always **exit FFXI (PlayOnline) before saving**.
- Game updates may change file formats without notice. Creating a manual backup of your entire `USER` folder before your first use or major reorganization is strongly recommended.

---

### Bug Reports, Requests & Feedback

- **Bug Reports & Feature Requests (GitHub account)**
  Please submit an issue on [GitHub Issues](https://github.com/thunder4th/FF11MacroOrganizer/issues).
- **Bug Reports & Feature Requests (Anonymous / No account required)**
  Submit via the [Feedback Form (Google Forms)](https://forms.gle/YdbgECE2FRJJP4on8).
- **Comments & Messages**
  Feel free to drop a note on [Marshmallow](https://marshmallow-qa.com/tomqjx7vldwzhn1) (please use Issues or the feedback form for bug reports/requests).

---

### Support Development

This tool is free and open-source. If it helps with your daily FFXI macro management, voluntary support is always appreciated:

- [Support on Ko-fi](https://ko-fi.com/thunder4th)
- [Support on OFUSE](https://ofuse.me/dc1806a8)

---

### License

Released under the **MIT License**. See [`LICENSE`](./LICENSE) for details.

Copyright (c) 2026 thunder4th

- All copyrights to game data referenced or displayed by this tool (Auto-Translate phrases, item names, etc.) belong to SQUARE ENIX CO., LTD. No game data is bundled with this tool.