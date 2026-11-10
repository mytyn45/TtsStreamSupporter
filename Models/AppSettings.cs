using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TtsStreamSupporter.Models;

public class AppSettings
{
    public string? TwitchNick { get; set; } = "";
    public string GoodgameNick { get; set; } = "";
    public string? KickNick { get; set; } = "";
    public string? BluestarApi { get; set; } = "";

    public bool CheckboxConnectToTwitch { get; set; }
    public bool CheckboxConnectToGoodgame { get; set; }
    public bool CheckboxConnectToKick { get; set; }
    public bool CheckboxConnectToBluestar { get; set; }

    public string? SelectedSingleVoice { get; set; }
    public int StandardVoiceVolume { get; set; } = 100;
    public int StandardVoiceSpeed { get; set; } = 0;

    public bool IndividualVoicesEnabled { get; set; }
    public List<IndividualVoiceSettings> IndividualVoices { get; set; } = new();

    public bool CheckBoxNormalizationTts { get; set; }
    public int CompressorPower { get; set; } = 0;
    public float MasterVolumeMultiplier { get; set; } = 1.0f;

    public bool CheckBoxAllowSkipMessages { get; set; }
    public string? SkipMessage { get; set; } = "!c";
    public string? SkipAllMessages { get; set; } = "!cв";
    public string? DoNotTtsIfStartWith { get; set; } = "!";

    public HashSet<string> ListBoxItemSourceBlackList { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> WordReplacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CharWordReplacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool CheckBoxNttsVoicesIsEnabled { get; set; }
    public string? NttsApi { get; set; } = "";
    public string NttsVoiceChangeCommand { get; set; } = "";
    public bool NttsUseFallbackServer { get; set; }

    public bool TtsDelayEnabled { get; set; }
    public int TtsDelayTime { get; set; } = 0;

    public bool RemoveEmoji { get; set; }
}
