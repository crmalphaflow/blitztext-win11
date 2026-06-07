using System.IO;
using NAudio.Wave;

namespace BlitztextWindows;

public sealed class AudioRecorderService : IDisposable
{
    private WaveInEvent? waveIn;
    private WaveFileWriter? writer;
    private DateTimeOffset startedAt;
    private string? currentPath;

    public void Start()
    {
        Cancel();
        currentPath = Path.Combine(Path.GetTempPath(), $"blitztext-{Guid.NewGuid():N}.wav");
        waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        writer = new WaveFileWriter(currentPath, waveIn.WaveFormat);
        waveIn.DataAvailable += (_, e) => writer?.Write(e.Buffer, 0, e.BytesRecorded);
        waveIn.RecordingStopped += (_, _) => writer?.Flush();
        startedAt = DateTimeOffset.UtcNow;
        waveIn.StartRecording();
    }

    public RecordingResult Stop()
    {
        if (waveIn is null || writer is null || currentPath is null)
        {
            throw new InvalidOperationException("Es laeuft keine Aufnahme.");
        }

        var path = currentPath;
        var duration = DateTimeOffset.UtcNow - startedAt;
        waveIn.StopRecording();
        waveIn.Dispose();
        writer.Dispose();
        waveIn = null;
        writer = null;
        currentPath = null;
        return new RecordingResult(path, duration);
    }

    public void Cancel()
    {
        var path = currentPath;
        try { waveIn?.StopRecording(); } catch { }
        waveIn?.Dispose();
        writer?.Dispose();
        waveIn = null;
        writer = null;
        currentPath = null;
        if (path is not null)
        {
            try { File.Delete(path); } catch { }
        }
    }

    public void Dispose() => Cancel();
}

public sealed record RecordingResult(string FilePath, TimeSpan Duration);
