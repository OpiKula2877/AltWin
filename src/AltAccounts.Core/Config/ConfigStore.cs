using System.Text.Json;

namespace AltAccounts.Core.Config;

public static class AppPaths
{
    public static string ProgramDataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AltAccounts");

    public static string LocalDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AltAccounts");

    public static string SharedConfigPath => Path.Combine(ProgramDataDir, "config.json");
    public static string AuditPath => Path.Combine(ProgramDataDir, "audit.log");
    public static string UserConfigPath => Path.Combine(LocalDir, "user.json");
    public static string LocalAuditPath => Path.Combine(LocalDir, "audit.log");
    public static string RequestsDir => Path.Combine(LocalDir, "requests");
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Returns a new default object when the file is missing or unreadable.</summary>
    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T();
        }
        catch (Exception) { return new T(); }
    }

    /// <summary>Atomic write: temp file in the same folder, then replace.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public static SharedConfig LoadShared() => FixComparer(Load<SharedConfig>(AppPaths.SharedConfigPath));
    public static UserConfig LoadUser() => Load<UserConfig>(AppPaths.UserConfigPath);
    public static void SaveUser(UserConfig c) => Save(AppPaths.UserConfigPath, c);

    /// <summary>Deserialization creates a case-sensitive dictionary; SIDs compare case-insensitively.</summary>
    public static SharedConfig FixComparer(SharedConfig c)
    {
        c.Policies = new Dictionary<string, AccountPolicy>(c.Policies ?? [], StringComparer.OrdinalIgnoreCase);
        c.Shares ??= [];
        return c;
    }

    public static ExportBundle FixComparerBundle(ExportBundle b)
    {
        b.Shared = FixComparer(b.Shared ?? new SharedConfig());
        return b;
    }
}
