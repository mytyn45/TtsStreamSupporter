using System;
using System.IO;

namespace TtsStreamSupporter.Services;

public sealed class PreparedAudio : IDisposable
{
    public MemoryStream Wav { get; }

    public PreparedAudio(MemoryStream wav)
    {
        Wav = wav;
    }

    public void Dispose()
    {
        Wav.Dispose();
    }
}