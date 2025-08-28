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
    [PluginActionId("dev.flowingspdg.atem.autotransition")]
    public class AutoTransitionAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.ATEMIPAddress = "192.168.1.101";
                instance.MixEffectBlock = 0;
                instance.TransitionStyle = 0; // Mix transition
                instance.TransitionRate = 30; // frames
                return instance;
            }

            [JsonProperty(PropertyName = "atemIPAddress")]
            public string ATEMIPAddress { get; set; }

            [JsonProperty(PropertyName = "mixEffectBlock")]
            public int MixEffectBlock { get; set; }

            [JsonProperty(PropertyName = "transitionStyle")]
            public int TransitionStyle { get; set; }

            [JsonProperty(PropertyName = "transitionRate")]
            public uint TransitionRate { get; set; }
        }

        #region Private Members

        private PluginSettings settings;
        private ATEMConnection connection;
        private bool isRetrying = false;

        #endregion

        public AutoTransitionAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "AutoTransitionAction constructor called");
                
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
                
                Logger.Instance.LogMessage(TracingLevel.INFO, "AutoTransitionAction constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in AutoTransitionAction constructor: {ex}");
                this.settings = PluginSettings.CreateDefaultSettings();
            }
        }

        private void InitializeATEMConnection()
        {
            if (!string.IsNullOrEmpty(settings.ATEMIPAddress))
            {
                connection = ATEMConnectionManager.Instance.GetConnection(settings.ATEMIPAddress);
                connection.ConnectionStateChanged += OnConnectionStateChanged;
            }
        }

        private void OnConnectionStateChanged(object sender, bool isConnected)
        {
            if (isConnected)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"ATEM connection established for {settings.ATEMIPAddress}");
            }
            else
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"ATEM connection lost for {settings.ATEMIPAddress}");
            }
        }

        public override void Dispose()
        {
            try
            {
                if (connection != null)
                {
                    connection.ConnectionStateChanged -= OnConnectionStateChanged;
                }
                Logger.Instance.LogMessage(TracingLevel.INFO, $"AutoTransitionAction Dispose called");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, "Auto Transition Action - Key Pressed");
                PerformAutoTransition();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload) 
        {
            // Auto transition action is performed on key press, no action needed on release
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

        private async void PerformAutoTransition()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "ATEM not connected, cannot perform auto transition");
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

                Logger.Instance.LogMessage(TracingLevel.INFO, $"Setting transition style to index {settings.TransitionStyle}");
                
                // Get the correct transition style enum value
                var transitionStyle = GetTransitionStyleFromIndex(settings.TransitionStyle);
                
                // Setup transition parameters
                var transitionParams = mixEffectBlock as IBMDSwitcherTransitionParameters;
                if (transitionParams != null)
                {
                    try
                    {
                        transitionParams.SetNextTransitionStyle(transitionStyle);
                        transitionParams.SetNextTransitionSelection(_BMDSwitcherTransitionSelection.bmdSwitcherTransitionSelectionBackground);
                        
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Successfully set transition style to {transitionStyle}");
                        
                        // Set transition rate based on style
                        switch (transitionStyle)
                        {
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix:
                                var mixParams = mixEffectBlock as IBMDSwitcherTransitionMixParameters;
                                if (mixParams != null)
                                {
                                    mixParams.SetRate(settings.TransitionRate);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set mix transition rate to {settings.TransitionRate}");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe:
                                var wipeParams = mixEffectBlock as IBMDSwitcherTransitionWipeParameters;
                                if (wipeParams != null)
                                {
                                    wipeParams.SetRate(settings.TransitionRate);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set wipe transition rate to {settings.TransitionRate}");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip:
                                var dipParams = mixEffectBlock as IBMDSwitcherTransitionDipParameters;
                                if (dipParams != null)
                                {
                                    dipParams.SetRate(settings.TransitionRate);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set dip transition rate to {settings.TransitionRate}");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE:
                                var dveParams = mixEffectBlock as IBMDSwitcherTransitionDVEParameters;
                                if (dveParams != null)
                                {
                                    dveParams.SetRate(settings.TransitionRate);
                                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Set DVE transition rate to {settings.TransitionRate}");
                                }
                                break;
                            case _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger:
                                // Stinger transitions typically don't have configurable rates
                                Logger.Instance.LogMessage(TracingLevel.INFO, "Stinger transition selected (rate not configurable)");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting transition parameters: {ex}");
                        return;
                    }
                }

                Logger.Instance.LogMessage(TracingLevel.INFO, "Performing auto transition");
                mixEffectBlock.PerformAutoTransition();
                Logger.Instance.LogMessage(TracingLevel.INFO, "Auto transition completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error performing auto transition: {ex}");
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