using System;

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
