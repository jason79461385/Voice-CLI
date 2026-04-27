using System;
using System.Threading.Tasks;
using Google.Cloud.Speech.V1;
using Pv;
using Google.Protobuf;

namespace VoiceAgent.Win
{
    public class CrossPlatformSpeechService
    {
        private SpeechClient? _speechClient;
        private PvRecorder?   _recorder;
        private bool          _isListening;
        private readonly string _googleKeyPath;
        private DateTime      _lastVoiceTime;

        public event Action<string>? OnFinalResultReceived;
        public event Action<string>? OnPartialResultReceived;
        public event Action? OnListeningStopped;

        // 🌟 接收由 Program.cs 傳進來的設定值
        public int NoiseThreshold { get; set; } = 200;
        public double HoldTimeSeconds { get; set; } = 3.0;

        public bool IsListening   => _isListening;
        public bool IsProcessing  { get; set; } = false;

        public CrossPlatformSpeechService(string googleKeyPath)
        {
            _googleKeyPath = googleKeyPath;
        }

        public void StopListening() { _isListening = false; }
        public void ResetVoiceTimeout() { _lastVoiceTime = DateTime.Now; }

        public async Task StartListeningAsync()
        {
            if (_isListening) return;

            _isListening  = true;
            IsProcessing  = false;
            _lastVoiceTime = DateTime.Now;

            try
            {
                if (_speechClient == null)
                {
                    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _googleKeyPath);
                    _speechClient = await SpeechClient.CreateAsync();
                }

                var streamingCall = _speechClient.StreamingRecognize();

                await streamingCall.WriteAsync(new StreamingRecognizeRequest
                {
                    StreamingConfig = new StreamingRecognitionConfig
                    {
                        Config = new RecognitionConfig
                        {
                            Encoding              = RecognitionConfig.Types.AudioEncoding.Linear16,
                            SampleRateHertz       = 16000,
                            LanguageCode          = "zh-TW",
                            EnableAutomaticPunctuation = true
                        },
                        InterimResults  = true,
                        SingleUtterance = false
                    }
                });

                _recorder = PvRecorder.Create(512, -1);
                _recorder.Start();

                var receiveTask = Task.Run(async () =>
                {
                    var responseStream = streamingCall.GetResponseStream();
                    await foreach (var response in responseStream)
                    {
                        foreach (var result in response.Results)
                        {
                            if (result.Alternatives.Count == 0) continue;
                            string transcript = result.Alternatives[0].Transcript;

                            if (result.IsFinal)
                                OnFinalResultReceived?.Invoke(transcript);
                            else
                                OnPartialResultReceived?.Invoke(transcript);
                        }
                    }
                });

                while (_isListening)
                {
                    short[] frame = _recorder.Read();

                    // 🌟 音量計算與動態閥值比對
                    long sum = 0;
                    for (int i = 0; i < frame.Length; i++) sum += Math.Abs(frame[i]);
                    double currentVolume = sum / (double)frame.Length;

                    if (currentVolume > NoiseThreshold) 
                        _lastVoiceTime = DateTime.Now;

                    // 🌟 靜音等待時間改為動態小數 (例如 0.4 秒)
                    if (!IsProcessing && (DateTime.Now - _lastVoiceTime).TotalSeconds > HoldTimeSeconds)
                    {
                        _isListening = false;
                        break;
                    }

                    byte[] byteBuffer = new byte[frame.Length * 2];
                    Buffer.BlockCopy(frame, 0, byteBuffer, 0, byteBuffer.Length);
                    await streamingCall.WriteAsync(new StreamingRecognizeRequest
                    {
                        AudioContent = ByteString.CopyFrom(byteBuffer)
                    });
                }

                _recorder.Stop();
                _recorder.Dispose();
                await streamingCall.WriteCompleteAsync();
                await receiveTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STT] 錯誤：{ex.Message}");
                _isListening = false;
            }
            finally
            {
                OnListeningStopped?.Invoke();
            }
        }
    }
}