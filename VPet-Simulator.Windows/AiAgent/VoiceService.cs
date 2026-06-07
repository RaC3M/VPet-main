using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using SherpaOnnx;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class VoiceService : IDisposable
{
    private static readonly string ProjectRoot = ResolveProjectRoot();
    private static readonly string ModelDir = Path.Combine(ProjectRoot, "voicemodel");
    private static readonly string RvcTtsDir = Path.Combine(ProjectRoot, "RVC TTS");
    private static readonly string RvcModelDir = Path.Combine(ProjectRoot, "RVC Voice Model");
    private const int DefaultRvcServerPort = 53683;
    private static readonly HttpClient RvcHttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly JsonSerializerOptions RvcJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object RvcServerLock = new();
    private static Process? RvcServerProcess;
    private OfflineRecognizer? _recognizer;
    private OfflineTts? _tts;
    private bool _isRvcTtsReady;
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private readonly object _lock = new();
    private readonly object _micLock = new();

    static VoiceService()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopRvcServer();
    }

    public bool IsAsrReady => _recognizer != null;
    public bool IsTtsReady => _tts != null || _isRvcTtsReady;

    public event EventHandler<string>? SpeechRecognized;
    public event EventHandler<bool>? RecordingStateChanged;

    private sealed class RvcSpeechResponse
    {
        public bool Ok { get; set; }
        public string Path { get; set; } = "";
        public string Error { get; set; } = "";
    }

    private static string ResolveProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VPet.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    public void InitializeAsr()
    {
        var modelPath = Path.Combine(ModelDir, "sherpa-onnx-paraformer-zh-2023-09-14", "model.int8.onnx");
        var tokensPath = Path.Combine(ModelDir, "sherpa-onnx-paraformer-zh-2023-09-14", "tokens.txt");

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            VoiceLogger.Log($"[InitASR] 模型找不到: model={File.Exists(modelPath)}, tokens={File.Exists(tokensPath)}");
            return;
        }

        VoiceLogger.Log($"[InitASR] 開始載入 ASR 模型...");
        try
        {
            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Paraformer.Model = modelPath;
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.Debug = 0;
            config.ModelConfig.NumThreads = 2;
            config.ModelConfig.Provider = "cpu";
            _recognizer = new OfflineRecognizer(config);
            VoiceLogger.Log($"[InitASR] ASR 載入完成");
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[InitASR]", ex);
        }
    }

    public void InitializeTts()
    {
        if (AiAgentEnvironment.GetTtsProvider().Equals("rvc", StringComparison.OrdinalIgnoreCase))
        {
            _isRvcTtsReady = InitializeRvcTts();
            return;
        }

        var modelDir = Path.Combine(ModelDir, "vits-zh-hf-fanchen-C");
        var modelPath = Path.Combine(modelDir, "vits-zh-hf-fanchen-C.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");
        var lexiconPath = Path.Combine(modelDir, "lexicon.txt");

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            VoiceLogger.Log($"[InitTTS] 模型找不到: model={File.Exists(modelPath)}, tokens={File.Exists(tokensPath)}");
            return;
        }

        VoiceLogger.Log($"[InitTTS] 開始載入 TTS 模型...");
        try
        {
            var config = new OfflineTtsConfig();
            config.Model.Vits.Model = modelPath;
            config.Model.Vits.Tokens = tokensPath;
            config.Model.Vits.Lexicon = lexiconPath;
            config.Model.Vits.NoiseScale = 0.667f;
            config.Model.Vits.NoiseScaleW = 0.8f;
            config.Model.Vits.LengthScale = 1.0f;
            config.Model.NumThreads = 2;
            config.Model.Provider = "cpu";
            config.Model.Debug = 0;
            config.RuleFsts = $"{modelDir}/phone.fst,{modelDir}/date.fst,{modelDir}/number.fst";
            config.MaxNumSentences = 1;
            _tts = new OfflineTts(config);
            VoiceLogger.Log($"[InitTTS] TTS 載入完成 (fanchen-C)");
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[InitTTS]", ex);
        }
    }

    public void StartRecording()
    {
        WaveInEvent? waveIn;
        lock (_micLock)
        {
            if (_waveIn != null)
            {
                VoiceLogger.Log("[Mic] StartRecording 跳過：已有錄音進行中");
                return;
            }
            VoiceLogger.Log("[Mic] 開始錄音...");
            _audioBuffer = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };
            _waveIn.DataAvailable += OnAudioData;
            _waveIn.RecordingStopped += OnRecordingStopped;
            waveIn = _waveIn;
        }
        waveIn.StartRecording();
        RecordingStateChanged?.Invoke(this, true);
    }

    public void StopRecording()
    {
        WaveInEvent? toDispose;
        lock (_micLock)
        {
            if (_waveIn == null)
            {
                VoiceLogger.Log("[Mic] StopRecording 跳過：沒有進行中的錄音");
                return;
            }
            VoiceLogger.Log("[Mic] 停止錄音...");
            toDispose = _waveIn;
            _waveIn = null;
        }
        toDispose.StopRecording();
        toDispose.Dispose();
    }

    private int _dataCount;
    private void OnAudioData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            _audioBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        _dataCount++;
        if (_dataCount % 50 == 0)
            VoiceLogger.Log($"[Mic] 接收音訊中... {_audioBuffer?.Length ?? 0} bytes");
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        RecordingStateChanged?.Invoke(this, false);

        byte[] audioBytes;
        lock (_lock)
        {
            if (_audioBuffer == null || _audioBuffer.Length == 0)
            {
                VoiceLogger.Log("[Mic] 錄音停止：無音訊資料");
                _audioBuffer?.Dispose();
                _audioBuffer = null;
                return;
            }
            audioBytes = _audioBuffer.ToArray();
            _audioBuffer.Dispose();
            _audioBuffer = null;
        }

        _dataCount = 0;
        VoiceLogger.Log($"[Mic] 錄音停止，取得 {audioBytes.Length} bytes ({audioBytes.Length / 2} samples)");

        var captured = audioBytes;
        Task.Run(() =>
        {
            try
            {
                var text = RecognizeBytes(captured);
                VoiceLogger.Log($"[ASR] 辨識結果: \"{text}\"");
                if (!string.IsNullOrWhiteSpace(text))
                    SpeechRecognized?.Invoke(this, text);
            }
            catch (Exception ex)
            {
                VoiceLogger.LogError("[ASR]", ex);
            }
        });
    }

    private string RecognizeBytes(byte[] audioBytes)
    {
        if (_recognizer == null)
        {
            VoiceLogger.Log("[ASR] 跳過：ASR 未初始化");
            return "";
        }
        if (audioBytes.Length < 100)
        {
            VoiceLogger.Log($"[ASR] 跳過：音訊太短 ({audioBytes.Length} bytes)");
            return "";
        }

        try
        {
            var samples = new float[audioBytes.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(audioBytes, i * 2) / 32768f;

            VoiceLogger.Log($"[ASR] 開始辨識 ({samples.Length} samples)...");
            var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            _recognizer.Decode(new List<OfflineStream> { stream });
            var result = stream.Result;
            var text = result.Text ?? "";
            VoiceLogger.Log($"[ASR] 辨識完成: \"{text}\"");
            return text;
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[ASR]", ex);
            return "";
        }
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        VoiceLogger.Log($"[TTS] 開始合成: \"{text}\"");
        Task.Run(() =>
        {
            try
            {
                if (AiAgentEnvironment.GetTtsProvider().Equals("rvc", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_isRvcTtsReady)
                    {
                        VoiceLogger.Log("[RVC] TTS is not ready");
                        return;
                    }

                    var rvcFile = GenerateRvcSpeech(text);
                    if (!string.IsNullOrWhiteSpace(rvcFile) && File.Exists(rvcFile))
                        PlayAudioFile(rvcFile, deleteAfterPlayback: false);
                    return;
                }

                if (_tts == null)
                    return;

                var genConfig = new SherpaOnnx.OfflineTtsGenerationConfig
                {
                    Sid = 20,
                    Speed = 1.0f,
                    SilenceScale = 0.2f
                };
                var audio = _tts.GenerateWithConfig(text, genConfig, null);
                var tempFile = Path.Combine(Path.GetTempPath(), "vpet_tts_" + Guid.NewGuid() + ".wav");
                audio.SaveToWaveFile(tempFile);
                VoiceLogger.Log($"[TTS] 合成完成，儲存至 {tempFile}");

                using var reader = new AudioFileReader(tempFile);
                using var output = new NAudio.Wave.DirectSoundOut();
                var mre = new ManualResetEvent(false);
                output.PlaybackStopped += (_, _) => mre.Set();
                output.Init(reader);
                output.Play();
                VoiceLogger.Log("[TTS] 開始播放");
                mre.WaitOne();
                VoiceLogger.Log("[TTS] 播放結束");

                try { File.Delete(tempFile); } catch { }
            }
            catch (Exception ex)
            {
                VoiceLogger.LogError("[TTS]", ex);
            }
        });
    }

    private static bool InitializeRvcTts()
    {
        var scriptPath = Path.Combine(RvcTtsDir, "vpet_rvc_server.py");
        var modelPath = FindFirstFile(RvcModelDir, "*.pth");
        var indexPath = FindFirstFile(RvcModelDir, "*.index");

        if (!File.Exists(scriptPath) || string.IsNullOrWhiteSpace(modelPath))
        {
            VoiceLogger.Log($"[RVC] Missing files: script={File.Exists(scriptPath)}, model={File.Exists(modelPath ?? "")}");
            return false;
        }

        if (!CanRunPython())
        {
            VoiceLogger.Log("[RVC] Python is not available. Install Python 3.10-3.12 and dependencies from RVC TTS/setup.py.");
            return false;
        }

        if (!EnsureRvcServer())
            return false;

        VoiceLogger.Log($"[RVC] Server ready: model={modelPath}, index={indexPath}");
        return true;
    }

    private static string GenerateRvcSpeech(string text)
    {
        if (!EnsureRvcServer())
        {
            VoiceLogger.Log("[RVC] Server is not ready");
            return "";
        }

        try
        {
            var port = GetRvcServerPort();
            var payload = new
            {
                text,
                filename = "vpet_rvc_" + Guid.NewGuid() + ".wav"
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload, RvcJsonOptions), Encoding.UTF8, "application/json");
            using var response = RvcHttpClient.PostAsync($"http://127.0.0.1:{port}/speak", content).GetAwaiter().GetResult();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                VoiceLogger.Log($"[RVC] Server failed: {json}");
                return "";
            }

            var result = JsonSerializer.Deserialize<RvcSpeechResponse>(json, RvcJsonOptions);
            if (result == null || !result.Ok || string.IsNullOrWhiteSpace(result.Path))
            {
                VoiceLogger.Log($"[RVC] Invalid server response: {json}");
                return "";
            }

            VoiceLogger.Log($"[RVC] Generated: {result.Path}");
            return result.Path;
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[RVC]", ex);
            return "";
        }
    }

    private static bool EnsureRvcServer()
    {
        lock (RvcServerLock)
        {
            var port = GetRvcServerPort();
            if (RvcServerProcess != null && !RvcServerProcess.HasExited && IsRvcServerHealthy(port))
                return true;

            if (RvcServerProcess != null)
            {
                try { RvcServerProcess.Dispose(); } catch { }
                RvcServerProcess = null;
            }

            var pythonExe = ResolvePythonCommand();
            var scriptPath = Path.Combine(RvcTtsDir, "vpet_rvc_server.py");
            var modelPath = FindFirstFile(RvcModelDir, "*.pth") ?? "";
            var indexPath = FindFirstFile(RvcModelDir, "*.index") ?? "";
            var outputDir = Path.Combine(RvcTtsDir, "output");
            var voice = AiAgentEnvironment.Get(AiAgentEnvironment.RvcEdgeVoice);
            if (string.IsNullOrWhiteSpace(voice))
                voice = "zh-TW-HsiaoYuNeural";
            var device = AiAgentEnvironment.Get(AiAgentEnvironment.RvcDevice);
            if (string.IsNullOrWhiteSpace(device))
                device = "cpu";
            var pitch = AiAgentEnvironment.Get(AiAgentEnvironment.RvcPitch);
            if (string.IsNullOrWhiteSpace(pitch))
                pitch = "0";
            var indexRate = AiAgentEnvironment.Get(AiAgentEnvironment.RvcIndexRate);
            if (string.IsNullOrWhiteSpace(indexRate))
                indexRate = "0.75";

            if (!File.Exists(scriptPath) || string.IsNullOrWhiteSpace(modelPath))
                return false;

            Directory.CreateDirectory(outputDir);

            var process = new Process
            {
                StartInfo = CreatePythonStartInfo(pythonExe, scriptPath, redirectOutput: false)
            };
            process.StartInfo.ArgumentList.Add("--host");
            process.StartInfo.ArgumentList.Add("127.0.0.1");
            process.StartInfo.ArgumentList.Add("--port");
            process.StartInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add("--model");
            process.StartInfo.ArgumentList.Add(modelPath);
            process.StartInfo.ArgumentList.Add("--index");
            process.StartInfo.ArgumentList.Add(indexPath);
            process.StartInfo.ArgumentList.Add("--output-dir");
            process.StartInfo.ArgumentList.Add(outputDir);
            process.StartInfo.ArgumentList.Add("--voice");
            process.StartInfo.ArgumentList.Add(voice);
            process.StartInfo.ArgumentList.Add("--device");
            process.StartInfo.ArgumentList.Add(device);
            process.StartInfo.ArgumentList.Add("--pitch");
            process.StartInfo.ArgumentList.Add(pitch);
            process.StartInfo.ArgumentList.Add("--index-rate");
            process.StartInfo.ArgumentList.Add(indexRate);

            process.Start();
            RvcServerProcess = process;

            if (WaitForRvcServer(port))
                return true;

            try { process.Kill(entireProcessTree: true); } catch { }
            try { process.Dispose(); } catch { }
            RvcServerProcess = null;
            VoiceLogger.Log("[RVC] Server failed to start");
            return false;
        }
    }

    private static bool WaitForRvcServer(int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (RvcServerProcess == null || RvcServerProcess.HasExited)
                return false;

            if (IsRvcServerHealthy(port))
                return true;

            Thread.Sleep(500);
        }

        return false;
    }

    private static bool IsRvcServerHealthy(int port)
    {
        try
        {
            using var response = RvcHttpClient.GetAsync($"http://127.0.0.1:{port}/health").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static int GetRvcServerPort()
    {
        var value = AiAgentEnvironment.Get(AiAgentEnvironment.RvcServerPort);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port > 0
            ? port
            : DefaultRvcServerPort;
    }

    private static void StopRvcServer()
    {
        lock (RvcServerLock)
        {
            if (RvcServerProcess == null)
                return;

            try
            {
                if (!RvcServerProcess.HasExited)
                    RvcServerProcess.Kill(entireProcessTree: true);
            }
            catch { }
            try { RvcServerProcess.Dispose(); } catch { }
            RvcServerProcess = null;
        }
    }

    private static void PlayAudioFile(string filePath, bool deleteAfterPlayback)
    {
        using var reader = new AudioFileReader(filePath);
        using var output = new NAudio.Wave.DirectSoundOut();
        var mre = new ManualResetEvent(false);
        output.PlaybackStopped += (_, _) => mre.Set();
        output.Init(reader);
        output.Play();
        mre.WaitOne();

        if (deleteAfterPlayback)
            try { File.Delete(filePath); } catch { }
    }

    private static string? FindFirstFile(string directory, string pattern)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;
    }

    private static string ResolvePythonCommand()
    {
        var configured = AiAgentEnvironment.Get(AiAgentEnvironment.RvcPythonExe);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var venvPython = Path.Combine(RvcTtsDir, ".venv", "Scripts", "python.exe");
        return File.Exists(venvPython) ? venvPython : "py";
    }

    private static ProcessStartInfo CreatePythonStartInfo(string pythonExe, string scriptPath, bool redirectOutput = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            WorkingDirectory = RvcTtsDir
        };

        var exeName = Path.GetFileNameWithoutExtension(pythonExe);
        if (exeName.Equals("py", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add("-3");

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.Environment["PYTHONPATH"] = RvcTtsDir;
        var ffmpegBin = ResolveFfmpegBin();
        if (!string.IsNullOrWhiteSpace(ffmpegBin))
            PrependEnvironmentPath(startInfo, ffmpegBin);
        return startInfo;
    }

    private static void PrependEnvironmentPath(ProcessStartInfo startInfo, string path)
    {
        var key = startInfo.Environment.Keys.FirstOrDefault(x => x.Equals("PATH", StringComparison.OrdinalIgnoreCase)) ?? "PATH";
        startInfo.Environment[key] = path + Path.PathSeparator + (startInfo.Environment.TryGetValue(key, out var value) ? value : "");
    }

    private static string ResolveFfmpegBin()
    {
        var ffmpegRoot = Path.Combine(RvcTtsDir, "ffmpeg");
        if (!Directory.Exists(ffmpegRoot))
            return "";

        var ffmpegExe = Directory.GetFiles(ffmpegRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
        return string.IsNullOrWhiteSpace(ffmpegExe) ? "" : Path.GetDirectoryName(ffmpegExe) ?? "";
    }

    private static bool CanRunPython()
    {
        try
        {
            var pythonExe = ResolvePythonCommand();
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RvcTtsDir
            };

            if (Path.GetFileNameWithoutExtension(pythonExe).Equals("py", StringComparison.OrdinalIgnoreCase))
                process.StartInfo.ArgumentList.Add("-3");
            process.StartInfo.ArgumentList.Add("--version");
            process.Start();
            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        StopRecording();
        _recognizer?.Dispose();
        _tts?.Dispose();
        StopRvcServer();
        VoiceLogger.Log("[VoiceService] 已釋放資源");
    }
}
