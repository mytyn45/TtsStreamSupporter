using System;
using NAudio.Wave;

namespace TtsStreamSupporter.Services;

public static class ApplyAudioEffects
{
    /// <summary>
    /// Применяет динамическую компрессию: делает тихие места громче,
    /// а громкие — тише, выравнивая общую громкость.
    /// </summary>
    /// <param name="chain">Входная цепочка сэмплов.</param>
    /// <param name="strength">Сила эффекта от 0 (выкл.) до 10 (максимум).</param>
    public static ISampleProvider ApplyNormalizationIfEnabled(
        ISampleProvider chain,
        float strength)
    {
        strength = Math.Clamp(strength, 0f, 10f);

        if (strength <= 0f)
            return chain;

        // strength 0..10 → threshold от -6дБ (мягко) до -36дБ (жёстко)
        float thresholdDb = -6f - (strength / 10f) * 30f;

        // strength 0..10 → ratio от 1.5:1 до 10:1
        float ratio = 1.5f + (strength / 10f) * 8.5f;

        // компенсируем часть "потерянной" громкости, чтобы тихие
        // сообщения не тонули после компрессии
        float makeupDb = (strength / 10f) * 6f;

        return new CompressorSampleProvider(chain)
        {
            ThresholdDb = thresholdDb,
            Ratio = ratio,
            AttackMs = 8f,
            ReleaseMs = 150f,
            MakeupGainDb = makeupDb
        };
    }

    /// <summary>
    /// Применяет умножение громкости (усиление готового сообщения) в реальном времени
    /// с мягким лимитером, предотвращающим цифровой клиппинг.
    /// </summary>
    public static ISampleProvider ApplyVolume(
        ISampleProvider chain,
        Func<float> volumeGetter)
    {
        return new VolumeSampleProvider(chain, volumeGetter);
    }
}

/// <summary>
/// Усилитель громкости готового аудио с мягким лимитером (soft limiter)
/// для предотвращения жесткого цифрового клиппинга при сильном усилении.
/// </summary>
public class VolumeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<float> _volumeGetter;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public VolumeSampleProvider(ISampleProvider source, Func<float> volumeGetter)
    {
        _source = source;
        _volumeGetter = volumeGetter;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        float volume = _volumeGetter();

        if (Math.Abs(volume - 1f) < 1e-4f)
        {
            return samplesRead;
        }

        for (int i = 0; i < samplesRead; i++)
        {
            buffer[offset + i] = SoftLimit(buffer[offset + i] * volume);
        }

        return samplesRead;
    }

    private static float SoftLimit(float x)
    {
        if (x > 0.8f)
        {
            return 0.8f + 0.2f * (float)Math.Tanh((x - 0.8f) / 0.2f);
        }
        if (x < -0.8f)
        {
            return -0.8f + 0.2f * (float)Math.Tanh((x + 0.8f) / 0.2f);
        }
        return x;
    }
}
/// <summary>
/// Простой feed-forward компрессор с envelope follower (attack/release).
/// Уменьшает динамический диапазон: тихие места остаются почти без
/// изменений, громкие — прижимаются к порогу.
/// </summary>
public class CompressorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _envelope;

    /// <summary>Порог срабатывания компрессии, дБ (например, -24).</summary>
    public float ThresholdDb { get; set; } = -24f;

    /// <summary>Коэффициент сжатия, N:1 (например, 4 = 4:1).</summary>
    public float Ratio { get; set; } = 4f;

    /// <summary>Скорость реакции на нарастание громкости, мс.</summary>
    public float AttackMs { get; set; } = 8f;

    /// <summary>Скорость реакции на спад громкости, мс.</summary>
    public float ReleaseMs { get; set; } = 120f;

    /// <summary>Компенсирующее усиление после компрессии, дБ.</summary>
    public float MakeupGainDb { get; set; } = 0f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public CompressorSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        float attackCoef = TimeToCoef(AttackMs);
        float releaseCoef = TimeToCoef(ReleaseMs);
        float thresholdLinear = DbToLinear(ThresholdDb);
        float makeupLinear = DbToLinear(MakeupGainDb);

        for (int i = 0; i < samplesRead; i++)
        {
            float sample = buffer[offset + i];
            float inputAbs = Math.Abs(sample);

            // envelope follower
            _envelope = inputAbs > _envelope
                ? attackCoef * _envelope + (1 - attackCoef) * inputAbs
                : releaseCoef * _envelope + (1 - releaseCoef) * inputAbs;

            float gain = 1f;

            if (_envelope > thresholdLinear && _envelope > 1e-6f)
            {
                float envelopeDb = LinearToDb(_envelope);
                float overDb = envelopeDb - ThresholdDb;
                float compressedOverDb = overDb / Ratio;
                float gainDb = compressedOverDb - overDb; // отрицательное значение
                gain = DbToLinear(gainDb);
            }

            buffer[offset + i] = sample * gain * makeupLinear;
        }

        return samplesRead;
    }

    private float TimeToCoef(float ms) =>
        (float)Math.Exp(-1.0 / (WaveFormat.SampleRate * (ms / 1000.0)));

    private static float DbToLinear(float db) =>
        (float)Math.Pow(10, db / 20.0);

    private static float LinearToDb(float linear) =>
        20f * (float)Math.Log10(Math.Max(linear, 1e-6f));
}