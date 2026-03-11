#region Using declarations
using System;
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
        private ExecutionBridge _bridge;
        private SignalIntakeTransport _signalIntake;
        private NdjsonTcpMarketDataTransport _marketDataTransport;
        private MarketDataPublisher _marketDataPublisher;
        private NinjaTraderEventAdapter _eventAdapter;
        private CancellationTokenSource _bridgeCts;
        private Task _signalIntakeTask;

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

                _bridge = new ExecutionBridge(_config, new ConsoleBridgeLogger());
                if (ArmOnStartup)
                {
                    _bridge.Arm();
                }

                _signalIntake = new SignalIntakeTransport(_config, _bridge, new ConsoleBridgeLogger());
                _marketDataTransport = new NdjsonTcpMarketDataTransport(_config, new ConsoleBridgeLogger());
                _marketDataPublisher = new MarketDataPublisher(_config, _marketDataTransport, new ConsoleBridgeLogger());
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
                }
                catch
                {
                    // Best effort shutdown in NinjaTrader script lifecycle.
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (_eventAdapter == null || _bridgeCts == null)
            {
                return;
            }

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
    }
}
