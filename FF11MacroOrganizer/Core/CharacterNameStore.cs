using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FF11MacroOrganizer.Core;

/// <summary>
/// キャラIDごとのアプリ内表示名を JSON で永続化するストア。
/// </summary>
public sealed class CharacterNameStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath;
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public CharacterNameStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "character_names.json");
        Load();
    }

    public void Load()
    {
        _names.Clear();
        if (!File.Exists(_filePath)) return;

        try
        {
            string json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded == null) return;

            foreach (var kv in loaded)
            {
                string key = kv.Key.Trim();
                string val = kv.Value?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                {
                    _names[key] = val;
                }
            }
        }
        catch
        {
            // 設定ファイル破損時は空のまま起動を継続
        }
    }

    public string? GetName(string characterId)
    {
        return _names.TryGetValue(characterId, out var name) ? name : null;
    }

    public void SetName(string characterId, string? customName)
    {
        string normalized = customName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(normalized))
        {
            _names.Remove(characterId);
        }
        else
        {
            _names[characterId] = normalized;
        }

        Save();
    }

    public void ApplyTo(IEnumerable<CharacterMacroStore> characters)
    {
        foreach (var ch in characters)
        {
            ch.SetCustomCharacterName(GetName(ch.CharacterId));
        }
    }

    private void Save()
    {
        var sorted = new SortedDictionary<string, string>(_names, StringComparer.OrdinalIgnoreCase);
        string json = JsonSerializer.Serialize(sorted, JsonOptions);
        string tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}