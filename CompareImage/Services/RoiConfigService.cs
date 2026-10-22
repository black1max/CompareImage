using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using CompareImage.Models;

namespace CompareImage.Services;

public class RoiConfigService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void SaveToFile(RoiConfig config, string filePath)
    {
        string json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static RoiConfig LoadFromFile(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var config = JsonSerializer.Deserialize<RoiConfig>(json, _jsonOptions);
        return config ?? new RoiConfig();
    }
}
