using System.IO;
using System.Text.Json;

namespace FehDialogExtractor;

public sealed class AzureVisionSettings
{
    public required string Endpoint { get; set; }

    public required string ApiKey { get; set; }

    // Load Azure Vision settings from JSON file located in the application base directory.
    public static AzureVisionSettings LoadAzureVisionSettings(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Azure Vision configuration file not found: {path}");

        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var settings = JsonSerializer.Deserialize<AzureVisionSettings>(json, opts);
        if (settings == null || string.IsNullOrEmpty(settings.Endpoint) || string.IsNullOrEmpty(settings.ApiKey))
            throw new InvalidOperationException($"Azure Vision configuration is invalid: {path}");

        return settings;
    }
};
