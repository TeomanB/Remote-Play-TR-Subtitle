using System.Text;
using System.Text.Json;

namespace AltyaziCevirici.Core;

/// <summary>Parses the response of translate.googleapis.com/translate_a/single?client=gtx&amp;dt=t.</summary>
public static class GoogleTranslateParser
{
    /// <summary>Returns the concatenated translated text or null if the payload is not understood.</summary>
    public static string? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
            var segments = root[0];
            if (segments.ValueKind != JsonValueKind.Array) return null;
            var sb = new StringBuilder();
            foreach (var seg in segments.EnumerateArray())
            {
                if (seg.ValueKind != JsonValueKind.Array || seg.GetArrayLength() == 0) continue;
                var t = seg[0];
                if (t.ValueKind == JsonValueKind.String) sb.Append(t.GetString());
            }
            var result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public const string MainClient = "gtx";
    /// <summary>Same endpoint and model, different client id; used only while "gtx" is rate limited.</summary>
    public const string BackupClient = "dict-chrome-ex";

    public static string BuildUrl(string text, string source = "en", string target = "tr", string client = MainClient) =>
        $"https://translate.googleapis.com/translate_a/single?client={client}&sl={Uri.EscapeDataString(source)}&tl={Uri.EscapeDataString(target)}&dt=t&q={Uri.EscapeDataString(text)}";
}
