using System.Diagnostics;
using System.IO;

namespace FF11MacroOrganizer.Core;

public sealed record SaveResult(bool Success, int SavedFileCount, string? BackupPath, string Message);

public sealed class MacroRepository
{
    public List<CharacterMacroStore> Characters { get; } = new();
    public string? RootPath { get; private set; }

    public static bool IsPolRunning()
    {
        return Process.GetProcessesByName("pol").Length > 0;
    }

    public bool HasUnsavedChanges => Characters.Any(c => c.HasDirtyData);

    public int CountDirtyFiles()
    {
        int count = 0;
        foreach (var ch in Characters)
        {
            if (ch.TitleFile1.IsDirty) count++;
            if (ch.TitleFile2.IsDirty) count++;
            count += ch.LoadedPages.Count(p => p.IsDirty);
        }
        return count;
    }

    /// <summary>
    /// USER親フォルダ（複数キャラ）または単体キャラフォルダのどちらを選んでも自動で読み込む
    /// </summary>
    public void LoadFolder(string selectedPath)
    {
        if (!Directory.Exists(selectedPath))
            throw new DirectoryNotFoundException(Loc.Instance.Format("ErrFolderNotFound", selectedPath));

        Characters.Clear();
        RootPath = selectedPath;

        // FINAL FANTASY XI 直下を選んだ場合は USER フォルダへ自動補正
        string searchBase = selectedPath;
        if (!ContainsMacroFiles(searchBase))
        {
            string userSub = Path.Combine(searchBase, "USER");
            if (Directory.Exists(userSub))
            {
                searchBase = userSub;
            }
        }

        if (ContainsMacroFiles(searchBase))
        {
            // 単体キャラフォルダ（テストフォルダ等）
            string id = new DirectoryInfo(searchBase).Name;
            Characters.Add(new CharacterMacroStore(id, searchBase));
            return;
        }

        // サブフォルダからキャラフォルダを探索
        foreach (var dir in Directory.GetDirectories(searchBase).OrderBy(d => d))
        {
            if (ContainsMacroFiles(dir))
            {
                string id = new DirectoryInfo(dir).Name;
                Characters.Add(new CharacterMacroStore(id, dir));
            }
        }
    }

    private static bool ContainsMacroFiles(string dir)
    {
        return File.Exists(Path.Combine(dir, "mcr.ttl")) ||
               File.Exists(Path.Combine(dir, "mcr.dat")) ||
               Directory.EnumerateFiles(dir, "mcr*.dat").Any();
    }

    public SaveResult SaveAll(string backupBaseDir, bool ignorePolLock)
    {
        if (!ignorePolLock && IsPolRunning())
        {
            return new SaveResult(false, 0, null, Loc.Instance["RepoPolRunning"]);
        }

        if (!HasUnsavedChanges)
        {
            return new SaveResult(true, 0, null, Loc.Instance["RepoNoChanges"]);
        }

        // 1. 保存前に全対象ファイルの外部更新（タイムスタンプ変化）を検証
        var externalModified = new List<string>();
        foreach (var ch in Characters)
        {
            CheckExternalChange(ch.TitleFile1.FilePath, ch.TitleFile1.IsDirty, ch.TitleFile1.ExistsOnDisk, ch.TitleFile1.LoadedLastWriteTimeUtc, externalModified);
            CheckExternalChange(ch.TitleFile2.FilePath, ch.TitleFile2.IsDirty, ch.TitleFile2.ExistsOnDisk, ch.TitleFile2.LoadedLastWriteTimeUtc, externalModified);
            foreach (var page in ch.LoadedPages)
            {
                CheckExternalChange(page.FilePath, page.IsDirty, page.ExistsOnDisk, page.LoadedLastWriteTimeUtc, externalModified);
            }
        }

        if (externalModified.Count > 0)
        {
            string list = string.Join("\n", externalModified.Take(5));
            return new SaveResult(false, 0, null, Loc.Instance.Format("RepoExternalModified", list));
        }

        // 2. バックアップ作成＆一時ファイル経由での安全書き込み
        string backupSessionDir = Path.Combine(backupBaseDir, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        int savedCount = 0;

        foreach (var ch in Characters)
        {
            string charBackupDir = Path.Combine(backupSessionDir, ch.CharacterId);

            if (ch.TitleFile1.IsDirty)
            {
                WriteSafely(ch.TitleFile1.FilePath, MacroBinaryCodec.SerializeTitleFile(ch.TitleFile1), charBackupDir);
                ch.TitleFile1.ExistsOnDisk = true;
                ch.TitleFile1.LoadedLastWriteTimeUtc = File.GetLastWriteTimeUtc(ch.TitleFile1.FilePath);
                ch.TitleFile1.IsDirty = false;
                savedCount++;
            }

            if (ch.TitleFile2.IsDirty)
            {
                WriteSafely(ch.TitleFile2.FilePath, MacroBinaryCodec.SerializeTitleFile(ch.TitleFile2), charBackupDir);
                ch.TitleFile2.ExistsOnDisk = true;
                ch.TitleFile2.LoadedLastWriteTimeUtc = File.GetLastWriteTimeUtc(ch.TitleFile2.FilePath);
                ch.TitleFile2.IsDirty = false;
                savedCount++;
            }

            foreach (var page in ch.LoadedPages.Where(p => p.IsDirty))
            {
                WriteSafely(page.FilePath, MacroBinaryCodec.SerializePage(page), charBackupDir);
                page.ExistsOnDisk = true;
                page.LoadedLastWriteTimeUtc = File.GetLastWriteTimeUtc(page.FilePath);
                page.IsDirty = false;
                savedCount++;
            }
        }

        return new SaveResult(true, savedCount, backupSessionDir, Loc.Instance.Format("RepoSavedCount", savedCount));
    }

    private static void CheckExternalChange(string path, bool isDirty, bool existedOnLoad, DateTime? loadedTimeUtc, List<string> errors)
    {
        if (!isDirty) return;
        bool existsNow = File.Exists(path);
        if (existedOnLoad != existsNow)
        {
            errors.Add(path);
            return;
        }
        if (existsNow && loadedTimeUtc.HasValue)
        {
            if (File.GetLastWriteTimeUtc(path) != loadedTimeUtc.Value)
            {
                errors.Add(path);
            }
        }
    }

    private static void WriteSafely(string targetPath, byte[] data, string backupDir)
    {
        if (File.Exists(targetPath))
        {
            Directory.CreateDirectory(backupDir);
            string bakPath = Path.Combine(backupDir, Path.GetFileName(targetPath));
            File.Copy(targetPath, bakPath, overwrite: true);
        }

        string tempPath = targetPath + ".tmp";
        File.WriteAllBytes(tempPath, data);
        File.Move(tempPath, targetPath, overwrite: true);
    }
}