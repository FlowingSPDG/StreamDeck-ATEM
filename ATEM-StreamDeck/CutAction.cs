using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.cutaction")]
    public class CutAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.ATEMIPAddress = "192.168.1.101";
                instance.MixEffectBlock = 0;
                return instance;
            }

            [JsonProperty(PropertyName = "atemIPAddress")]
            public string ATEMIPAddress { get; set; }

            [JsonProperty(PropertyName = "mixEffectBlock")]
            public int MixEffectBlock { get; set; }
        }

        #region Private Members

        private PluginSettings settings;
        private ATEMConnection connection;
        private bool isRetrying = false;

        #endregion

        public CutAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "CutAction constructor called");
                
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
                
                Logger.Instance.LogMessage(TracingLevel.INFO, "CutAction constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in CutAction constructor: {ex}");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, $"CutAction Dispose called");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, "Cut Action - Key Pressed");
                PerformCut();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload) 
        {
            // Cut action is performed on key press, no action needed on release
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

        private async void PerformCut()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "ATEM not connected, cannot perform cut");
                    return;
                }

                var switcher = connection.Switcher;
                if (switcher == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "Failed to get switcher");
                    return;
                }

                // Create a mix effect block iterator
                IntPtr meIteratorPtr;
                switcher.CreateIterator(typeof(IBMDSwitcherMixEffectBlockIterator).GUID, out meIteratorPtr);
                IBMDSwitcherMixEffectBlockIterator meIterator = Marshal.GetObjectForIUnknown(meIteratorPtr) as IBMDSwitcherMixEffectBlockIterator;
                if (meIterator == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "Failed to create mix effect block iterator");
                    return;
                }

                // Get the mix effect block
                IBMDSwitcherMixEffectBlock mixEffectBlock = null;
                for (int i = 0; i <= settings.MixEffectBlock; i++)
                {
                    meIterator.Next(out mixEffectBlock);
                    if (mixEffectBlock == null)
                    {
                        Logger.Instance.LogMessage(TracingLevel.ERROR, $"Mix Effect Block {settings.MixEffectBlock} not found");
                        return;
                    }
                    if (i < settings.MixEffectBlock)
                    {
                        Marshal.ReleaseComObject(mixEffectBlock);
                    }
                }

                Logger.Instance.LogMessage(TracingLevel.INFO, "Performing cut transition");
                mixEffectBlock.PerformCut();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error performing cut: {ex}");
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