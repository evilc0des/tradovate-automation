using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NinjaTraderTradovateBridge;

var mode = args.Length > 0 ? args[0] : "publisher-smoke";

if (string.Equals(mode, "--rust-e2e", StringComparison.OrdinalIgnoreCase))
{
    await RunRustE2EAsync();
    return;
}

if (string.Equals(mode, "--signal-intake-smoke", StringComparison.OrdinalIgnoreCase))
{
    await RunSignalIntakeSmokeAsync();
    return;
}

if (string.Equals(mode, "--signal-intake-rust-e2e", StringComparison.OrdinalIgnoreCase))
{
    await RunSignalIntakeRustE2EAsync();
    return;
}

if (string.Equals(mode, "--phase8-smoke", StringComparison.OrdinalIgnoreCase))
{
    await RunPhase8SmokeAsync();
    return;
}

if (string.Equals(mode, "--phase9-smoke", StringComparison.OrdinalIgnoreCase))
{
    await RunPhase9SmokeAsync();
    return;
}

if (string.Equals(mode, "--phase10-smoke", StringComparison.OrdinalIgnoreCase))
{
    await RunPhase10SmokeAsync();
    return;
}

await RunPublisherSmokeAsync();

static async Task RunPublisherSmokeAsync()
{
    var config = new BridgeConfig
    {
        MarketDataHost = "127.0.0.1",
        MarketDataPort = 19100,
        SignalHost = "127.0.0.1",
        SignalPort = 19101,
        LiveTradingEnabled = false,
        DisarmOnStartup = false,
        AllowedAccount = "SIM101",
        AllowedInstruments = ["MES 06-26"],
        AllowedSignalSources = ["test-host", "rust.strategy"],
    };

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromSeconds(3));

    var logger = new ConsoleBridgeLogger();

    var marketDataServerTask = RunMarketDataCaptureServerAsync(config.MarketDataPort, cts.Token, "[FRAME]");
    await Task.Delay(150, cts.Token);

    await using var transport = new NdjsonTcpMarketDataTransport(config, logger);
    var publisher = new MarketDataPublisher(transport, logger);
    var feed = new SimulationMarketDataFeed(publisher, "MES 06-26");

    try
    {
        await feed.RunAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        // expected on test timeout
    }

    await marketDataServerTask;

    RunExecutionBridgeSmokeCheck(config);

    Console.WriteLine("Test host completed.");
}

static async Task RunRustE2EAsync()
{
    var marketDataPort = 9100;
    var signalPort = 9101;
    var instrument = "MES 06-26";

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    Console.WriteLine("[RUST-E2E] Starting end-to-end run.");

    var signalCaptureTask = RunSignalCaptureServerAsync(signalPort, cts.Token);

    var rustDir = LocateRustServiceDirectory();
    using var rustProcess = StartRustService(rustDir, marketDataPort, signalPort, instrument);
    try
    {
        await Task.Delay(2500, cts.Token);
        await SendMarketDataBurstToRustAsync(marketDataPort, instrument, cts.Token);

        var signal = await signalCaptureTask;
        Console.WriteLine($"[RUST-SIGNAL] {signal}");
        Console.WriteLine("[RUST-E2E] Completed.");
    }
    finally
    {
        TryStopProcess(rustProcess);
    }
}

static async Task RunSignalIntakeSmokeAsync()
{
    var config = new BridgeConfig
    {
        SignalHost = "127.0.0.1",
        SignalPort = 19101,
        LiveTradingEnabled = false,
        DisarmOnStartup = false,
        AllowedAccount = "SIM101",
        AllowedInstruments = ["MES 06-26"],
        AllowedSignalSources = ["test-host", "rust.strategy"],
        SignalReadTimeoutMs = 5000,
    };

        var logger = new ConsoleBridgeLogger();
        var bridge = new ExecutionBridge(config);
        bridge.Arm();

        var intake = new SignalIntakeTransport(config, bridge, logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var intakeTask = intake.RunAsync(cts.Token);
        await Task.Delay(250, cts.Token);

        // First connection: valid signal should return SignalAck Accepted.
        var validSignal = new TradeSignal
        {
            MessageType = "TradeSignal",
            Version = "v1",
            Timestamp = DateTimeOffset.UtcNow,
            SourceId = "test-host",
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignalId = Guid.NewGuid().ToString("N"),
            StrategyId = "phase6-smoke",
            Account = "SIM101",
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            OrderType = "Market",
            Reason = "phase6 valid",
        };

        var ack1 = await SendSignalAndReadResponseAsync(config.SignalHost, config.SignalPort, JsonSerializer.Serialize(validSignal), cts.Token);
        Console.WriteLine($"[PHASE6-ACK1] {ack1}");

        // Same connection semantics over reconnect: malformed JSON should return ErrorEnvelope.
        var malformed = "{\"messageType\":\"TradeSignal\", bad-json";
        var err = await SendSignalAndReadResponseAsync(config.SignalHost, config.SignalPort, malformed, cts.Token);
        Console.WriteLine($"[PHASE6-ERR] {err}");

        // Reconnect with invalid semantic source to verify safe rejection.
        var invalidSourceSignal = new TradeSignal
        {
            MessageType = "TradeSignal",
            Version = "v1",
            Timestamp = DateTimeOffset.UtcNow,
            SourceId = "unknown-source",
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignalId = Guid.NewGuid().ToString("N"),
            StrategyId = "phase6-smoke",
            Account = "SIM101",
            Instrument = "MES 06-26",
            Side = "Buy",
            Quantity = 1,
            OrderType = "Market",
        };

        var ack2 = await SendSignalAndReadResponseAsync(config.SignalHost, config.SignalPort, JsonSerializer.Serialize(invalidSourceSignal), cts.Token);
        Console.WriteLine($"[PHASE6-ACK2] {ack2}");

        cts.Cancel();
        try
        {
            await intakeTask;
        }
        catch (OperationCanceledException)
        {
        }

    Console.WriteLine("[PHASE6] Signal intake smoke completed.");
}

static async Task RunSignalIntakeRustE2EAsync()
{
    var config = new BridgeConfig
    {
        MarketDataHost = "127.0.0.1",
        MarketDataPort = 9100,
        SignalHost = "127.0.0.1",
        SignalPort = 9101,
        LiveTradingEnabled = false,
        DisarmOnStartup = false,
        AllowedAccount = "SIM101",
        AllowedInstruments = ["MES 06-26"],
        AllowedSignalSources = ["rust.strategy"],
        SignalReadTimeoutMs = 8000,
    };

        var logger = new ConsoleBridgeLogger();
        var bridge = new ExecutionBridge(config);
        bridge.Arm();
        var intake = new SignalIntakeTransport(config, bridge, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var intakeTask = intake.RunAsync(cts.Token);

        var rustDir = LocateRustServiceDirectory();
        using var rustProcess = StartRustService(rustDir, config.MarketDataPort, config.SignalPort, "MES 06-26");

        try
        {
            await Task.Delay(2500, cts.Token);
            await SendMarketDataBurstToRustAsync(config.MarketDataPort, "MES 06-26", cts.Token);
            await Task.Delay(1500, cts.Token);
        }
        finally
        {
            TryStopProcess(rustProcess);
            cts.Cancel();
            try
            {
                await intakeTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

    Console.WriteLine("[PHASE6] Signal intake Rust E2E completed.");
}

static Task RunPhase8SmokeAsync()
{
    var config = new BridgeConfig
    {
        LiveTradingEnabled = false,
        DisarmOnStartup = false,
        AllowedAccount = "SIM101",
        AllowedInstruments = ["MES 06-26"],
        AllowedSignalSources = ["test-host"],
        ProcessedSignalStorePath = "state/test-phase8-processed-ids.txt",
        SafetyStatePath = "state/test-phase8-safety-state.json",
        ExecutionJournalPath = "state/test-phase8-execution-journal.ndjson",
        ActualStateSnapshotPath = "state/test-phase8-actual-state.json",
    };

    var logger = new ConsoleBridgeLogger();
    var bridge = new ExecutionBridge(config, logger);
    bridge.Arm();

    var signal = new TradeSignal
    {
        MessageType = "TradeSignal",
        Version = "v1",
        Timestamp = DateTimeOffset.UtcNow,
        SourceId = "test-host",
        CorrelationId = Guid.NewGuid().ToString("N"),
        SignalId = Guid.NewGuid().ToString("N"),
        StrategyId = "phase8-smoke",
        Account = "SIM101",
        Instrument = "MES 06-26",
        Side = "Buy",
        Quantity = 1,
        OrderType = "Market",
        Reason = "phase8 lifecycle",
    };

    var ack = bridge.HandleSignal(signal);
    Console.WriteLine($"[PHASE8-ACK] {JsonSerializer.Serialize(ack)}");

    var orderId = bridge.LastOrderId ?? "SIM-UNKNOWN";
    bridge.OnOrderPartiallyFilled(orderId, 1, "Partial fill simulated");
    bridge.OnOrderFilled(orderId, 1, "Full fill simulated");
    bridge.OnOrderCanceled(orderId, 1, "Cancel simulated after fill for journal coverage");
    bridge.OnExecutionAmbiguity(orderId, "Connection dropped during execution callback stream");

    Console.WriteLine($"[PHASE8] Wrote journal to {config.ExecutionJournalPath}");
    Console.WriteLine($"[PHASE8] Wrote actual state to {config.ActualStateSnapshotPath}");
    return Task.CompletedTask;
}

static async Task RunPhase9SmokeAsync()
{
    var baseStateDir = Path.Combine("state", "test-phase9");
    Directory.CreateDirectory(baseStateDir);

    var config = new BridgeConfig
    {
        LiveTradingEnabled = false,
        DisarmOnStartup = false,
        AllowedAccount = "SIM101",
        AllowedInstruments = ["MES 06-26"],
        AllowedSignalSources = ["test-host"],
        ProcessedSignalStorePath = Path.Combine(baseStateDir, "processed-ids.txt"),
        SafetyStatePath = Path.Combine(baseStateDir, "safety-state.json"),
        ExecutionJournalPath = Path.Combine(baseStateDir, "execution-journal.ndjson"),
        ActualStateSnapshotPath = Path.Combine(baseStateDir, "actual-state.json"),
        ExpectedStateSnapshotPath = Path.Combine(baseStateDir, "expected-state.json"),
        ReconciliationReportPath = Path.Combine(baseStateDir, "reconciliation-report.json"),
    };

    var logger = new ConsoleBridgeLogger();

    // Bridge #1 writes expected+actual state from a normal accepted signal.
    var bridge1 = new ExecutionBridge(config, logger);
    bridge1.Arm();
    var signal = new TradeSignal
    {
        MessageType = "TradeSignal",
        Version = "v1",
        Timestamp = DateTimeOffset.UtcNow,
        SourceId = "test-host",
        CorrelationId = Guid.NewGuid().ToString("N"),
        SignalId = Guid.NewGuid().ToString("N"),
        StrategyId = "phase9-smoke",
        Account = "SIM101",
        Instrument = "MES 06-26",
        Side = "Buy",
        Quantity = 1,
        OrderType = "Market",
    };

    var ack = bridge1.HandleSignal(signal);
    Console.WriteLine($"[PHASE9-ACK] {JsonSerializer.Serialize(ack)}");

    // Force startup mismatch by deleting actual snapshot before bridge restart.
    if (File.Exists(config.ActualStateSnapshotPath))
    {
        File.Delete(config.ActualStateSnapshotPath);
    }

    var bridge2 = new ExecutionBridge(config, logger);
    bridge2.Arm();
    var startupReport = bridge2.RunStartupRecoveryCheck();
    Console.WriteLine($"[PHASE9-STARTUP] match={startupReport.IsMatch} mismatches={startupReport.Mismatches.Count} disarmed={bridge2.IsDisarmed}");

    // Explicit re-arm path after mismatch.
    bridge2.Arm();
    Console.WriteLine($"[PHASE9-REARM] disarmed={bridge2.IsDisarmed}");

    // Reconnect check should also run and report current state.
    var reconnectReport = bridge2.RunReconnectRecoveryCheck();
    Console.WriteLine($"[PHASE9-RECONNECT] match={reconnectReport.IsMatch} mismatches={reconnectReport.Mismatches.Count} disarmed={bridge2.IsDisarmed}");

    if (File.Exists(config.ReconciliationReportPath))
    {
        var reportJson = await File.ReadAllTextAsync(config.ReconciliationReportPath);
        Console.WriteLine($"[PHASE9-REPORT] {reportJson}");
    }
}

static async Task RunPhase10SmokeAsync()
{
    var baseStateDir = Path.Combine("state", "test-phase10");
    Directory.CreateDirectory(baseStateDir);

    var config = new BridgeConfig
    {
        LiveTradingEnabled = false,
        DisarmOnStartup = false,
        AllowedAccount = "SIM101",
        AllowedInstruments = ["MES 06-26"],
        AllowedSignalSources = ["test-host"],
        ProcessedSignalStorePath = Path.Combine(baseStateDir, "processed-ids.txt"),
        SafetyStatePath = Path.Combine(baseStateDir, "safety-state.json"),
        ExecutionJournalPath = Path.Combine(baseStateDir, "execution-journal.ndjson"),
        ActualStateSnapshotPath = Path.Combine(baseStateDir, "actual-state.json"),
        ExpectedStateSnapshotPath = Path.Combine(baseStateDir, "expected-state.json"),
        ReconciliationReportPath = Path.Combine(baseStateDir, "reconciliation-report.json"),
        RuntimeMarkersPath = Path.Combine(baseStateDir, "runtime-markers.ndjson"),
    };

    var logger = new ConsoleBridgeLogger();

    // Seed corruption in persistence file to verify fail-closed startup.
    await File.WriteAllTextAsync(config.SafetyStatePath, "{ bad-json");

    var bridge = new ExecutionBridge(config, logger);
    Console.WriteLine($"[PHASE10-START] disarmed={bridge.IsDisarmed}");

    var signal = new TradeSignal
    {
        MessageType = "TradeSignal",
        Version = "v1",
        Timestamp = DateTimeOffset.UtcNow,
        SourceId = "test-host",
        CorrelationId = Guid.NewGuid().ToString("N"),
        SignalId = Guid.NewGuid().ToString("N"),
        StrategyId = "phase10-smoke",
        Account = "SIM101",
        Instrument = "MES 06-26",
        Side = "Buy",
        Quantity = 1,
        OrderType = "Market",
    };

    var ackDisarmed = bridge.HandleSignal(signal);
    Console.WriteLine($"[PHASE10-ACK-DISARMED] {JsonSerializer.Serialize(ackDisarmed)}");

    // Explicit re-arm after operator intervention.
    bridge.Arm();
    var ackArmed = bridge.HandleSignal(new TradeSignal
    {
        MessageType = "TradeSignal",
        Version = "v1",
        Timestamp = DateTimeOffset.UtcNow,
        SourceId = "test-host",
        CorrelationId = Guid.NewGuid().ToString("N"),
        SignalId = Guid.NewGuid().ToString("N"),
        StrategyId = "phase10-smoke",
        Account = "SIM101",
        Instrument = "MES 06-26",
        Side = "Buy",
        Quantity = 1,
        OrderType = "Market",
    });
    Console.WriteLine($"[PHASE10-ACK-ARMED] {JsonSerializer.Serialize(ackArmed)}");

    bridge.Shutdown();

    var markerLines = await File.ReadAllLinesAsync(config.RuntimeMarkersPath);
    Console.WriteLine($"[PHASE10-MARKERS] count={markerLines.Length}");
}

static async Task RunMarketDataCaptureServerAsync(int port, CancellationToken cancellationToken, string prefix)
{
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();
    Console.WriteLine($"[HOST] Listening on 127.0.0.1:{port}");

    try
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        Console.WriteLine("[HOST] Publisher connected.");

        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            Console.WriteLine($"{prefix} {line}");
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("[HOST] Capture server cancellation reached.");
    }
    finally
    {
        listener.Stop();
    }
}

static async Task<string> RunSignalCaptureServerAsync(int signalPort, CancellationToken cancellationToken)
{
    var listener = new TcpListener(IPAddress.Loopback, signalPort);
    listener.Start();
    Console.WriteLine($"[RUST-E2E] Listening for TradeSignal on 127.0.0.1:{signalPort}");

    try
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }
    }
    finally
    {
        listener.Stop();
    }

    throw new TimeoutException("No signal received from Rust service.");
}

static async Task<string> SendSignalAndReadResponseAsync(string host, int port, string ndjsonPayload, CancellationToken cancellationToken)
{
    using var client = new TcpClient();
    await client.ConnectAsync(host, port, cancellationToken);
    await using var stream = client.GetStream();
    await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
    using var reader = new StreamReader(stream, Encoding.UTF8);

    await writer.WriteLineAsync(ndjsonPayload);
    var line = await reader.ReadLineAsync(cancellationToken);
    return line ?? string.Empty;
}

static Process StartRustService(string rustDir, int marketDataPort, int signalPort, string instrument)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "cargo",
        Arguments = "run",
        WorkingDirectory = rustDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    startInfo.Environment["MARKET_DATA_BIND"] = $"127.0.0.1:{marketDataPort}";
    startInfo.Environment["SIGNAL_BIND"] = $"127.0.0.1:{signalPort}";
    startInfo.Environment["ALLOWED_ACCOUNT"] = "SIM101";
    startInfo.Environment["ALLOWED_INSTRUMENTS"] = instrument;

    var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Rust strategy service process.");

    _ = Task.Run(async () =>
    {
        while (!process.HasExited)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            Console.WriteLine($"[RUST] {line}");
        }
    });

    _ = Task.Run(async () =>
    {
        while (!process.HasExited)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            Console.WriteLine($"[RUST-ERR] {line}");
        }
    });

    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        TryStopProcess(process);
    };

    return process;
}

static async Task SendMarketDataBurstToRustAsync(int marketDataPort, string instrument, CancellationToken cancellationToken)
{
    using var client = new TcpClient();

    var connected = false;
    for (var attempt = 1; attempt <= 30 && !connected; attempt++)
    {
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, marketDataPort, cancellationToken);
            connected = true;
        }
        catch (SocketException) when (attempt < 30)
        {
            await Task.Delay(250, cancellationToken);
        }
    }

    if (!connected)
    {
        throw new TimeoutException($"Could not connect to Rust market-data endpoint on port {marketDataPort}.");
    }

    await using var stream = client.GetStream();
    await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

    // First frame seeds quote; second frame sets lastPrice above ask to trigger deterministic Buy signal.
    var quoteOnly = new
    {
        messageType = "MarketDataMessage",
        version = "v1",
        timestamp = DateTimeOffset.UtcNow,
        sourceId = "test-host",
        correlationId = Guid.NewGuid().ToString("N"),
        instrument,
        eventType = "QuoteUpdate",
        bid = 5000.0,
        ask = 5000.25,
    };

    var trigger = new
    {
        messageType = "MarketDataMessage",
        version = "v1",
        timestamp = DateTimeOffset.UtcNow,
        sourceId = "test-host",
        correlationId = Guid.NewGuid().ToString("N"),
        instrument,
        eventType = "TradePrint",
        bid = 5000.0,
        ask = 5000.25,
        lastPrice = 5000.5,
        lastSize = 1,
    };

    await writer.WriteLineAsync(JsonSerializer.Serialize(quoteOnly));
    await writer.WriteLineAsync(JsonSerializer.Serialize(trigger));
}

static string LocateRustServiceDirectory()
{
    var current = Directory.GetCurrentDirectory();
    for (var i = 0; i < 6; i++)
    {
        var candidate = Path.Combine(current, "src", "rust", "strategy-service");
        if (File.Exists(Path.Combine(candidate, "Cargo.toml")))
        {
            return candidate;
        }

        var parent = Directory.GetParent(current);
        if (parent is null)
        {
            break;
        }

        current = parent.FullName;
    }

    throw new DirectoryNotFoundException("Could not locate src/rust/strategy-service from current directory.");
}

static void TryStopProcess(Process process)
{
    try
    {
        if (process.HasExited)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        process.WaitForExit(2000);
    }
    catch
    {
        // best effort cleanup
    }
}

static void RunExecutionBridgeSmokeCheck(BridgeConfig config)
{
    var bridge = new ExecutionBridge(config);
    bridge.Arm();

    var signal = new TradeSignal
    {
        MessageType = "TradeSignal",
        Version = "v1",
        Timestamp = DateTimeOffset.UtcNow,
        SourceId = "test-host",
        CorrelationId = Guid.NewGuid().ToString("N"),
        SignalId = Guid.NewGuid().ToString("N"),
        StrategyId = "smoke",
        Account = "SIM101",
        Instrument = "MES 06-26",
        Side = "Buy",
        Quantity = 1,
        OrderType = "Market",
        Reason = "smoke-check",
    };

    var ack = bridge.HandleSignal(signal);
    Console.WriteLine($"[ACK] status={ack.Status} detail={ack.Detail}");
}
