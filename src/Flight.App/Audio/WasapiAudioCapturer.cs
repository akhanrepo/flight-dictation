using Flight.Core.Audio;
using NAudio.Wave;

namespace Flight.App.Audio;

/// <summary>
/// Microphone capture via NAudio at 16kHz mono 16-bit (whisper's input
/// format; the driver/ACM converts if the device runs at another rate).
/// Samples only accumulate between Start and Stop.
/// </summary>
public sealed class WasapiAudioCapturer : IAudioCapturer, IDisposable
{
    private readonly object _lock = new();
    private WaveInEvent? _waveIn;
    private List<float>? _samples;

    public void Start()
    {
        lock (_lock)
        {
            if (_waveIn is not null)
            {
                return;
            }

            _samples = [];
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (_samples is null)
            {
                return;
            }

            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i);
                _samples.Add(sample / 32768f);
            }
        }
    }

    public float[] Stop()
    {
        lock (_lock)
        {
            if (_waveIn is null || _samples is null)
            {
                return [];
            }

            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;

            var captured = _samples.ToArray();
            _samples = null;
            return captured;
        }
    }

    public void Dispose() => Stop();
}
