using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BMDSwitcherAPI;
using BarRaider.SdTools;

namespace ATEM_StreamDeck
{
    public class ATEMConnectionManager
    {
        private static readonly Lazy<ATEMConnectionManager> _instance = new Lazy<ATEMConnectionManager>(() => new ATEMConnectionManager());
        public static ATEMConnectionManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, ATEMConnection> _connections = new ConcurrentDictionary<string, ATEMConnection>();
        private readonly Timer _connectionMonitorTimer;

        // Framerate caching
        private readonly ConcurrentDictionary<string, double> _switcherFramerates = new ConcurrentDictionary<string, double>();

        // ATEM Capabilities caching
        private readonly ConcurrentDictionary<string, ATEMSwitcherInfo> _switcherCapabilities = new ConcurrentDictionary<string, ATEMSwitcherInfo>();

        // Global state tracking
        private readonly ConcurrentDictionary<string, ATEMSwitcherState> _switcherStates = new ConcurrentDictionary<string, ATEMSwitcherState>();

        // Events for global state changes
        public event EventHandler<ATEMStateChangeEventArgs> StateChanged;

        private ATEMConnectionManager()
        {
            // Monitor connections every 5 seconds
            _connectionMonitorTimer = new Timer(MonitorConnections, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public ATEMConnection GetConnection(string ipAddress)
        {
            return _connections.GetOrAdd(ipAddress, ip => new ATEMConnection(ip));
        }

        public void ReleaseConnection(string ipAddress)
        {
            if (_connections.TryRemove(ipAddress, out var connection))
            {
                connection.Dispose();
                _switcherFramerates.TryRemove(ipAddress, out _);
                _switcherStates.TryRemove(ipAddress, out _);
                _switcherCapabilities.TryRemove(ipAddress, out _);
            }
        }

        public double GetSwitcherFramerate(string ipAddress)
        {
            // Return cached framerate or default if not available
            if (_switcherFramerates.TryGetValue(ipAddress, out double framerate))
            {
                return framerate;
            }
            return ATEMConstants.DEFAULT_FRAMERATE;
        }

        internal void SetSwitcherFramerate(string ipAddress, double framerate)
        {
            _switcherFramerates[ipAddress] = framerate;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Cached framerate for {ipAddress}: {framerate} fps");
        }

        public ATEMSwitcherInfo GetSwitcherInfo(string ipAddress)
        {
            return _switcherCapabilities.GetOrAdd(ipAddress, ip => new ATEMSwitcherInfo(ip));
        }

        internal void SetSwitcherInfo(string ipAddress, ATEMSwitcherInfo info)
        {
            _switcherCapabilities[ipAddress] = info;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Cached ATEM capabilities for {ipAddress}: {info.MixEffectCount} ME blocks, {info.InputCount} inputs");
        }

        public ATEMSwitcherState GetSwitcherState(string ipAddress)
        {
            return _switcherStates.GetOrAdd(ipAddress, ip => new ATEMSwitcherState(ip));
        }

        internal void OnStateChanged(string ipAddress, ATEMStateChangeEventArgs args)
        {
            StateChanged?.Invoke(this, args);
        }

        private void MonitorConnections(object state)
        {
            var connectionsToRemove = new List<string>();

            foreach (var kvp in _connections)
            {
                var connection = kvp.Value;
                if (!connection.IsConnected && connection.LastActivity < DateTime.Now.AddMinutes(-5))
                {
                    // Remove inactive connections after 5 minutes
                    connectionsToRemove.Add(kvp.Key);
                }
                else if (!connection.IsConnected)
                {
                    // Try to reconnect
                    Task.Run(() => connection.TryReconnect());
                }
            }

            foreach (var ip in connectionsToRemove)
            {
                ReleaseConnection(ip);
            }
        }

        public void Dispose()
        {
            _connectionMonitorTimer?.Dispose();
            foreach (var connection in _connections.Values)
            {
                connection.Dispose();
            }
            _connections.Clear();
            _switcherFramerates.Clear();
            _switcherStates.Clear();
            _switcherCapabilities.Clear();
        }
    }

    // ATEM Switcher capabilities info
    public class ATEMSwitcherInfo
    {
        public string IPAddress { get; }
        public int MixEffectCount { get; set; }
        public int InputCount { get; set; }
        public List<ATEMInputInfo> Inputs { get; set; }
        public DateTime LastUpdated { get; set; }

        public ATEMSwitcherInfo(string ipAddress)
        {
            IPAddress = ipAddress;
            MixEffectCount = 1; // Default fallback
            InputCount = 0;
            Inputs = new List<ATEMInputInfo>();
            LastUpdated = DateTime.MinValue;
        }
    }

    // ATEM Input info
    public class ATEMInputInfo
    {
        public long InputId { get; set; }
        public string ShortName { get; set; }
        public string LongName { get; set; }

        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(LongName) && LongName != ShortName)
                return $"{InputId}: {LongName} ({ShortName})";
            else if (!string.IsNullOrEmpty(ShortName))
                return $"{InputId}: {ShortName}";
            else
                return $"Input {InputId}";
        }
    }

    // State change event arguments
    public class ATEMStateChangeEventArgs : EventArgs
    {
        public string IPAddress { get; }
        public int MixEffectIndex { get; }
        public ATEMEventType EventType { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }

        public ATEMStateChangeEventArgs(string ipAddress, int mixEffectIndex)
        {
            IPAddress = ipAddress;
            MixEffectIndex = mixEffectIndex;
        }
    }

    // Event types for ATEM state changes
    public enum ATEMEventType
    {
        TransitionStateChanged,
        TransitionPositionChanged,
        ProgramInputChanged,
        PreviewInputChanged
    }

    // Mix Effect state container
    public class ATEMMixEffectState
    {
        public int Index { get; }
        public bool IsInTransition { get; private set; }
        public double TransitionPosition { get; private set; }
        public long ProgramInput { get; private set; }
        public long PreviewInput { get; private set; }

        public ATEMMixEffectState(int index)
        {
            Index = index;
            IsInTransition = false;
            TransitionPosition = 0.0;
            ProgramInput = 0;
            PreviewInput = 0;
        }

        public void UpdateTransitionState(bool inTransition, double position)
        {
            IsInTransition = inTransition;
            TransitionPosition = position;
        }

        public void UpdateInputs(long program, long preview)
        {
            ProgramInput = program;
            PreviewInput = preview;
        }
    }

    // Global state container for a switcher
    public class ATEMSwitcherState
    {
        public string IPAddress { get; }
        public ConcurrentDictionary<int, ATEMMixEffectState> MixEffectStates { get; }

        public ATEMSwitcherState(string ipAddress)
        {
            IPAddress = ipAddress;
            MixEffectStates = new ConcurrentDictionary<int, ATEMMixEffectState>();
        }

        public ATEMMixEffectState GetMixEffectState(int meIndex)
        {
            return MixEffectStates.GetOrAdd(meIndex, index => new ATEMMixEffectState(index));
        }
    }

    // ATEM Mix Effect callback implementation
    public class ATEMMixEffectCallback : IBMDSwitcherMixEffectBlockCallback
    {
        private readonly string _ipAddress;
        private readonly int _mixEffectIndex;
        private readonly ATEMSwitcherState _switcherState;
        private readonly IBMDSwitcherMixEffectBlock _mixEffectBlock;

        public ATEMMixEffectCallback(string ipAddress, int mixEffectIndex, ATEMSwitcherState switcherState, IBMDSwitcherMixEffectBlock mixEffectBlock)
        {
            _ipAddress = ipAddress;
            _mixEffectIndex = mixEffectIndex;
            _switcherState = switcherState;
            _mixEffectBlock = mixEffectBlock;
        }

        public void Notify(_BMDSwitcherMixEffectBlockEventType eventType)
        {
            try
            {
                var meState = _switcherState?.GetMixEffectState(_mixEffectIndex);
                var eventArgs = new ATEMStateChangeEventArgs(_ipAddress, _mixEffectIndex);

                switch (eventType)
                {
                    case _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypeInTransitionChanged:
                        if (_mixEffectBlock != null && meState != null)
                        {
                            _mixEffectBlock.GetInTransition(out int inTransition);
                            bool wasInTransition = meState.IsInTransition;
                            bool isInTransition = inTransition != 0;

                            meState.UpdateTransitionState(isInTransition, meState.TransitionPosition);

                            eventArgs.EventType = ATEMEventType.TransitionStateChanged;
                            eventArgs.OldValue = wasInTransition;
                            eventArgs.NewValue = isInTransition;

                            Logger.Instance.LogMessage(TracingLevel.INFO,
                                $"ME {_mixEffectIndex} transition state changed: {(isInTransition ? "IN TRANSITION" : "IDLE")}");
                        }
                        break;

                    case _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypeTransitionPositionChanged:
                        if (_mixEffectBlock != null && meState != null)
                        {
                            _mixEffectBlock.GetTransitionPosition(out double position);
                            double oldPosition = meState.TransitionPosition;

                            meState.UpdateTransitionState(meState.IsInTransition, position);

                            eventArgs.EventType = ATEMEventType.TransitionPositionChanged;
                            eventArgs.OldValue = oldPosition;
                            eventArgs.NewValue = position;
                        }
                        break;

                    case _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypeProgramInputChanged:
                        if (_mixEffectBlock != null && meState != null)
                        {
                            _mixEffectBlock.GetProgramInput(out long programInput);
                            long oldProgram = meState.ProgramInput;

                            meState.UpdateInputs(programInput, meState.PreviewInput);

                            eventArgs.EventType = ATEMEventType.ProgramInputChanged;
                            eventArgs.OldValue = oldProgram;
                            eventArgs.NewValue = programInput;

                            Logger.Instance.LogMessage(TracingLevel.INFO, $"ME {_mixEffectIndex} program input changed: {oldProgram} -> {programInput}");
                        }
                        break;

                    case _BMDSwitcherMixEffectBlockEventType.bmdSwitcherMixEffectBlockEventTypePreviewInputChanged:
                        if (_mixEffectBlock != null && meState != null)
                        {
                            _mixEffectBlock.GetPreviewInput(out long previewInput);
                            long oldPreview = meState.PreviewInput;

                            meState.UpdateInputs(meState.ProgramInput, previewInput);

                            eventArgs.EventType = ATEMEventType.PreviewInputChanged;
                            eventArgs.OldValue = oldPreview;
                            eventArgs.NewValue = previewInput;

                            Logger.Instance.LogMessage(TracingLevel.INFO, $"ME {_mixEffectIndex} preview input changed: {oldPreview} -> {previewInput}");
                        }
                        break;

                    default:
                        // Don't process unknown event types
                        return;
                }

                // Notify global state change (only if we have connection manager context)
                if (!string.IsNullOrEmpty(_ipAddress))
                {
                    ATEMConnectionManager.Instance.OnStateChanged(_ipAddress, eventArgs);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in MixEffect callback for ME {_mixEffectIndex}: {ex}");
            }
        }
    }

    public class ATEMConnection : IDisposable
    {
        private readonly string _ipAddress;
        private IBMDSwitcher _switcher;
        private IBMDSwitcherDiscovery _discovery;
        private readonly object _lockObject = new object();
        private volatile bool _isConnected;
        private DateTime _lastActivity;
        private int _retryCount;
        private const int MaxRetries = 3;

        // Callback management
        private readonly List<(ATEMMixEffectCallback callback, IBMDSwitcherMixEffectBlock meBlock)> _mixEffectCallbacks =
            new List<(ATEMMixEffectCallback callback, IBMDSwitcherMixEffectBlock meBlock)>();

        // Events for status updates
        public event EventHandler<ATEMStatusEventArgs> StatusChanged;
        public event EventHandler<bool> ConnectionStateChanged;

        public string IPAddress => _ipAddress;
        public bool IsConnected => _isConnected;
        public DateTime LastActivity => _lastActivity;
        public IBMDSwitcher Switcher => _switcher;

        public ATEMConnection(string ipAddress)
        {
            _ipAddress = ipAddress;
            _lastActivity = DateTime.Now;
            _discovery = new CBMDSwitcherDiscovery();

            Task.Run(() => TryConnect());
        }

        public Task<bool> TryConnect()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_isConnected)
                        return Task.FromResult(true);

                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Attempting to connect to ATEM at {_ipAddress}");

                    _BMDSwitcherConnectToFailure failureReason;
                    _discovery.ConnectTo(_ipAddress, out _switcher, out failureReason);

                    if (_switcher != null)
                    {
                        _isConnected = true;
                        _retryCount = 0;
                        _lastActivity = DateTime.Now;

                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Successfully connected to ATEM at {_ipAddress}");

                        // Cache framerate for this switcher
                        CacheFramerate();

                        // Cache ATEM capabilities
                        CacheATEMCapabilities();

                        // Setup event handlers for status monitoring
                        SetupStatusMonitoring();

                        ConnectionStateChanged?.Invoke(this, true);
                        return Task.FromResult(true);
                    }
                    else
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to connect to ATEM at {_ipAddress}. Reason: {failureReason}");
                        _isConnected = false;
                        ConnectionStateChanged?.Invoke(this, false);
                        return Task.FromResult(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Exception connecting to ATEM at {_ipAddress}: {ex.Message}");
                    _isConnected = false;
                    ConnectionStateChanged?.Invoke(this, false);
                    return Task.FromResult(false);
                }
            }
        }

        private void CacheFramerate()
        {
            try
            {
                if (_switcher != null)
                {
                    _switcher.GetVideoMode(out _BMDSwitcherVideoMode videoMode);
                    double framerate = GetFramerateFromVideoMode(videoMode);
                    ATEMConnectionManager.Instance.SetSwitcherFramerate(_ipAddress, framerate);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error caching framerate: {ex}");
                // Use default framerate
                ATEMConnectionManager.Instance.SetSwitcherFramerate(_ipAddress, ATEMConstants.DEFAULT_FRAMERATE);
            }
        }

        private void CacheATEMCapabilities()
        {
            try
            {
                if (_switcher == null) return;

                var switcherInfo = new ATEMSwitcherInfo(_ipAddress);

                // Get Mix Effect count
                IntPtr meIteratorPtr;
                _switcher.CreateIterator(typeof(IBMDSwitcherMixEffectBlockIterator).GUID, out meIteratorPtr);
                IBMDSwitcherMixEffectBlockIterator meIterator = Marshal.GetObjectForIUnknown(meIteratorPtr) as IBMDSwitcherMixEffectBlockIterator;
                if (meIterator != null)
                {
                    try
                    {
                        int meCount = 0;
                        while (true)
                        {
                            meIterator.Next(out IBMDSwitcherMixEffectBlock meBlock);
                            if (meBlock == null) break;
                            
                            meCount++;
                            // Release each mix effect block after counting
                            Marshal.ReleaseComObject(meBlock);
                        }
                        switcherInfo.MixEffectCount = Math.Max(1, meCount);
                    }
                    finally
                    {
                        // Always release the iterator
                        Marshal.ReleaseComObject(meIterator);
                    }
                }

                // Get Input information
                IntPtr inputIteratorPtr;
                _switcher.CreateIterator(typeof(IBMDSwitcherInputIterator).GUID, out inputIteratorPtr);
                IBMDSwitcherInputIterator inputIterator = Marshal.GetObjectForIUnknown(inputIteratorPtr) as IBMDSwitcherInputIterator;
                if (inputIterator != null)
                {
                    try
                    {
                        var inputs = new List<ATEMInputInfo>();
                        while (true)
                        {
                            inputIterator.Next(out IBMDSwitcherInput input);
                            if (input == null) break;

                            try
                            {
                                input.GetInputId(out long inputId);
                                input.GetShortName(out string shortName);
                                input.GetLongName(out string longName);
                                
                                inputs.Add(new ATEMInputInfo
                                {
                                    InputId = inputId,
                                    ShortName = shortName ?? "",
                                    LongName = longName ?? ""
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Instance.LogMessage(TracingLevel.WARN, $"Error reading input info: {ex.Message}");
                            }
                            finally
                            {
                                // Release each input object after reading
                                Marshal.ReleaseComObject(input);
                            }
                        }

                        switcherInfo.Inputs = inputs.OrderBy(i => i.InputId).ToList();
                        switcherInfo.InputCount = inputs.Count;
                    }
                    finally
                    {
                        // Always release the iterator
                        Marshal.ReleaseComObject(inputIterator);
                    }
                }

                switcherInfo.LastUpdated = DateTime.Now;
                ATEMConnectionManager.Instance.SetSwitcherInfo(_ipAddress, switcherInfo);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error caching ATEM capabilities: {ex}");
            }
        }

        private double GetFramerateFromVideoMode(_BMDSwitcherVideoMode videoMode)
        {
            switch (videoMode)
            {
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode525i5994NTSC:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode625i50PAL:
                    return 25.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode720p50:
                    return 50.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode720p5994:
                    return 59.94;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080i50:
                    return 25.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080i5994:
                    return 29.97;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p2398:
                    return 23.98;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p24:
                    return 24.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p25:
                    return 25.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p2997:
                    return 29.97;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p30:
                    return 30.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p50:
                    return 50.0;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p5994:
                    return 59.94;
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p60:
                    return 60.0;
                default:
                    return ATEMConstants.DEFAULT_FRAMERATE;
            }
        }

        public async Task<bool> TryReconnect()
        {
            if (_retryCount >= MaxRetries)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Max retries reached for ATEM at {_ipAddress}");
                return false;
            }

            _retryCount++;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Reconnection attempt {_retryCount} for ATEM at {_ipAddress}");

            Disconnect();
            await Task.Delay(2000); // Wait 2 seconds before retry

            return await TryConnect();
        }

        private void SetupStatusMonitoring()
        {
            try
            {
                var switcherState = ATEMConnectionManager.Instance.GetSwitcherState(_ipAddress);

                // Get mix effect blocks and setup callbacks
                IntPtr meIteratorPtr;
                _switcher.CreateIterator(typeof(IBMDSwitcherMixEffectBlockIterator).GUID, out meIteratorPtr);
                IBMDSwitcherMixEffectBlockIterator meIterator = Marshal.GetObjectForIUnknown(meIteratorPtr) as IBMDSwitcherMixEffectBlockIterator;
                if (meIterator != null)
                {
                    try
                    {
                        int meIndex = 0;
                        while (true)
                        {
                            meIterator.Next(out IBMDSwitcherMixEffectBlock meBlock);
                            if (meBlock == null) break;

                            // Initialize state for this ME block
                            InitializeMixEffectState(meBlock, meIndex, switcherState);

                            // Create and add ME callback
                            var meCallback = new ATEMMixEffectCallback(_ipAddress, meIndex, switcherState, meBlock);
                            meBlock.AddCallback(meCallback);
                            _mixEffectCallbacks.Add((meCallback, meBlock));

                            Logger.Instance.LogMessage(TracingLevel.INFO, $"Setup monitoring for ME {meIndex}");
                            meIndex++;
                        }
                    }
                    finally
                    {
                        // Release the iterator
                        Marshal.ReleaseComObject(meIterator);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting up status monitoring: {ex}");
            }
        }

        private void InitializeMixEffectState(IBMDSwitcherMixEffectBlock meBlock, int meIndex, ATEMSwitcherState switcherState)
        {
            try
            {
                var meState = switcherState.GetMixEffectState(meIndex);

                // Get initial state
                meBlock.GetInTransition(out int inTransition);
                meBlock.GetTransitionPosition(out double position);
                meBlock.GetProgramInput(out long programInput);
                meBlock.GetPreviewInput(out long previewInput);

                // Update state
                meState.UpdateTransitionState(inTransition != 0, position);
                meState.UpdateInputs(programInput, previewInput);

                Logger.Instance.LogMessage(TracingLevel.INFO,
                    $"Initialized ME {meIndex}: Program={programInput}, Preview={previewInput}");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error initializing ME {meIndex} state: {ex}");
            }
        }

        private void CleanupCallbacks()
        {
            try
            {
                // Cleanup ME callbacks
                foreach (var (callback, meBlock) in _mixEffectCallbacks)
                {
                    try
                    {
                        meBlock?.RemoveCallback(callback);
                        // Release the mix effect block COM object
                        if (meBlock != null)
                        {
                            Marshal.ReleaseComObject(meBlock);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessage(TracingLevel.WARN, $"Error removing ME callback: {ex}");
                    }
                }
                _mixEffectCallbacks.Clear();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error cleaning up callbacks: {ex}");
            }
        }

        public ATEMSwitcherWrapper GetSwitcherWrapper()
        {
            _lastActivity = DateTime.Now;
            return _isConnected && _switcher != null ? new ATEMSwitcherWrapper(_switcher) : null;
        }

        public void Disconnect()
        {
            lock (_lockObject)
            {
                try
                {
                    CleanupCallbacks();

                    if (_switcher != null)
                    {
                        Marshal.ReleaseComObject(_switcher);
                        _switcher = null;
                    }
                    _isConnected = false;
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Disconnected from ATEM at {_ipAddress}");
                    ConnectionStateChanged?.Invoke(this, false);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error disconnecting from ATEM at {_ipAddress}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Disconnect();

            if (_discovery != null)
            {
                Marshal.ReleaseComObject(_discovery);
                _discovery = null;
            }
        }
    }

    public class ATEMStatusEventArgs : EventArgs
    {
        public string PropertyName { get; set; }
        public object Value { get; set; }
        public DateTime Timestamp { get; set; }

        public ATEMStatusEventArgs(string propertyName, object value)
        {
            PropertyName = propertyName;
            Value = value;
            Timestamp = DateTime.Now;
        }
    }

    public class ATEMSwitcherWrapper
    {
        private readonly IBMDSwitcher _switcher;

        public ATEMSwitcherWrapper(IBMDSwitcher switcher)
        {
            _switcher = switcher;
        }

        public IEnumerable<IBMDSwitcherMixEffectBlock> MixEffectBlocks
        {
            get
            {
                IntPtr meIteratorPtr;
                _switcher.CreateIterator(typeof(IBMDSwitcherMixEffectBlockIterator).GUID, out meIteratorPtr);
                IBMDSwitcherMixEffectBlockIterator meIterator = Marshal.GetObjectForIUnknown(meIteratorPtr) as IBMDSwitcherMixEffectBlockIterator;
                if (meIterator == null)
                    yield break;

                try
                {
                    while (true)
                    {
                        IBMDSwitcherMixEffectBlock me;
                        meIterator.Next(out me);

                        if (me != null)
                            yield return me;
                        else
                            yield break;
                    }
                }
                finally
                {
                    // Always release the iterator
                    Marshal.ReleaseComObject(meIterator);
                }
            }
        }

        public IEnumerable<IBMDSwitcherInput> SwitcherInputs
        {
            get
            {
                IntPtr inputIteratorPtr;
                _switcher.CreateIterator(typeof(IBMDSwitcherInputIterator).GUID, out inputIteratorPtr);
                IBMDSwitcherInputIterator inputIterator = Marshal.GetObjectForIUnknown(inputIteratorPtr) as IBMDSwitcherInputIterator;
                if (inputIterator == null)
                    yield break;

                try
                {
                    while (true)
                    {
                        IBMDSwitcherInput input;
                        inputIterator.Next(out input);

                        if (input != null)
                            yield return input;
                        else
                            yield break;
                    }
                }
                finally
                {
                    // Always release the iterator
                    Marshal.ReleaseComObject(inputIterator);
                }
            }
        }
    }
}