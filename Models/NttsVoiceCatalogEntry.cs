using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TtsStreamSupporter.Models;

public class NttsVoicesFile
{
    [JsonPropertyName("voices")]
    public List<NttsVoiceCatalogEntry> Voices { get; set; } = new();

    [JsonPropertyName("custom_voices")]
    public List<NttsVoiceCatalogEntry> CustomVoices { get; set; } = new();
}

public class NttsVoiceCatalogEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("speakers")]
    public List<string> Speakers { get; set; } = new();
}
