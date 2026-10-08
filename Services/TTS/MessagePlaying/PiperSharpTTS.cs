using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using NickBuhro.Translit;
using Nut;
using PiperSharp;
using PiperSharp.Models;
using Serilog;
using TtsStreamSupporter.Models;
using TtsStreamSupporter.Settings;

namespace TtsStreamSupporter.Services;

public static class PiperSharpTTS
{
    public static string AppDataFolder => DirectoryService.AppDataFolder;
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static Task<bool> DownloadPiper(string voiceName)
    {
        return DownloadPiperWithProgressAsync(voiceName).ContinueWith(t => t.IsCompletedSuccessfully && t.Result.Status == PiperDownloadStatus.Success);
    }

    public static async Task<PiperDownloadResult> DownloadPiperWithProgressAsync(
        string voiceName,
        IProgress<PiperDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
            return new PiperDownloadResult { Status = PiperDownloadStatus.Failed, ErrorMessage = "Не указан голос для загрузки" };

        var lockTaken = false;
        string? voiceModelPath = null;
        try
        {
            await DownloadLock.WaitAsync(cancellationToken);
            lockTaken = true;

            string AppDataFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "ToltoonTts");
            var modelsPath = Path.Combine(AppDataFolder, "models");
            var normalizedVoiceName = voiceName.Trim();
            voiceModelPath = Path.Combine(modelsPath, normalizedVoiceName);
            var modelJsonPath = Path.Combine(voiceModelPath, "model.json");

            // Проверяем, существует ли уже корректно установленная модель
            if (Directory.Exists(voiceModelPath) && File.Exists(modelJsonPath))
            {
                var onnxFiles = Directory.GetFiles(voiceModelPath, "*.onnx");
                if (onnxFiles.Length > 0)
                {
                    return new PiperDownloadResult { Status = PiperDownloadStatus.AlreadyExists };
                }
            }

            Directory.CreateDirectory(modelsPath);

            // Гарантируем, что движок piper установлен или загружается
            if (!PiperVoicesData.IsEngineInstalled())
            {
                await PiperVoicesData.EnsureEngineDownloadedAsync(cancellationToken);
            }

            // Если папка существовала, но была повреждена или пуста — очищаем перед повторным скачиванием
            if (Directory.Exists(voiceModelPath))
            {
                try { Directory.Delete(voiceModelPath, true); } catch { }
            }
            Directory.CreateDirectory(voiceModelPath);

            var model = await PiperDownloader.GetModelByKey(normalizedVoiceName);
            if (model == null || model.Files == null || model.Files.Count == 0)
            {
                try { Directory.Delete(voiceModelPath, true); } catch { }
                return new PiperDownloadResult { Status = PiperDownloadStatus.Failed, ErrorMessage = "Модель не найдена в репозитории Piper" };
            }

            // Подсчитываем общий размер файлов
            long totalBytes = 0;
            foreach (var fileEntry in model.Files)
            {
                if (fileEntry.Value is JsonElement el)
                {
                    if (el.TryGetProperty("size_bytes", out var szProp) && szProp.TryGetInt64(out var sz))
                        totalBytes += sz;
                }
                else if (fileEntry.Value is System.Collections.IDictionary dict && dict.Contains("size_bytes"))
                {
                    if (Convert.ToInt64(dict["size_bytes"]) is var sz && sz > 0)
                        totalBytes += sz;
                }
            }

            progress?.Report(new PiperDownloadProgress { DownloadedBytes = 0, TotalBytes = totalBytes });

            long downloadedBytes = 0;
            foreach (var fileKey in model.Files.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(fileKey);
                var filePath = Path.Combine(voiceModelPath, fileName);
                var url = $"https://huggingface.co/rhasspy/piper-voices/resolve/main/{fileKey}?download=true";

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                if (fileName.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var ms = new MemoryStream();
                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                        {
                            await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            downloadedBytes += read;
                            progress?.Report(new PiperDownloadProgress
                            {
                                DownloadedBytes = downloadedBytes,
                                TotalBytes = totalBytes,
                                CurrentFileName = fileName
                            });
                        }
                    }

                    ms.Position = 0;
                    try
                    {
                        var deserialized = await JsonSerializer.DeserializeAsync<VoiceModel>(ms, cancellationToken: cancellationToken);
                        if (deserialized?.Audio != null)
                        {
                            model.Audio = deserialized.Audio;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Не удалось разобрать config для {VoiceName}", voiceName);
                    }

                    await File.WriteAllBytesAsync(filePath, ms.ToArray(), cancellationToken);
                }
                else
                {
                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloadedBytes += read;
                        progress?.Report(new PiperDownloadProgress
                        {
                            DownloadedBytes = downloadedBytes,
                            TotalBytes = totalBytes,
                            CurrentFileName = fileName
                        });
                    }
                }
            }

            model.ModelLocation = voiceModelPath;
            var modelJson = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(modelJsonPath, modelJson, cancellationToken);

            progress?.Report(new PiperDownloadProgress
            {
                DownloadedBytes = totalBytes > 0 ? totalBytes : downloadedBytes,
                TotalBytes = totalBytes > 0 ? totalBytes : downloadedBytes,
                CurrentFileName = null
            });

            return new PiperDownloadResult { Status = PiperDownloadStatus.Success };
        }
        catch (OperationCanceledException)
        {
            if (voiceModelPath != null && Directory.Exists(voiceModelPath))
            {
                try { Directory.Delete(voiceModelPath, true); } catch { }
            }
            return new PiperDownloadResult { Status = PiperDownloadStatus.Failed, ErrorMessage = "Загрузка отменена" };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при загрузке модели Piper {VoiceName}", voiceName);
            if (voiceModelPath != null && Directory.Exists(voiceModelPath))
            {
                try { Directory.Delete(voiceModelPath, true); } catch { }
            }
            return new PiperDownloadResult { Status = PiperDownloadStatus.Failed, ErrorMessage = ex.Message };
        }
        finally
        {
            if (lockTaken)
                DownloadLock.Release();
        }
    }

    public static async Task<MemoryStream?> GenerateVoice(ProcessedTtsMessage message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message.VoiceName))
                return null;

            if (string.IsNullOrWhiteSpace(message.Text))
                return null;

            bool NeedToTranslitToRussian = message.VoiceName.StartsWith("ru_RU") || message.VoiceName.StartsWith("uk_UA");
            message.Text = ConvertNumbers(message.Text);
            if (NeedToTranslitToRussian)
            {
                message.Text = Transliteration.LatinToCyrillic(message.Text);
                message.Text = Regex.Replace(message.Text, "q", "к", RegexOptions.IgnoreCase);
                message.Text = Regex.Replace(message.Text, "w", "в", RegexOptions.IgnoreCase);
                message.Text = Regex.Replace(message.Text, "y", "и", RegexOptions.IgnoreCase);
                //message.Text = Regex.Replace(message.Text, "h", "х", RegexOptions.IgnoreCase); //проверь переделку букв
                message.Text = Regex.Replace(message.Text, "ѵ", "й", RegexOptions.IgnoreCase);
            }
            else
            {
                message.Text = Transliteration.CyrillicToLatin(message.Text);
                message.Text = message.Text.Replace("`", "");
                message.Text = Regex.Replace(message.Text, "x", "h", RegexOptions.IgnoreCase);
            }

            //if (message.VoiceName.StartsWith )
            message.Text = CleanForTts(message.Text);
            //if (translitToRussian)
            //{ //не в рашн ёмаё а в английский у тебя и так всё на русском крендель!
            //    message.Text = Transliteration.LatinToCyrillic(message.Text);
            //}
            var piperExecutable = Path.Join(AppDataFolder, "piper", "piper.exe");
            if (!File.Exists(piperExecutable))
            {
                if (!PiperVoicesData.IsEngineInstalled())
                {
                    await PiperVoicesData.EnsureEngineDownloadedAsync();
                }

                if (!File.Exists(piperExecutable))
                {
                    Log.Error("Нету движка пайпер");
                    return null;
                }
            }

            var model = await VoiceModel.LoadModelByKey(message.VoiceName);
            float speakingRate = Math.Clamp(1 - message.VoiceSpeed / 10f, 0.1f, 3f);
            // чтобы избежать кэширования одинаковых запросов
            var piperModel = new PiperProvider(new PiperConfiguration()
            {
                //допили громкость 
                //P.S нельзя менять громкость в piper!!!!!!!
                ExecutableLocation = piperExecutable,
                WorkingDirectory = Path.Join(AppDataFolder, "piper"),
                Model = model,
                SpeakingRate = speakingRate //войс спид 0 почему-то всегда
            });

            byte[] wavBytes = await piperModel.InferAsync(
                message.Text,
                AudioOutputType.Wav
            );

            var wavMessage = new MemoryStream(wavBytes);
            if (wavMessage != null)
            {
                return wavMessage;
            }
            else
            {
                Log.Error("Не получилось сделать Piper озвучку");
                return null;
            }
        }
        catch (Exception)
        {
            Log.Error("Ошибка при генерации голоса Piper для текста: {Text}", message.Text);
            return null;
        }


    }

    static string ConvertNumbers(string text)
    {
        return Regex.Replace(text, @"\d+", match =>
        {
            long number;

            // пытаемся распарсить число
            if (!long.TryParse(match.Value, out number))
            {
                return match.Value;
            }

            try
            {
                return number.ToText("ru");
            }
            catch
            {
                return match.Value;
            }
        });
    }

    static string CleanForTts(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return new string(
            input.Where(c =>
            {
                var cat = Char.GetUnicodeCategory(c);

                return cat != UnicodeCategory.Control &&
                       cat != UnicodeCategory.Format &&
                       cat != UnicodeCategory.NonSpacingMark &&
                       cat != UnicodeCategory.Surrogate &&
                       c != '\0';
            }).ToArray()
        );
    }

}