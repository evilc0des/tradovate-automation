using System;
using System.IO;
using System.Text.Json;

namespace NinjaTraderTradovateBridge;

public sealed class RuntimeMarkersStore
{
    private readonly string _markerPath;
    private readonly IBridgeLogger _logger;

    public RuntimeMarkersStore(string markerPath, IBridgeLogger logger)
    {
        _markerPath = Path.GetFullPath(markerPath);
        _logger = logger;
    }

    public void MarkStartup()
    {
        Append("Startup");
    }

    public void MarkShutdown()
    {
        Append("Shutdown");
    }

    private void Append(string markerType)
    {
        try
        {
            var directory = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var marker = new RuntimeMarker
            {
                MarkerType = markerType,
                Timestamp = DateTimeOffset.UtcNow,
            };

            var line = JsonSerializer.Serialize(marker);
            File.AppendAllText(_markerPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to persist runtime marker: {markerType}", ex);
        }
    }
}

public sealed class RuntimeMarker
{
    public string MarkerType { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}
