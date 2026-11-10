using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ducky.Discord;

internal static class DiscordCommandAccess
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string FilePath =>
        Path.Combine(DuckyPaths.DataDirectory, "discord-command-users.json");

    public static HashSet<string> LoadAllowedUserIds()
    {
        if (!File.Exists(FilePath))
            return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            string[]? ids = JsonSerializer.Deserialize<string[]>(
                File.ReadAllText(FilePath),
                JsonOptions);
            return Normalize(ids ?? []);
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not load Discord command allowlist: " + ex);
            throw new InvalidDataException(
                "Could not read the Discord user allowlist. Check the log for details.",
                ex);
        }
    }

    public static void SaveAllowedUserIds(IEnumerable<string> userIds)
    {
        HashSet<string> normalized = Normalize(userIds);
        Directory.CreateDirectory(DuckyPaths.DataDirectory);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(normalized.OrderBy(id => id), JsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }

    public static bool IsAllowed(string userId) =>
        LoadAllowedUserIds().Contains(userId);

    private static HashSet<string> Normalize(IEnumerable<string> userIds)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (string userId in userIds)
        {
            string id = userId.Trim();
            if (id.Length == 0)
                continue;

            if (!ulong.TryParse(id, out ulong numericId) || numericId == 0)
            {
                throw new InvalidDataException(
                    $"'{id}' is not a valid Discord user ID.");
            }

            result.Add(numericId.ToString());
        }

        return result;
    }
}
