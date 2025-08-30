using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.preview")]
    public class PreviewAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.ATEMIPAddress = ATEMConstants.DEFAULT_ATEM_IP;
                instance.MixEffectBlock = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
                instance.InputId = 1;
                instance.ShowTally = true;
                return instance;
            }

            [JsonProperty(PropertyName = "atemIPAddress")]
            public string ATEMIPAddress { get; set; }

            [JsonProperty(PropertyName = "mixEffectBlock")]
            public int MixEffectBlock { get; set; }

            [JsonProperty(PropertyName = "inputId")]
            public long InputId { get; set; }

            [JsonProperty(PropertyName = "showTally")]
            public bool ShowTally { get; set; }
        }

        #region Private Members

        private PluginSettings settings;
        private ATEMConnection connection;
        private bool isRetrying = false;

        #endregion

        public PreviewAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "PreviewAction constructor called");

                if (payload.Settings == null || payload.Settings.Count == 0)
                {
                    this.settings = PluginSettings.CreateDefaultSettings();
                    SaveSettings();
                }
                else
                {
                    this.settings = payload.Settings.ToObject<PluginSettings>();
                }

                InitializeATEMConnection();

                Logger.Instance.LogMessage(TracingLevel.INFO, "PreviewAction constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in PreviewAction constructor: {ex}");
                this.settings = PluginSettings.CreateDefaultSettings();
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

                if (e.EventType == ATEMEventType.PreviewInputChanged)
                {
                    long newPreviewInput = (long)e.NewValue;
                    Logger.Instance.LogMessage(TracingLevel.INFO,
                        $"Preview input changed to {newPreviewInput} for ME {settings.MixEffectBlock}");
                    
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
                if (!settings.ShowTally)
                {
                    // If tally is disabled, show default image
                    Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to DEFAULT (tally disabled for input {settings.InputId})");
                    return;
                }

                // Get current state from cache
                bool isOnPreview = GetCurrentPreviewState();

                if (isOnPreview)
                {
                    // Set button to green image when on preview
                    Connection.SetImageAsync(ATEMConstants.GREEN_BUTTON_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to GREEN (input {settings.InputId} on preview)");
                }
                else
                {
                    // Set button to default image when not on preview
                    Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to DEFAULT (input {settings.InputId} not on preview)");
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

        public override void Dispose()
        {
            try
            {
                // Unsubscribe from global state changes
                ATEMConnectionManager.Instance.StateChanged -= OnATEMStateChanged;

                if (connection != null)
                {
                    connection.ConnectionStateChanged -= OnConnectionStateChanged;
                }
                Logger.Instance.LogMessage(TracingLevel.INFO, $"PreviewAction Dispose called");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Preview Action - Key Pressed for input {settings.InputId}");
                SetPreviewInput();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            // Preview action is performed on key press, no action needed on release
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
                bool oldShowTally = settings.ShowTally;

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
                         oldShowTally != settings.ShowTally)
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

        private async void SetPreviewInput()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "ATEM not connected, cannot set preview input");
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

                Logger.Instance.LogMessage(TracingLevel.INFO, $"Setting preview input to {settings.InputId}");
                mixEffectBlock.SetPreviewInput(settings.InputId);
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Preview input set to {settings.InputId} successfully");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting preview input: {ex}");
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