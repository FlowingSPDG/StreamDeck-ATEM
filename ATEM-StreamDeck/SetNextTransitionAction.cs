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
    [PluginActionId("dev.flowingspdg.atem.setnexttransition")]
    public class SetNextTransitionAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.ATEMIPAddress = ATEMConstants.DEFAULT_ATEM_IP;
                instance.MixEffectBlock = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
                instance.TransitionStyle = 0; // Mix transition
                instance.TransitionDuration = ATEMConstants.DEFAULT_TRANSITION_DURATION;
                instance.ShowTally = false;
                return instance;
            }

            [JsonProperty(PropertyName = "atemIPAddress")]
            public string ATEMIPAddress { get; set; }

            [JsonProperty(PropertyName = "mixEffectBlock")]
            public int MixEffectBlock { get; set; }

            [JsonProperty(PropertyName = "transitionStyle")]
            public int TransitionStyle { get; set; }

            [JsonProperty(PropertyName = "transitionDuration")]
            public double TransitionDuration { get; set; }

            [JsonProperty(PropertyName = "showTally")]
            public bool ShowTally { get; set; }
        }

        #region Private Members

        private PluginSettings settings;
        private ATEMConnection connection;
        private bool isRetrying = false;

        #endregion

        public SetNextTransitionAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "SetNextTransitionAction constructor called");
                
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
                
                Logger.Instance.LogMessage(TracingLevel.INFO, "SetNextTransitionAction constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in SetNextTransitionAction constructor: {ex}");
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

                // Subscribe to global state changes for tally support
                if (settings.ShowTally)
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

                if (e.EventType == ATEMEventType.TransitionStateChanged)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO,
                        $"Transition state changed for ME {settings.MixEffectBlock}");
                    
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
                    return;
                }

                // Get current state from cache
                bool isInTransition = GetCurrentTransitionState();

                if (isInTransition)
                {
                    // Set button to red image when in transition
                    Connection.SetImageAsync(ATEMConstants.RED_BUTTON_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to RED (ME {settings.MixEffectBlock} in transition)");
                }
                else
                {
                    // Set button to default image when not in transition
                    Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Button image set to DEFAULT (ME {settings.MixEffectBlock} not in transition)");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error updating button state from cache: {ex}");
                // Fallback to default image on error
                Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
            }
        }

        private bool GetCurrentTransitionState()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    return false;
                }

                var switcherState = ATEMConnectionManager.Instance.GetSwitcherState(settings.ATEMIPAddress);
                var meState = switcherState.GetMixEffectState(settings.MixEffectBlock);

                return meState.IsInTransition;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error getting current transition state: {ex}");
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
                    transitionStyles = new[]
                    {
                        new { value = 0, name = "Mix" },
                        new { value = 1, name = "Dip" },
                        new { value = 2, name = "Wipe" },
                        new { value = 3, name = "DVE" },
                        new { value = 4, name = "Stinger" }
                    }
                };

                Connection.SendToPropertyInspectorAsync(JObject.FromObject(atemInfoPayload));
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Sent ATEM info to Property Inspector: {switcherInfo.MixEffectCount} ME blocks");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, $"SetNextTransitionAction Dispose called");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, "Set Next Transition Action - Key Pressed");
                SetNextTransition();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload) 
        {
            // Set next transition action is performed on key press, no action needed on release
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
                // If Mix Effect block or tally setting changed, update state monitoring
                else if (oldMixEffectBlock != settings.MixEffectBlock || oldShowTally != settings.ShowTally)
                {
                    // Update tally subscription
                    if (settings.ShowTally && !oldShowTally)
                    {
                        ATEMConnectionManager.Instance.StateChanged += OnATEMStateChanged;
                    }
                    else if (!settings.ShowTally && oldShowTally)
                    {
                        ATEMConnectionManager.Instance.StateChanged -= OnATEMStateChanged;
                    }

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

        private _BMDSwitcherTransitionStyle GetTransitionStyleFromIndex(int index)
        {
            // Map UI index to actual ATEM SDK enum values
            switch (index)
            {
                case 0: return _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;
                case 1: return _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip;
                case 2: return _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe;
                case 3: return _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE;
                case 4: return _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger;
                default:
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"Invalid transition style index {index}, defaulting to Mix");
                    return _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;
            }
        }

        private uint ConvertDurationToFrames(double durationInSeconds)
        {
            // Get the actual framerate from the ATEM switcher
            double framesPerSecond = ATEMConnectionManager.Instance.GetSwitcherFramerate(settings.ATEMIPAddress);
            uint frames = (uint)Math.Round(durationInSeconds * framesPerSecond) / 2;
            
            // Ensure minimum and maximum frame constraints
            uint result = Math.Max(ATEMConstants.MIN_TRANSITION_FRAMES, Math.Min(ATEMConstants.MAX_TRANSITION_FRAMES, frames));
            
            Logger.Instance.LogMessage(TracingLevel.INFO, 
                $"Converting {durationInSeconds}s (actual: {durationInSeconds}s) at {framesPerSecond} fps = {result} frames");
            
            return result;
        }

        private void SetNextTransition()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "ATEM not connected, cannot set next transition");
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

                Logger.Instance.LogMessage(TracingLevel.INFO, $"Setting next transition style to index {settings.TransitionStyle}");
                
                // Get the correct transition style enum value
                var transitionStyle = GetTransitionStyleFromIndex(settings.TransitionStyle);
                
                // Convert duration to frames for ATEM API (with 2x multiplier and actual framerate)
                uint transitionFrames = ConvertDurationToFrames(settings.TransitionDuration);
                
                // Setup transition parameters
                var transitionParams = mixEffectBlock as IBMDSwitcherTransitionParameters;
                if (transitionParams != null)
                {
                    try
                    {
                        transitionParams.SetNextTransitionStyle(transitionStyle);
                        transitionParams.SetNextTransitionSelection(_BMDSwitcherTransitionSelection.bmdSwitcherTransitionSelectionBackground);
                        
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Successfully set next transition style to {transitionStyle}");
                        
                        // Set transition rate based on style
                        switch (transitionStyle)
                        {
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix:
                                var mixParams = mixEffectBlock as IBMDSwitcherTransitionMixParameters;
                                if (mixParams != null)
                                {
                                    mixParams.SetRate(transitionFrames);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set mix transition duration to {settings.TransitionDuration}s (actual: {settings.TransitionDuration * 2.0}s, {transitionFrames} frames)");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe:
                                var wipeParams = mixEffectBlock as IBMDSwitcherTransitionWipeParameters;
                                if (wipeParams != null)
                                {
                                    wipeParams.SetRate(transitionFrames);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set wipe transition duration to {settings.TransitionDuration}s (actual: {settings.TransitionDuration * 2.0}s, {transitionFrames} frames)");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip:
                                var dipParams = mixEffectBlock as IBMDSwitcherTransitionDipParameters;
                                if (dipParams != null)
                                {
                                    dipParams.SetRate(transitionFrames);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set dip transition duration to {settings.TransitionDuration}s (actual: {settings.TransitionDuration * 2.0}s, {transitionFrames} frames)");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE:
                                var dveParams = mixEffectBlock as IBMDSwitcherTransitionDVEParameters;
                                if (dveParams != null)
                                {
                                    dveParams.SetRate(transitionFrames);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set DVE transition duration to {settings.TransitionDuration}s (actual: {settings.TransitionDuration * 2.0}s, {transitionFrames} frames)");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger:
                                // Stinger transitions typically don't have configurable rates
                                Logger.Instance.LogMessage(TracingLevel.INFO, "Stinger transition selected (duration not configurable)");
                                break;
                        }

                        Logger.Instance.LogMessage(TracingLevel.INFO, "Next transition configuration completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting transition parameters: {ex}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting next transition: {ex}");
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