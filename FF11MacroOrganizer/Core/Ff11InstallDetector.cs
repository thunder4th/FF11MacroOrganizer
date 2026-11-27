using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace FF11MacroOrganizer.Core;

public enum Ff11Region
{
    Jp,
    Us,
    Eu,
    Unknown
}

public sealed record Ff11InstallInfo(
    Ff11Region Region,
    string InstallDir,
    string UserDir,
    string RomDir
)
{
    public bool HasUserDir => Directory.Exists(UserDir);
    public bool HasRomDir => IsValidRomDir(RomDir);

    internal static bool IsValidRomDir(string path)
        => Directory.Exists(path) && Directory.Exists(Path.Combine(path, "0"));
}

/// <summary>
/// JP / US / EU 各リージョンのレジストリおよび既定パスから FF11 インストール先を検出するユーティリティ。
/// </summary>
public static class Ff11InstallDetector
{
    private const string Ff11ValueName = "0001";

    private static readonly (Ff11Region Region, string SubKey)[] RegistryTargetsJpFirst =
    [
        (Ff11Region.Jp, @"SOFTWARE\PlayOnline\InstallFolder"),
        (Ff11Region.Us, @"SOFTWARE\PlayOnlineUS\InstallFolder"),
        (Ff11Region.Eu, @"SOFTWARE\PlayOnlineEU\InstallFolder"),
    ];

    private static readonly (Ff11Region Region, string SubKey)[] RegistryTargetsEnFirst =
    [
        (Ff11Region.Us, @"SOFTWARE\PlayOnlineUS\InstallFolder"),
        (Ff11Region.Eu, @"SOFTWARE\PlayOnlineEU\InstallFolder"),
        (Ff11Region.Jp, @"SOFTWARE\PlayOnline\InstallFolder"),
    ];

    /// <summary>
    /// レジストリおよび既定フォールバック先から、実在する FF11 インストール先を優先順で列挙する。
    /// </summary>
    public static IReadOnlyList<Ff11InstallInfo> DetectAll()
    {
        var results = new List<Ff11InstallInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool isJapaneseOs = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "ja",
            StringComparison.OrdinalIgnoreCase);

        var targets = isJapaneseOs ? RegistryTargetsJpFirst : RegistryTargetsEnFirst;

        // 1. 32bit レジストリビュー (WOW6432Node 透過参照) から検出
        foreach (var (region, subKey) in targets)
        {
            string? installDir = TryReadInstallFolder(subKey);
            TryAddInstallInfo(results, seen, region, installDir);
        }

        // 2. レジストリ欠損・移行環境向けの既定パスフォールバック
        foreach (string fallbackDir in GetFallbackInstallDirs())
        {
            TryAddInstallInfo(results, seen, Ff11Region.Unknown, fallbackDir);
        }

        return results;
    }

    /// <summary>
    /// 初回起動時に自動で開く既定の USER フォルダを返す。見つからない場合は null。
    /// </summary>
    public static string? DetectDefaultUserDir()
    {
        return DetectAll()
            .FirstOrDefault(info => info.HasUserDir)
            ?.UserDir;
    }

    /// <summary>
    /// 開いているフォルダ (USER やキャラフォルダ、コピー先フォルダ等) をヒントに、
    /// 辞書読み込み用の FF11 ルートフォルダ (ROM の親フォルダ) を解決する。
    /// </summary>
    public static string? ResolveFf11Root(string? hintFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(hintFolderPath))
        {
            string? fromHint = FindFf11RootAroundHint(hintFolderPath);
            if (fromHint is not null)
                return fromHint;
        }

        return DetectAll()
            .FirstOrDefault(info => info.HasRomDir)
            ?.InstallDir;
    }

    private static string? FindFf11RootAroundHint(string hintFolderPath)
    {
        try
        {
            var dir = new DirectoryInfo(NormalizePath(hintFolderPath));
            // 自身・親・祖父母・曽祖父母の4段階まで探索（既存の挙動を維持）
            for (int depth = 0; depth < 4 && dir is not null; depth++, dir = dir.Parent)
            {
                string romCandidate = Path.Combine(dir.FullName, "ROM");
                if (Ff11InstallInfo.IsValidRomDir(romCandidate))
                    return dir.FullName;
            }
        }
        catch
        {
            // 無効パスや権限不足はレジストリ側フォールバックへ流す
        }

        return null;
    }

    public static string? ResolveRomDir(string? hintFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(hintFolderPath))
        {
            string? fromHint = FindRomDirAroundHint(hintFolderPath);
            if (fromHint is not null)
                return fromHint;
        }

        return DetectAll()
            .FirstOrDefault(info => info.HasRomDir)
            ?.RomDir;
    }

    /// <summary>
    /// 開いているフォルダに対応するリージョンを推定する（次ステップの英語DAT判定用）。
    /// </summary>
    public static Ff11Region DetectRegionForHint(string? hintFolderPath)
    {
        var installs = DetectAll();

        if (!string.IsNullOrWhiteSpace(hintFolderPath))
        {
            string normalizedHint = NormalizePath(hintFolderPath);
            var matched = installs.FirstOrDefault(info =>
                normalizedHint.StartsWith(info.InstallDir, StringComparison.OrdinalIgnoreCase));

            if (matched is not null && matched.Region != Ff11Region.Unknown)
                return matched.Region;
        }

        return installs.FirstOrDefault(i => i.Region != Ff11Region.Unknown)?.Region
               ?? Ff11Region.Unknown;
    }

    private static string? TryReadInstallFolder(string subKeyPath)
    {
        try
        {
            using var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = hklm32.OpenSubKey(subKeyPath, writable: false);
            var raw = key?.GetValue(Ff11ValueName) as string;
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
    }

    private static void TryAddInstallInfo(
        List<Ff11InstallInfo> results,
        HashSet<string> seen,
        Ff11Region region,
        string? rawInstallDir)
    {
        if (string.IsNullOrWhiteSpace(rawInstallDir))
            return;

        string installDir = NormalizePath(rawInstallDir);
        if (!Directory.Exists(installDir))
            return;

        if (!seen.Add(installDir))
            return;

        string userDir = Path.Combine(installDir, "USER");
        string romDir = Path.Combine(installDir, "ROM");

        // アンインストール後の空フォルダ残骸を弾くため、USER か ROM のどちらかが実在するもののみ採用
        if (!Directory.Exists(userDir) && !Ff11InstallInfo.IsValidRomDir(romDir))
            return;

        results.Add(new Ff11InstallInfo(region, installDir, userDir, romDir));
    }

    private static string? FindRomDirAroundHint(string hintFolderPath)
    {
        try
        {
            string current = NormalizePath(hintFolderPath);
            if (!Directory.Exists(current))
                return null;

            // hint が「FF11直下」「USER」「USER\<キャラID>」のいずれであっても拾えるよう最大3段階上まで確認
            for (int depth = 0; depth <= 2; depth++)
            {
                string candidate = Path.Combine(current, "ROM");
                if (Ff11InstallInfo.IsValidRomDir(candidate))
                    return candidate;

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrEmpty(parent))
                    break;

                current = parent;
            }
        }
        catch
        {
            // 無効パスや権限不足はフォールバックへ流す
        }

        return null;
    }

    private static IEnumerable<string> GetFallbackInstallDirs()
    {
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrEmpty(pf86))
            yield return Path.Combine(pf86, "PlayOnline", "SquareEnix", "FINAL FANTASY XI");

        if (!string.IsNullOrEmpty(pf) && !string.Equals(pf, pf86, StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(pf, "PlayOnline", "SquareEnix", "FINAL FANTASY XI");
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}