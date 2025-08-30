using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.autotransition")]
    public class AutoTransitionAction : ATEMActionBase
    {
        #region Private Members

        private TransitionActionSettings settings;

        #endregion

        #region Protected Properties Override

        protected override string ATEMIPAddress => settings?.ATEMIPAddress ?? ATEMConstants.DEFAULT_ATEM_IP;
        protected override int MixEffectBlock => settings?.MixEffectBlock ?? ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
        protected override bool SupportsStateMonitoring => true;

        #endregion

        #region Constructor

        public AutoTransitionAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        #endregion

        #region Abstract Methods Implementation

        protected override void InitializeSettings(InitialPayload payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = new TransitionActionSettings();
                this.settings.SetDefaults();
                SaveSettings();
            }
            else
            {
                try
                {
                    this.settings = payload.Settings.ToObject<TransitionActionSettings>();
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error deserializing settings, using safe conversion: {ex}");
                    this.settings = SafeConvertSettings(payload.Settings);
                }
            }
        }

        protected override void InitializeDefaultSettings()
        {
            this.settings = new TransitionActionSettings();
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
            int oldMixEffectBlock = settings.MixEffectBlock;
            bool oldShowTally = settings.ShowTally;

            try
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in AutoPopulateSettings, using safe conversion: {ex}");
                var safeSettings = SafeConvertSettings(payload.Settings);
                this.settings = safeSettings;
            }

            // If IP address changed, reconnect
            if (oldIP != settings.ATEMIPAddress)
            {
                HandleReconnection();
            }
            // If Mix Effect Block or tally setting changed, update button state
            else if (oldMixEffectBlock != settings.MixEffectBlock || oldShowTally != settings.ShowTally)
            {
                UpdateButtonStateFromCache();
            }

            SaveSettings();
        }

        protected override void PerformAction()
        {
            PerformAutoTransition();
        }

        #endregion

        #region Virtual Methods Override

        protected override void OnATEMStateChanged(object sender, ATEMStateChangeEventArgs e)
        {
            try
            {
                // Only handle events for our specific switcher and Mix Effect block
                if (e.IPAddress != settings.ATEMIPAddress || e.MixEffectIndex != settings.MixEffectBlock)
                    return;

                if (e.EventType == ATEMEventType.TransitionStateChanged)
                {
                    bool newTransitionState = (bool)e.NewValue;
                    Logger.Instance.LogMessage(TracingLevel.INFO, 
                        $"Transition state changed to {(newTransitionState ? "IN TRANSITION" : "IDLE")} for ME {settings.MixEffectBlock}");
                    
                    // Update button state based on the new cached state
                    UpdateButtonStateFromCache();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error handling ATEM state change: {ex}");
            }
        }

        protected override void UpdateButtonStateFromCache()
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
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Button image set to RED (in transition)");
                }
                else
                {
                    // Set button to default image when not in transition
                    Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Button image set to DEFAULT (idle)");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error updating button state from cache: {ex}");
                // Fallback to default image on error
                Connection.SetImageAsync(ATEMConstants.DEFAULT_IMAGE);
            }
        }

        protected override void OnIPAddressChangeRequested(string newIPAddress)
        {
            settings.ATEMIPAddress = newIPAddress;
            HandleReconnection();
            SaveSettings();
        }

        #endregion

        #region Private Methods

        private void PerformAutoTransition()
        {
            try
            {
                var mixEffectBlock = GetMixEffectBlock();
                if (mixEffectBlock == null) return;

                Logger.Instance.LogMessage(TracingLevel.INFO, "Performing auto transition with current transition settings");
                mixEffectBlock.PerformAutoTransition();
                Logger.Instance.LogMessage(TracingLevel.INFO, "Auto transition completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error performing auto transition: {ex}");
            }
        }

        private bool GetCurrentTransitionState()
        {
            try
            {
                if (connection == null || !connection.IsConnected)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"Connection not available for ME {settings.MixEffectBlock}, returning false");
                    return false;
                }

                var switcherState = ATEMConnectionManager.Instance.GetSwitcherState(settings.ATEMIPAddress);
                var meState = switcherState.GetMixEffectState(settings.MixEffectBlock);

                bool isInTransition = meState.IsInTransition;
                Logger.Instance.LogMessage(TracingLevel.DEBUG, 
                    $"ME {settings.MixEffectBlock} transition state: {isInTransition}");
                
                return isInTransition;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error getting current transition state: {ex}");
                return false;
            }
        }

        private TransitionActionSettings SafeConvertSettings(JObject settingsObject)
        {
            var result = new TransitionActionSettings();
            result.SetDefaults();

            try
            {
                // Safe conversion for ATEM IP Address
                if (settingsObject["atemIPAddress"] != null)
                {
                    result.ATEMIPAddress = settingsObject["atemIPAddress"].ToString();
                }

                // Safe conversion for Mix Effect Block
                if (settingsObject["mixEffectBlock"] != null)
                {
                    if (int.TryParse(settingsObject["mixEffectBlock"].ToString(), out int meBlock))
                    {
                        result.MixEffectBlock = meBlock;
                    }
                }

                // Safe conversion for ShowTally
                if (settingsObject["showTally"] != null)
                {
                    var tallyValue = settingsObject["showTally"].ToString().ToLowerInvariant();
                    result.ShowTally = tallyValue == "true" || tallyValue == "on" || tallyValue == "1";
                }

                Logger.Instance.LogMessage(TracingLevel.INFO, $"Safe conversion completed: IP={result.ATEMIPAddress}, ME={result.MixEffectBlock}, ShowTally={result.ShowTally}");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in safe conversion, using defaults: {ex}");
            }

            return result;
        }

        #endregion
    }
}