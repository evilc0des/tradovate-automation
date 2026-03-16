#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTraderTradovateBridge;
#endregion

// Copy this file into: Documents\NinjaTrader 8\bin\Custom\NinjaScript\Strategies\
namespace NinjaTrader.NinjaScript.Strategies
{
    public class BridgeRunnerStrategy : Strategy
    {
        private BridgeConfig _config;
        private FileBridgeLogger _fileLogger;
        private ExecutionBridge _bridge;
        private SignalIntakeTransport _signalIntake;
        private NdjsonTcpMarketDataTransport _marketDataTransport;
        private MarketDataPublisher _marketDataPublisher;
        private NinjaTraderEventAdapter _eventAdapter;
        private CancellationTokenSource _bridgeCts;
        private Task _signalIntakeTask;
        private readonly ConcurrentQueue<TradeSignal> _pendingNativeSignals = new ConcurrentQueue<TradeSignal>();

        [NinjaScriptProperty]
        public string SignalHost { get; set; }

        [NinjaScriptProperty]
        public int SignalPort { get; set; }

        [NinjaScriptProperty]
        public string MarketDataHost { get; set; }

        [NinjaScriptProperty]
        public int MarketDataPort { get; set; }

        [NinjaScriptProperty]
        public bool ArmOnStartup { get; set; }

        [NinjaScriptProperty]
        public bool NativeOrderSubmission { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BridgeRunnerStrategy";
                Description = "Runs NinjaTrader Tradovate bridge plumbing in strategy scope.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                IsUnmanaged = false;

                SignalHost = "127.0.0.1";
                SignalPort = 19201;
                MarketDataHost = "127.0.0.1";
                MarketDataPort = 19200;
                ArmOnStartup = false;
                NativeOrderSubmission = false;
            }
            else if (State == State.DataLoaded)
            {
                _config = new BridgeConfig
                {
                    SignalHost = SignalHost,
                    SignalPort = SignalPort,
                    MarketDataHost = MarketDataHost,
                    MarketDataPort = MarketDataPort,
                    LiveTradingEnabled = false,
                    DisarmOnStartup = true,
                    AllowedAccount = "SIM101",
                    AllowedInstruments = new[] { Instrument.FullName },
                    AllowedSignalSources = new[] { "rust.strategy" },
                };

                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "logs", "bridge");
                _fileLogger = new FileBridgeLogger(logDir);
                IBridgeLogger logger = _fileLogger;
                IOrderSubmissionGateway orderGateway = NativeOrderSubmission
                    ? new NinjaTraderQueuedOrderSubmissionGateway(logger, EnqueueNativeOrder)
                    : new SimulatedOrderSubmissionGateway();

                _bridge = new ExecutionBridge(_config, logger, orderGateway);
                if (ArmOnStartup)
                {
                    _bridge.Arm();
                }

                _signalIntake = new SignalIntakeTransport(_config, _bridge, logger);
                _marketDataTransport = new NdjsonTcpMarketDataTransport(_config, logger);
                _marketDataPublisher = new MarketDataPublisher(_config, _marketDataTransport, logger);
                _eventAdapter = new NinjaTraderEventAdapter(_marketDataPublisher);

                _bridgeCts = new CancellationTokenSource();
                _signalIntakeTask = _signalIntake.RunAsync(_bridgeCts.Token);
                _marketDataPublisher.OnStarted();
            }
            else if (State == State.Terminated)
            {
                try
                {
                    if (_marketDataPublisher != null)
                    {
                        _marketDataPublisher.OnStopped();
                    }

                    if (_bridgeCts != null)
                    {
                        _bridgeCts.Cancel();
                    }

                    if (_signalIntakeTask != null)
                    {
                        _signalIntakeTask.Wait(TimeSpan.FromSeconds(2));
                    }

                    if (_marketDataTransport != null)
                    {
                        _marketDataTransport.Dispose();
                    }

                    if (_bridge != null)
                    {
                        _bridge.Shutdown();
                    }

                    _fileLogger?.Dispose();
                    _fileLogger = null;
                }
                catch
                {
                    // Best effort shutdown in NinjaTrader script lifecycle.
                    _fileLogger?.Dispose();
                    _fileLogger = null;
                }
            }
        }

        // Called once per bar close when Calculate = Calculate.OnEachTick.
        // IsFirstTickOfBar becomes true on the first tick after the previous bar sealed,
        // so we publish index [1] which is the just-completed bar.
        protected override void OnBarUpdate()
        {
            if (_eventAdapter == null || _bridgeCts == null)
                return;
            if (BarsInProgress != 0)
                return; // primary series only
            if (CurrentBar < 1)
                return; // need at least one fully closed bar
            if (!IsFirstTickOfBar)
                return; // fire once per bar close, not on every tick

            var ts = DateTimeOffset.UtcNow;
            var barTime = new DateTimeOffset(Time[1].ToUniversalTime(), TimeSpan.Zero);
            var interval = GetIntervalString(BarsPeriod);

            _ = _eventAdapter.OnBarAsync(
                ts,
                Instrument.FullName,
                barTime,
                interval,
                Open[1],
                High[1],
                Low[1],
                Close[1],
                (long)Volume[1],
                _bridgeCts.Token);
        }

        private static string GetIntervalString(NinjaTrader.Data.BarsPeriod period)
        {
            return period.BarsPeriodType switch
            {
                NinjaTrader.Data.BarsPeriodType.Second => $"{period.Value}s",
                NinjaTrader.Data.BarsPeriodType.Minute when period.Value % 60 == 0 => $"{period.Value / 60}h",
                NinjaTrader.Data.BarsPeriodType.Minute => $"{period.Value}m",
                NinjaTrader.Data.BarsPeriodType.Day    => "1d",
                _ => $"{period.Value}u",
            };
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (_eventAdapter == null || _bridgeCts == null)
            {
                return;
            }

            DrainPendingNativeOrders();

            var ts = DateTimeOffset.UtcNow;
            if (marketDataUpdate.MarketDataType == MarketDataType.Last)
            {
                _ = _eventAdapter.OnTradePrintAsync(
                    ts,
                    Instrument.FullName,
                    marketDataUpdate.Price,
                    (int)marketDataUpdate.Volume,
                    "Unknown",
                    _bridgeCts.Token);
            }
            else if (marketDataUpdate.MarketDataType == MarketDataType.Bid)
            {
                _ = _eventAdapter.OnQuoteAsync(
                    ts,
                    Instrument.FullName,
                    marketDataUpdate.Price,
                    GetCurrentAsk(),
                    1,
                    1,
                    _bridgeCts.Token);
            }
            else if (marketDataUpdate.MarketDataType == MarketDataType.Ask)
            {
                _ = _eventAdapter.OnQuoteAsync(
                    ts,
                    Instrument.FullName,
                    GetCurrentBid(),
                    marketDataUpdate.Price,
                    1,
                    1,
                    _bridgeCts.Token);
            }
        }

        private void EnqueueNativeOrder(TradeSignal signal)
        {
            _pendingNativeSignals.Enqueue(signal);
        }

        private void DrainPendingNativeOrders()
        {
            while (_pendingNativeSignals.TryDequeue(out var signal))
            {
                try
                {
                    var signalName = $"bridge-{signal.SignalId}";
                    var instruction = signal.Instruction ?? "entry";

                    if (string.Equals(instruction, "flatten", StringComparison.OrdinalIgnoreCase))
                    {
                        // Close all open positions for this instrument (qty 0 = all in NT8 managed).
                        ExitLong(0, signalName, "");
                        ExitShort(0, signalName, "");
                    }
                    else if (string.Equals(signal.Side, "Buy", StringComparison.OrdinalIgnoreCase))
                    {
                        EnterLong((int)signal.Quantity, signalName);
                    }
                    else if (string.Equals(signal.Side, "Sell", StringComparison.OrdinalIgnoreCase))
                    {
                        EnterShort((int)signal.Quantity, signalName);
                    }
                    else
                    {
                        Print($"[BridgeRunnerStrategy] Unsupported side={signal.Side} signalId={signal.SignalId}");
                    }
                }
                catch (Exception ex)
                {
                    Print($"[BridgeRunnerStrategy] Native submit failed signalId={signal.SignalId} error={ex.Message}");
                }
            }
        }

        private sealed class NinjaTraderQueuedOrderSubmissionGateway : IOrderSubmissionGateway
        {
            private readonly IBridgeLogger _logger;
            private readonly Action<TradeSignal> _enqueue;

            public NinjaTraderQueuedOrderSubmissionGateway(IBridgeLogger logger, Action<TradeSignal> enqueue)
            {
                _logger = logger;
                _enqueue = enqueue;
            }

            public OrderSubmissionResult SubmitMarketOrder(TradeSignal signal)
            {
                _enqueue(signal);
                var orderId = $"NT-PENDING-{Guid.NewGuid():N}";
                _logger.Info($"Queued native NinjaTrader order signalId={signal.SignalId} side={signal.Side} qty={signal.Quantity} instrument={signal.Instrument}");
                return new OrderSubmissionResult
                {
                    Accepted = true,
                    OrderId = orderId,
                    Detail = "Queued for NinjaTrader native submission",
                    SignalIdTag = signal.SignalId,
                    CorrelationIdTag = signal.CorrelationId,
                };
            }
        }
    }
}
