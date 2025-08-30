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
    [PluginActionId("dev.flowingspdg.atem.program")]
    public class ProgramAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.ATEMIPAddress = ATEMConstants.DEFAULT_ATEM_IP;
                instance.MixEffectBlock = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
                instance.InputId = 1;
                instance.TallyForPreview = false;
                instance.TallyForProgram = true;
                return instance;
            }

            [JsonProperty(PropertyName = "atemIPAddress")]
            public string ATEMIPAddress { get; set; }

            [JsonProperty(PropertyName = "mixEffectBlock")]
            public int MixEffectBlock { get; set; }

            [JsonProperty(PropertyName = "inputId")]
            public long InputId { get; set; }

            [JsonProperty(PropertyName = "tallyForPreview")]
            public bool TallyForPreview { get; set; }

            [JsonProperty(PropertyName = "tallyForProgram")]
            public bool TallyForProgram { get; set; }

            // Backward compatibility
            [JsonProperty(PropertyName = "showTally")]
            public bool ShowTally
            {
                get => TallyForProgram;
                set => TallyForProgram = value;
            }
        }

        #region Private Members

        private PluginSettings settings;
        private ATEMConnection connection;
        private bool isRetrying = false;

        #endregion

        public ProgramAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "ProgramAction constructor called");

                if (payload.Settings == null || payload.Settings.Count == 0)
                {
                    this.settings = PluginSettings.CreateDefaultSettings();
                    SaveSettings();
                }
                else
                {
                    this.settings = payload.Settings.ToObject<PluginSettings>();
                }

                // Set up event handlers
                Connection.OnSendToPlugin += Connection_OnSendToPlugin;

                InitializeATEMConnection();

                Logger.Instance.LogMessage(TracingLevel.INFO, "ProgramAction constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in ProgramAction constructor: {ex}");
                this.settings = PluginSettings.CreateDefaultSettings();
            }
        }

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
                        
                        // Update IP if different
                        if (requestedIP != settings.ATEMIPAddress)
                        {
                            settings.ATEMIPAddress = requestedIP;
                            InitializeATEMConnection();
                            SaveSettings();
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

        private void InitializeATEMConnection()
        {
            if (!string.IsNullOrEmpty(settings.ATEMIPAddress))
            {
                connection = ATEMConnectionManager.Instance.GetConnection(settings.ATEMIPAddress);
                connection.ConnectionStateChanged += OnConnectionStateChanged;

                // Subscribe to global state changes
                ATEMConnectionManager.Instance.StateChanged += OnATEMStateChanged;

                // Update button state based on current cached state
                UpdateButtonStateFromCache();
            }
        }

        private void OnConnectionStateChanged(object sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"ATEM connection established for {settings.ATEMIPAddress}");
                // Send ATEM info to Property Inspector when connected
                SendATEMInfoToPropertyInspector();
                // Update button state when connection is established
                UpdateButtonStateFromCache();
            }
            else
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"ATEM connection lost for {settings.ATEMIPAddress}");
                // Show default state when disconnected
                UpdateButtonStateFromCache();
            }
        }

        private void OnATEMStateChanged(object sender, ATEMStateChangeEventArgs e)
        {
            try
            {
                // Only handle events for our specific switcher and Mix Effect block
                if (e.IPAddress != settings.ATEMIPAddress || e.MixEffectIndex != settings.MixEffectBlock)
                    return;

                // Handle both preview and program changes since we support both tally options
                if (e.EventType == ATEMEventType.PreviewInputChanged || e.EventType == ATEMEventType.ProgramInputChanged)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO,
                        $"{e.EventType} changed to {e.NewValue} for ME {settings.MixEffectBlock}");
                    
                    // Update button state based on the new cached state
                    UpdateButtonStateFromCache();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error handling ATEM state change: {ex}");
            }
        }

        private void UpdateButtonStateFromCache()
        {
            try
            {
                if (!settings.TallyForPreview && !settings.TallyForProgram)
                {
                    // If both tally options are disabled, show default image
                    Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to DEFAULT (tally disabled for input {settings.InputId})");
                    return;
                }

                // Get current state from cache
                bool isOnPreview = settings.TallyForPreview ? GetCurrentPreviewState() : false;
                bool isOnProgram = settings.TallyForProgram ? GetCurrentProgramState() : false;

                // Priority: Program (RED) > Preview (GREEN) > Default
                if (isOnProgram && settings.TallyForProgram)
                {
                    // Set button to red image when on program (highest priority)
                    Connection.SetImageAsync(ATEMConstants.RED_BUTTON_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to RED (input {settings.InputId} on program)");
                }
                else if (isOnPreview && settings.TallyForPreview)
                {
                    // Set button to green image when on preview
                    Connection.SetImageAsync(ATEMConstants.GREEN_BUTTON_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to GREEN (input {settings.InputId} on preview)");
                }
                else
                {
                    // Set button to default image when not matching any enabled tally condition
                    Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to DEFAULT (input {settings.InputId} not matching tally conditions)");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error updating button state from cache: {ex}");
                // Fallback to default image on error
                Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
            }
        }

        private bool GetCurrentPreviewState()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Connection not available for input {settings.InputId}, returning false");
                    return false;
                }

                var switcherState = ATEMConnectionManager.Instance.GetSwitcherState(settings.ATEMIPAddress);
                var meState = switcherState.GetMixEffectState(settings.MixEffectBlock);

                bool isOnPreview = (meState.PreviewInput == settings.InputId);
                Logger.Instance.LogMessage(TracingLevel.DEBUG, 
                    $"Input {settings.InputId} preview state: {isOnPreview} (current preview: {meState.PreviewInput})");
                
                return isOnPreview;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error getting current preview state: {ex}");
                return false;
            }
        }

        private bool GetCurrentProgramState()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Connection not available for input {settings.InputId}, returning false");
                    return false;
                }

                var switcherState = ATEMConnectionManager.Instance.GetSwitcherState(settings.ATEMIPAddress);
                var meState = switcherState.GetMixEffectState(settings.MixEffectBlock);

                bool isOnProgram = (meState.ProgramInput == settings.InputId);
                Logger.Instance.LogMessage(TracingLevel.DEBUG, 
                    $"Input {settings.InputId} program state: {isOnProgram} (current program: {meState.ProgramInput})");
                
                return isOnProgram;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error getting current program state: {ex}");
                return false;
            }
        }

        private void SendATEMInfoToPropertyInspector()
        {
            try
            {
                var switcherInfo = ATEMConnectionManager.Instance.GetSwitcherInfo(settings.ATEMIPAddress);
                if (switcherInfo.LastUpdated == DateTime.MinValue)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "ATEM info not yet cached, skipping PI update");
                    return;
                }

                var atemInfoPayload = new
                {
                    action = "atemInfoResponse",
                    ipAddress = settings.ATEMIPAddress,
                    mixEffectCount = switcherInfo.MixEffectCount,
                    inputCount = switcherInfo.InputCount,
                    inputs = switcherInfo.Inputs.Select(input => new
                    {
                        inputId = input.InputId,
                        shortName = input.ShortName,
                        longName = input.LongName,
                        displayName = input.GetDisplayName()
                    }).ToArray()
                };

                Connection.SendToPropertyInspectorAsync(JObject.FromObject(atemInfoPayload));
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Sent ATEM info to Property Inspector: {switcherInfo.MixEffectCount} ME blocks, {switcherInfo.InputCount} inputs");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error sending ATEM info to Property Inspector: {ex}");
            }
        }

        public override void Dispose()
        {
            try
            {
                // Unsubscribe from events
                Connection.OnSendToPlugin -= Connection_OnSendToPlugin;

                // Unsubscribe from global state changes
                ATEMConnectionManager.Instance.StateChanged -= OnATEMStateChanged;

                if (connection != null)
                {
                    connection.ConnectionStateChanged -= OnConnectionStateChanged;
                }
                Logger.Instance.LogMessage(TracingLevel.INFO, $"ProgramAction Dispose called");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Program Action - Key Pressed for input {settings.InputId}");
                SetProgramInput();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            // Program action is performed on key press, no action needed on release
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
                string oldIP = settings.ATEMIPAddress;
                int oldMixEffectBlock = settings.MixEffectBlock;
                long oldInputId = settings.InputId;
                bool oldTallyForPreview = settings.TallyForPreview;
                bool oldTallyForProgram = settings.TallyForProgram;

                Tools.AutoPopulateSettings(settings, payload.Settings);

                // If IP address changed, reconnect
                if (oldIP != settings.ATEMIPAddress)
                {
                    if (connection != null)
                    {
                        connection.ConnectionStateChanged -= OnConnectionStateChanged;
                    }
                    InitializeATEMConnection();
                }
                // If any settings that affect state changed, update button state
                else if (oldMixEffectBlock != settings.MixEffectBlock || 
                         oldInputId != settings.InputId || 
                         oldTallyForPreview != settings.TallyForPreview ||
                         oldTallyForProgram != settings.TallyForProgram)
                {
                    UpdateButtonStateFromCache();
                }

                SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in ReceivedSettings: {ex}");
            }
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        #region Private Methods

        private void SetProgramInput()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "ATEM not connected, cannot set program input");
                    return;
                }

                var switcherWrapper = connection.GetSwitcherWrapper();
                if (switcherWrapper == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "Failed to get switcher wrapper");
                    return;
                }

                var mixEffectBlock = switcherWrapper.MixEffectBlocks.ElementAtOrDefault(settings.MixEffectBlock);
                if (mixEffectBlock == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Mix Effect Block {settings.MixEffectBlock} not found");
                    return;
                }

                try
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Setting program input to {settings.InputId}");
                    mixEffectBlock.SetProgramInput(settings.InputId);
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Program input set successfully");
                }
                finally
                {
                    // Note: Mix effect blocks from wrapper are automatically cleaned up by the wrapper
                    // The wrapper itself handles COM object cleanup in its enumerator finalizers
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting program input: {ex}");
            }
        }

        private Task SaveSettings()
        {
            try
            {
                return Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in SaveSettings: {ex}");
                return Task.CompletedTask;
            }
        }

        #endregion
    }
}