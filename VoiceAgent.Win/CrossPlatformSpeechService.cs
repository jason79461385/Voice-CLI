using System;
using System.Threading.Tasks;
using Google.Cloud.Speech.V1;
using Pv;
using Google.Protobuf;

namespace VoiceAgent.Win
{
    public class CrossPlatformSpeechService
    {
        private SpeechClient? _speechClient; private PvRecorder? _recorder;
        private bool _isListening; private readonly string _googleKeyPath;
        private DateTime _lastVoiceTime;
        public event Action<string>? OnFinalResultReceived;
        public bool IsListening => _isListening;
        public bool IsProcessing { get; set; } = false;

        public CrossPlatformSpeechService(string googleKeyPath) { _googleKeyPath = googleKeyPath; }
        public void StopListening() { if (_isListening) _isListening = false; }
        public void ResetVoiceTimeout() { _lastVoiceTime = DateTime.Now; }

        public async Task StartListeningAsync()
        {
            if (_isListening) return;
            _isListening = true; IsProcessing = false; _lastVoiceTime = DateTime.Now;
            try
            {
                if (_speechClient == null)
                {
                    // 【最正確解法】設定環境變數，Google 核心會自動抓取，完全無警告！
                    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _googleKeyPath);
                    _speechClient = await SpeechClient.CreateAsync();
                }
                var streamingCall = _speechClient.StreamingRecognize();
                await streamingCall.WriteAsync(new StreamingRecognizeRequest { StreamingConfig = new StreamingRecognitionConfig { Config = new RecognitionConfig { Encoding = RecognitionConfig.Types.AudioEncoding.Linear16, SampleRateHertz = 16000, LanguageCode = "zh-TW", EnableAutomaticPunctuation = true }, InterimResults = true, SingleUtterance = false } });
                
                _recorder = PvRecorder.Create(512, -1); _recorder.Start();
                var receiveTask = Task.Run(async () =>
                {
                    var responseStream = streamingCall.GetResponseStream();
                    await foreach (var response in responseStream)
                    {
                        foreach (var result in response.Results) { if (result.IsFinal && result.Alternatives.Count > 0) OnFinalResultReceived?.Invoke(result.Alternatives[0].Transcript); }
                    }
                });

                while (_isListening)
                {
                    short[] frame = _recorder.Read();
                    long sum = 0; for (int i = 0; i < frame.Length; i++) sum += Math.Abs(frame[i]);
                    if (sum / (double)frame.Length > 200) _lastVoiceTime = DateTime.Now;
                    if (!IsProcessing && (DateTime.Now - _lastVoiceTime).TotalSeconds > 5) { _isListening = false; break; }
                    
                    byte[] byteBuffer = new byte[frame.Length * 2];
                    Buffer.BlockCopy(frame, 0, byteBuffer, 0, byteBuffer.Length);
                    await streamingCall.WriteAsync(new StreamingRecognizeRequest { AudioContent = ByteString.CopyFrom(byteBuffer) });
                }
                _recorder.Stop(); _recorder.Dispose(); await streamingCall.WriteCompleteAsync();
            }
            catch { _isListening = false; }
        }
    }
}