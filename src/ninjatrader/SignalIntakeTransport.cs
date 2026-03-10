using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

public sealed class SignalIntakeTransport
{
    private readonly BridgeConfig _config;
    private readonly ExecutionBridge _executionBridge;
    private readonly IBridgeLogger _logger;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HashSet<string> _seenSources = new(StringComparer.OrdinalIgnoreCase);

    public SignalIntakeTransport(BridgeConfig config, ExecutionBridge executionBridge, IBridgeLogger logger)
    {
        _config = config;
        _executionBridge = executionBridge;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var ip = IPAddress.Parse(_config.SignalHost);
        var listener = new TcpListener(ip, _config.SignalPort);
        listener.Start();
        _logger.Info($"Signal intake listening on {_config.SignalHost}:{_config.SignalPort}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
            _logger.Info("Signal intake listener stopped.");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;

        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_config.SignalReadTimeoutMs);

                string? line;
                try
                {
                    line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _logger.Warn("Signal client timed out waiting for data; closing connection.");
                    break;
                }
                catch (IOException)
                {
                    _logger.Info("Signal client disconnected during read.");
                    break;
                }

                if (line is null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var correlationId = TryExtractCorrelationId(trimmed) ?? Guid.NewGuid().ToString("N");
                var signalId = TryExtractSignalId(trimmed) ?? string.Empty;

                TradeSignal? signal;
                try
                {
                    signal = JsonSerializer.Deserialize<TradeSignal>(trimmed, _serializerOptions);
                }
                catch (JsonException ex)
                {
                    _logger.Warn($"Malformed signal JSON: {ex.Message}");
                    await WriteErrorAsync(writer, correlationId, "SIG_MALFORMED", "Malformed signal JSON", ex.Message, retryable: false).ConfigureAwait(false);
                    continue;
                }

                if (signal is null)
                {
                    await WriteErrorAsync(writer, correlationId, "SIG_EMPTY", "Signal payload was null", "Deserializer returned null", retryable: false).ConfigureAwait(false);
                    continue;
                }

                TrackSignalSource(signal.SourceId);
                var ack = _executionBridge.HandleSignal(signal);

                if (string.IsNullOrWhiteSpace(ack.CorrelationId))
                {
                    ack.CorrelationId = correlationId;
                }
                if (string.IsNullOrWhiteSpace(ack.SignalId))
                {
                    ack.SignalId = signalId;
                }

                var payload = JsonSerializer.Serialize(ack, _serializerOptions);
                await writer.WriteLineAsync(payload).ConfigureAwait(false);
                _logger.Info($"Signal processed source={signal.SourceId} signalId={ack.SignalId} status={ack.Status}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Signal intake client handler crashed.", ex);
        }
    }

    private void TrackSignalSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        if (_seenSources.Add(sourceId))
        {
            _logger.Info($"Discovered signal source: {sourceId}");
        }
    }

    private static string? TryExtractCorrelationId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("correlationId", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch
        {
            // Best effort extraction only.
        }

        return null;
    }

    private static string? TryExtractSignalId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("signalId", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch
        {
            // Best effort extraction only.
        }

        return null;
    }

    private async Task WriteErrorAsync(StreamWriter writer, string correlationId, string code, string message, string details, bool retryable)
    {
        var errorEnvelope = new ErrorEnvelope
        {
            CorrelationId = correlationId,
            Code = code,
            Severity = "Error",
            Message = message,
            Details = details,
            Retryable = retryable,
        };

        var payload = JsonSerializer.Serialize(errorEnvelope, _serializerOptions);
        await writer.WriteLineAsync(payload).ConfigureAwait(false);
    }
}
