using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.AI
{
    public class AIConsoleViewModel
    {
        public ObservableCollection<AITradingSignalRecord> Signals { get; } = new();
        public ObservableCollection<TradingEventRecord> TradingEvents { get; } = new();
        public ObservableCollection<GateRejectionRecord> GateRejections { get; } = new();
        public ObservableCollection<StrategyChangeRecord> StrategyChanges { get; } = new();
        public ObservableCollection<ParameterAdjustmentRecord> ParameterAdjustments { get; } = new();

        private AITradingSignalRecord? _selectedSignal;
        public AITradingSignalRecord? SelectedSignal
        {
            get => _selectedSignal;
            set { _selectedSignal = value; }
        }

        public AIConsoleViewModel()
        {
            Task.Run(() => AttachToEventBusIfAvailable());
        }

        private void AttachToEventBusIfAvailable()
        {
            try
            {
                // Locate EventBus via reflection to avoid compile-time coupling
                object? eventBus = null;
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var slType = assemblies.SelectMany(a => a.GetTypesSafe()).FirstOrDefault(t => t.Name == "ServiceLocator");
                if (slType != null)
                {
                    var getAic = slType.GetMethod("GetAICentralCoordinator", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (getAic != null)
                    {
                        var coordinator = getAic.Invoke(null, null);
                        if (coordinator != null)
                        {
                            var evProp = coordinator.GetType().GetProperty("EventBus");
                            if (evProp != null)
                            {
                                eventBus = evProp.GetValue(coordinator);
                            }
                        }
                    }

                    if (eventBus == null)
                    {
                        var evPropStatic = slType.GetProperty("EventBus", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (evPropStatic != null)
                        {
                            eventBus = evPropStatic.GetValue(null);
                        }
                    }
                }

                if (eventBus == null)
                {
                    // try finding a public type named AICentralCoordinator
                    var coordType = assemblies.SelectMany(a => a.GetTypesSafe()).FirstOrDefault(t => t.Name == "AICentralCoordinator");
                    if (coordType != null)
                    {
                        var instances = coordType.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        // fallback: no instance
                    }
                }

                if (eventBus is object eb)
                {
                    // subscribe via reflection to avoid generic type issues
                    var subscribeMethod = eb.GetType().GetMethod("Subscribe");
                    if (subscribeMethod != null)
                    {
                        // Use the strongly-typed subscriptions available in code where possible by casting
                        if (eb is 币安量化机器人.Services.AI.EventBus typedEb)
                        {
                            typedEb.Subscribe<AITradingSignalGeneratedEvent>(async e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    Signals.Insert(0, new AITradingSignalRecord(e.Signal));
                                    if (Signals.Count > 500) { Signals.RemoveAt(Signals.Count - 1); }
                                });
                                await Task.CompletedTask;
                            });

                            typedEb.Subscribe<AITradingSignalExecutedEvent>(async e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    TradingEvents.Insert(0, new TradingEventRecord(e.Signal, e.Result));
                                    if (TradingEvents.Count > 500) { TradingEvents.RemoveAt(TradingEvents.Count - 1); }
                                });
                                await Task.CompletedTask;
                            });

                            typedEb.Subscribe<TradeGateRejectionEvent>(async e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    GateRejections.Insert(0, new GateRejectionRecord { Reason = e.Reason, Timestamp = e.Timestamp });
                                    if (GateRejections.Count > 200) { GateRejections.RemoveAt(GateRejections.Count - 1); }
                                });
                                await Task.CompletedTask;
                            });

                            typedEb.Subscribe<StrategyChangedEvent>(async e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    StrategyChanges.Insert(0, new StrategyChangeRecord { Summary = e.Summary, Timestamp = e.Timestamp });
                                    if (StrategyChanges.Count > 200) { StrategyChanges.RemoveAt(StrategyChanges.Count - 1); }
                                });
                                await Task.CompletedTask;
                            });

                            typedEb.Subscribe<ParameterAdjustedEvent>(async e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    ParameterAdjustments.Insert(0, new ParameterAdjustmentRecord { Summary = e.Summary, Timestamp = e.Timestamp });
                                    if (ParameterAdjustments.Count > 200) { ParameterAdjustments.RemoveAt(ParameterAdjustments.Count - 1); }
                                });
                                await Task.CompletedTask;
                            });

                            typedEb.Subscribe<MarketFundingRateUpdateEvent>(e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    TradingEvents.Insert(0, new TradingEventRecord { Summary = $"Funding {e.Symbol}: {e.FundingRate:P6}", Timestamp = e.Timestamp == default ? DateTime.UtcNow : e.Timestamp });
                                    if (TradingEvents.Count > 500) { TradingEvents.RemoveAt(TradingEvents.Count - 1); }
                                });
                                return Task.CompletedTask;
                            });

                            typedEb.Subscribe<MarketOpenInterestUpdateEvent>(e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    TradingEvents.Insert(0, new TradingEventRecord { Summary = $"OI {e.Symbol}: {e.OpenInterest}", Timestamp = e.Timestamp == default ? DateTime.UtcNow : e.Timestamp });
                                    if (TradingEvents.Count > 500) { TradingEvents.RemoveAt(TradingEvents.Count - 1); }
                                });
                                return Task.CompletedTask;
                            });

                            typedEb.Subscribe<MarketLongShortRatioUpdateEvent>(e =>
                            {
                                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                {
                                    TradingEvents.Insert(0, new TradingEventRecord { Summary = $"LSR {e.Symbol}: {e.LongShortRatio:F2}", Timestamp = e.Timestamp == default ? DateTime.UtcNow : e.Timestamp });
                                    if (TradingEvents.Count > 500) { TradingEvents.RemoveAt(TradingEvents.Count - 1); }
                                });
                                return Task.CompletedTask;
                            });
                        }
                    }
                }
            }
            catch
            {
                // ignore subscription failures
            }
        }
    }

    // Top-level extension moved here to satisfy compiler requirements
    public static class AssemblyExtensions
    {
        public static Type[] GetTypesSafe(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
