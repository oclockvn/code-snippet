using System.IO;
using System.Text.Json;
using CodeSnippet.Models;
using PromptManager.Models;

namespace CodeSnippet.Services;

public sealed class AppSettingsStore
{
    private readonly string _filePath;

    public AppSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptManager", "settings.json"))
    {
    }

    public AppSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize(json, PromptJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // Corrupt settings file: fall back to defaults below.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, PromptJsonContext.Default.AppSettings);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception)
        {
            // Best-effort persistence; a failed save just means the rebind doesn't survive next launch.
        }
    }
}
