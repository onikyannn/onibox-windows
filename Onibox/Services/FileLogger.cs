using System;
using System.IO;
using System.Text;

namespace Onibox.Services;

public sealed class FileLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _writer;

    public string LogPath { get; }

    public FileLogger(string logPath)
    {
        LogPath = logPath;
        var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}
