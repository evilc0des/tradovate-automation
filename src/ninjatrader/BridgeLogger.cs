using System;
using System.IO;
using System.Text;

namespace NinjaTraderTradovateBridge;

public interface IBridgeLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class ConsoleBridgeLogger : IBridgeLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO] {DateTimeOffset.UtcNow:o} {message}");

    public void Warn(string message) => Console.WriteLine($"[WARN] {DateTimeOffset.UtcNow:o} {message}");

    public void Error(string message, Exception? exception = null)
    {
        Console.WriteLine($"[ERROR] {DateTimeOffset.UtcNow:o} {message}");
        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }
}

/// <summary>
/// Writes structured log lines to both stdout and a per-calendar-day file under
/// the configured directory.  A new file is opened automatically at each UTC day
/// boundary.  Thread-safe.  File name pattern: YYYY-MM-DD.log
/// </summary>
public sealed class FileBridgeLogger : IBridgeLogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly object _lock = new object();
    private StreamWriter? _writer;
    private string _currentDay = string.Empty;

    public FileBridgeLogger(string logDirectory)
    {
        _logDirectory = Path.GetFullPath(logDirectory);
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var now = DateTimeOffset.UtcNow;
        var line = $"[{level}] {now:o} {message}";
        Console.WriteLine(line);
        lock (_lock)
        {
            EnsureWriter(now);
            _writer!.WriteLine(line);
            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
            _writer.Flush();
        }
    }

    private void EnsureWriter(DateTimeOffset now)
    {
        var day = now.ToString("yyyy-MM-dd");
        if (day == _currentDay && _writer is not null)
        {
            return;
        }

        _writer?.Dispose();
        var path = Path.Combine(_logDirectory, $"{day}.log");
        _writer = new StreamWriter(path, append: true, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _currentDay = day;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
