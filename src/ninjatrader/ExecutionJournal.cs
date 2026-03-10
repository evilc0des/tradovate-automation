using System;
using System.IO;
using System.Text.Json;

namespace NinjaTraderTradovateBridge;

public sealed class ExecutionJournal
{
    private readonly string _journalPath;
    private readonly IBridgeLogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ExecutionJournal(string journalPath, IBridgeLogger logger)
    {
        _journalPath = Path.GetFullPath(journalPath);
        _logger = logger;
    }

    public void Append(ExecutionJournalEntry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = JsonSerializer.Serialize(entry, _jsonOptions);
            File.AppendAllText(_journalPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist execution journal entry.", ex);
        }
    }
}

public sealed class ExecutionJournalEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string EventType { get; init; } = string.Empty;
    public string SignalId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string Instrument { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int FilledQuantity { get; init; }
    public string Detail { get; init; } = string.Empty;
}
