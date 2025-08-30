using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.cutaction")]
    public class CutAction : ATEMActionBase
    {
        #region Private Members

        private BasicActionSettings settings;

        #endregion

        #region Protected Properties Override

        protected override string ATEMIPAddress => settings?.ATEMIPAddress ?? ATEMConstants.DEFAULT_ATEM_IP;
        protected override int MixEffectBlock => settings?.MixEffectBlock ?? ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
        protected override bool SupportsPropertyInspector => false;
        protected override bool SupportsStateMonitoring => false;

        #endregion

        #region Constructor

        public CutAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        #endregion

        #region Abstract Methods Implementation

        protected override void InitializeSettings(InitialPayload payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = new BasicActionSettings();
                this.settings.SetDefaults();
                SaveSettings();
            }
            else
            {
                this.settings = payload.Settings.ToObject<BasicActionSettings>();
            }
        }

        protected override void InitializeDefaultSettings()
        {
            this.settings = new BasicActionSettings();
            this.settings.SetDefaults();
        }

        protected override async Task SaveSettings()
        {
            try
            {
                await Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in SaveSettings: {ex}");
            }
        }

        protected override void HandleSettingsUpdate(ReceivedSettingsPayload payload)
        {
            string oldIP = settings.ATEMIPAddress;

            Tools.AutoPopulateSettings(settings, payload.Settings);

            // If IP address changed, reconnect
            if (oldIP != settings.ATEMIPAddress)
            {
                HandleReconnection();
            }

            SaveSettings();
        }

        protected override void PerformAction()
        {
            PerformCut();
        }

        #endregion

        #region Private Methods

        private void PerformCut()
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

                try
                {
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
                            // Release intermediate blocks we don't need
                            Marshal.ReleaseComObject(mixEffectBlock);
                        }
                    }

                    if (mixEffectBlock != null)
                    {
                        try
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, "Performing cut transition");
                            mixEffectBlock.PerformCut();
                        }
                        finally
                        {
                            // Release the mix effect block
                            Marshal.ReleaseComObject(mixEffectBlock);
                        }
                    }
                }
                finally
                {
                    // Always release the iterator
                    Marshal.ReleaseComObject(meIterator);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error performing cut: {ex}");
            }
        }

        #endregion
    }
}