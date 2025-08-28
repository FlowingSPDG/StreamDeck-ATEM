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
            }
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

        public async Task<bool> TryConnect()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_isConnected)
                        return true;

                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Attempting to connect to ATEM at {_ipAddress}");

                    _BMDSwitcherConnectToFailure failureReason;
                    _discovery.ConnectTo(_ipAddress, out _switcher, out failureReason);

                    if (_switcher != null)
                    {
                        _isConnected = true;
                        _retryCount = 0;
                        _lastActivity = DateTime.Now;
                        
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Successfully connected to ATEM at {_ipAddress}");
                        
                        // Setup event handlers for status monitoring
                        SetupStatusMonitoring();
                        
                        ConnectionStateChanged?.Invoke(this, true);
                        return true;
                    }
                    else
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to connect to ATEM at {_ipAddress}. Reason: {failureReason}");
                        _isConnected = false;
                        ConnectionStateChanged?.Invoke(this, false);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Exception connecting to ATEM at {_ipAddress}: {ex.Message}");
                    _isConnected = false;
                    ConnectionStateChanged?.Invoke(this, false);
                    return false;
                }
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
            // Note: In a real implementation, you would set up callbacks for BMD switcher events
            // This is a simplified version - BMD SDK has specific callback interfaces
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
        }
    }
}