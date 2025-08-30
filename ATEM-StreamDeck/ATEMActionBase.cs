using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    /// <summary>
    /// Base class for all ATEM StreamDeck actions providing common functionality
    /// </summary>
    public abstract class ATEMActionBase : KeypadBase
    {
        #region Protected Properties

        protected ATEMConnection connection;
        protected bool isRetrying = false;

        #endregion

        #region Abstract Properties

        /// <summary>
        /// Gets the ATEM IP address from the current settings
        /// </summary>
        protected abstract string ATEMIPAddress { get; }

        /// <summary>
        /// Gets the Mix Effect Block from the current settings
        /// </summary>
        protected abstract int MixEffectBlock { get; }

        /// <summary>
        /// Gets whether this action supports Property Inspector communication
        /// </summary>
        protected virtual bool SupportsPropertyInspector => true;

        /// <summary>
        /// Gets whether this action supports ATEM state change monitoring
        /// </summary>
        protected virtual bool SupportsStateMonitoring => false;

        #endregion

        #region Constructor

        protected ATEMActionBase(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"{GetType().Name} constructor called");

                // Initialize settings through derived class
                InitializeSettings(payload);

                // Set up common event handlers
                if (SupportsPropertyInspector)
                {
                    Connection.OnSendToPlugin += Connection_OnSendToPlugin;
                }

                InitializeATEMConnection();

                Logger.Instance.LogMessage(TracingLevel.INFO, $"{GetType().Name} constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in {GetType().Name} constructor: {ex}");
                InitializeDefaultSettings();
            }
        }

        #endregion

        #region Abstract Methods

        /// <summary>
        /// Initialize settings from payload or create default settings
        /// </summary>
        protected abstract void InitializeSettings(InitialPayload payload);

        /// <summary>
        /// Initialize default settings when initialization fails
        /// </summary>
        protected abstract void InitializeDefaultSettings();

        /// <summary>
        /// Save current settings
        /// </summary>
        protected abstract Task SaveSettings();

        /// <summary>
        /// Handle settings updates
        /// </summary>
        protected abstract void HandleSettingsUpdate(ReceivedSettingsPayload payload);

        /// <summary>
        /// Handle the main action when key is pressed
        /// </summary>
        protected abstract void PerformAction();

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Handle ATEM state changes - override in derived classes that need state monitoring
        /// </summary>
        protected virtual void OnATEMStateChanged(object sender, ATEMStateChangeEventArgs e) { }

        /// <summary>
        /// Update button state - override in derived classes that need visual feedback
        /// </summary>
        protected virtual void UpdateButtonStateFromCache() { }

        /// <summary>
        /// Create ATEM info payload for Property Inspector
        /// </summary>
        protected virtual object CreateATEMInfoPayload(ATEMSwitcherInfo switcherInfo)
        {
            return new
            {
                action = "atemInfoResponse",
                ipAddress = ATEMIPAddress,
                mixEffectCount = switcherInfo.MixEffectCount,
                inputCount = switcherInfo.InputCount
            };
        }

        #endregion

        #region Common Event Handlers

        private void Connection_OnSendToPlugin(object sender, SDEventReceivedEventArgs<BarRaider.SdTools.Events.SendToPlugin> e)
        {
            try
            {
                var payload = e.Event.Payload;
                if (payload != null && payload["action"]?.ToString() == "getATEMInfo")
                {
                    string requestedIP = payload["ipAddress"]?.ToString();
                    if (!string.IsNullOrEmpty(requestedIP))
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Received ATEM info request for IP: {requestedIP}");
                        
                        // Update IP if different and notify derived class
                        if (requestedIP != ATEMIPAddress)
                        {
                            OnIPAddressChangeRequested(requestedIP);
                        }

                        // Send current ATEM info
                        SendATEMInfoToPropertyInspector();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in SendToPlugin event handler: {ex}");
            }
        }

        protected virtual void OnIPAddressChangeRequested(string newIPAddress)
        {
            // Default implementation - derived classes should override if they need custom handling
        }

        private void InitializeATEMConnection()
        {
            if (!string.IsNullOrEmpty(ATEMIPAddress))
            {
                connection = ATEMConnectionManager.Instance.GetConnection(ATEMIPAddress);
                connection.ConnectionStateChanged += OnConnectionStateChanged;

                // Subscribe to global state changes if supported
                if (SupportsStateMonitoring)
                {
                    ATEMConnectionManager.Instance.StateChanged += OnATEMStateChanged;
                }

                // Update button state based on current cached state
                UpdateButtonStateFromCache();
            }
        }

        private void OnConnectionStateChanged(object sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"ATEM connection established for {ATEMIPAddress}");
                // Send ATEM info to Property Inspector when connected
                if (SupportsPropertyInspector)
                {
                    SendATEMInfoToPropertyInspector();
                }
                // Update button state when connection is established
                UpdateButtonStateFromCache();
            }
            else
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"ATEM connection lost for {ATEMIPAddress}");
                // Show default state when disconnected
                UpdateButtonStateFromCache();
            }
        }

        private void SendATEMInfoToPropertyInspector()
        {
            try
            {
                var switcherInfo = ATEMConnectionManager.Instance.GetSwitcherInfo(ATEMIPAddress);
                if (switcherInfo.LastUpdated == DateTime.MinValue)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "ATEM info not yet cached, skipping PI update");
                    return;
                }

                var atemInfoPayload = CreateATEMInfoPayload(switcherInfo);
                Connection.SendToPropertyInspectorAsync(JObject.FromObject(atemInfoPayload));
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Sent ATEM info to Property Inspector: {switcherInfo.MixEffectCount} ME blocks, {switcherInfo.InputCount} inputs");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error sending ATEM info to Property Inspector: {ex}");
            }
        }

        #endregion

        #region Protected Helper Methods

        /// <summary>
        /// Get mix effect block wrapper safely
        /// </summary>
        protected IBMDSwitcherMixEffectBlock GetMixEffectBlock()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "ATEM not connected");
                    return null;
                }

                var switcherWrapper = connection.GetSwitcherWrapper();
                if (switcherWrapper == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "Failed to get switcher wrapper");
                    return null;
                }

                var mixEffectBlock = switcherWrapper.MixEffectBlocks.ElementAtOrDefault(MixEffectBlock);
                if (mixEffectBlock == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Mix Effect Block {MixEffectBlock} not found");
                    return null;
                }

                return mixEffectBlock;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error getting mix effect block: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Handle connection reconnection
        /// </summary>
        protected void HandleReconnection()
        {
            if (connection != null)
            {
                connection.ConnectionStateChanged -= OnConnectionStateChanged;
            }
            InitializeATEMConnection();
        }

        #endregion

        #region Overrides

        public override void Dispose()
        {
            try
            {
                // Unsubscribe from events
                if (SupportsPropertyInspector)
                {
                    Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
                }

                // Unsubscribe from global state changes
                if (SupportsStateMonitoring)
                {
                    ATEMConnectionManager.Instance.StateChanged -= OnATEMStateChanged;
                }

                if (connection != null)
                {
                    connection.ConnectionStateChanged -= OnConnectionStateChanged;
                }
                Logger.Instance.LogMessage(TracingLevel.INFO, $"{GetType().Name} Dispose called");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in Dispose: {ex}");
            }
        }

        public override void KeyPressed(KeyPayload payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"{GetType().Name} - Key Pressed");
                PerformAction();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            // Most ATEM actions are performed on key press, no action needed on release
            // Override in derived classes if needed
        }

        public override void OnTick()
        {
            // Check connection status and retry if needed
            if (connection != null && !connection.IsConnected && !isRetrying)
            {
                Task.Run(async () =>
                {
                    isRetrying = true;
                    try
                    {
                        await connection.TryReconnect();
                    }
                    finally
                    {
                        isRetrying = false;
                    }
                });
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            try
            {
                HandleSettingsUpdate(payload);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in ReceivedSettings: {ex}");
            }
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        #endregion
    }
}