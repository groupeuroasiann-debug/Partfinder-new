using System.Text.Json;

namespace PartFinder.Helpers;

/// <summary>Reads appsettings.json and optional local overlays next to the running executable.</summary>
public static class AppConfiguration
{
    private const string DefaultLicenseApiBaseUrl = "https://backend.partfinder.store";

    public static string GetLicenseApiBaseUrl()
    {
        var fromFile = ReadLicenseApiBaseUrl();
        return string.IsNullOrWhiteSpace(fromFile) ? DefaultLicenseApiBaseUrl : fromFile.TrimEnd('/');
    }

    private static string? ReadLicenseApiBaseUrl()
    {
        using var root = LoadMergedRoot();
        if (root is null)
        {
            return null;
        }

        if (!root.RootElement.TryGetProperty("LicenseApi", out var licenseApi) ||
            !licenseApi.TryGetProperty("BaseUrl", out var baseUrlEl))
        {
            return null;
        }

        return baseUrlEl.GetString();
    }

    private static JsonDocument? LoadMergedRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var files = new[]
        {
            Path.Combine(baseDir, "appsettings.json"),
            Path.Combine(baseDir, "appsettings.Development.json"),
            Path.Combine(baseDir, "appsettings.local.json"),
        };

        JsonElement? merged = null;
        foreach (var path in files)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                merged = merged is null
                    ? doc.RootElement.Clone()
                    : MergeJson(merged.Value, doc.RootElement);
            }
            catch
            {
                // Skip invalid overlay files; base appsettings still applies.
            }
        }

        if (merged is null)
        {
            return null;
        }

        return JsonDocument.Parse(merged.Value.GetRawText());
    }

    private static JsonElement MergeJson(JsonElement baseEl, JsonElement overlayEl)
    {
        if (baseEl.ValueKind != JsonValueKind.Object || overlayEl.ValueKind != JsonValueKind.Object)
        {
            return overlayEl.Clone();
        }

        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in baseEl.EnumerateObject())
        {
            dict[prop.Name] = prop.Value;
        }

        foreach (var prop in overlayEl.EnumerateObject())
        {
            if (dict.TryGetValue(prop.Name, out var existing) &&
                existing.ValueKind == JsonValueKind.Object &&
                prop.Value.ValueKind == JsonValueKind.Object)
            {
                dict[prop.Name] = MergeJson(existing, prop.Value);
            }
            else
            {
                dict[prop.Name] = prop.Value.Clone();
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var pair in dict)
            {
                writer.WritePropertyName(pair.Key);
                pair.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }
}