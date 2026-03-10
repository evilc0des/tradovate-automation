using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTraderTradovateBridge;

public interface IMarketDataTransport : IAsyncDisposable
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken) where T : class;
}

public sealed class NdjsonTcpMarketDataTransport : IMarketDataTransport
{
    private readonly BridgeConfig _config;
    private readonly IBridgeLogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private TcpClient? _client;
    private StreamWriter? _writer;

    public NdjsonTcpMarketDataTransport(BridgeConfig config, IBridgeLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken) where T : class
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(message, _serializerOptions);
            await _writer!.WriteLineAsync(payload).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("Market data publish failed; resetting transport connection.");
            await ResetConnectionAsync().ConfigureAwait(false);
            _logger.Error("Market data transport error.", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is { Connected: true } && _writer is not null)
        {
            return;
        }

        await ResetConnectionAsync().ConfigureAwait(false);

        _client = new TcpClient();
        _client.NoDelay = true;
        await _client.ConnectAsync(_config.MarketDataHost, _config.MarketDataPort, cancellationToken).ConfigureAwait(false);
        _writer = new StreamWriter(_client.GetStream(), new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        _logger.Info($"Connected market data transport to {_config.MarketDataHost}:{_config.MarketDataPort}.");
    }

    private async Task ResetConnectionAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = null;
        }

        _client?.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetConnectionAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
