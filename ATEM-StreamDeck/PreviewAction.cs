using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.preview")]
    public class PreviewAction : ATEMActionBase
    {
        #region Private Members

        private PreviewActionSettings settings;

        #endregion

        #region Protected Properties Override

        protected override string ATEMIPAddress => settings?.ATEMIPAddress ?? ATEMConstants.DEFAULT_ATEM_IP;
        protected override int MixEffectBlock => settings?.MixEffectBlock ?? ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
        protected override bool SupportsStateMonitoring => true;

        #endregion

        #region Constructor

        public PreviewAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        #endregion

        #region Abstract Methods Implementation

        protected override void InitializeSettings(InitialPayload payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = new PreviewActionSettings();
                this.settings.SetDefaults();
                SaveSettings();
            }
            else
            {
                this.settings = payload.Settings.ToObject<PreviewActionSettings>();
            }
        }

        protected override void InitializeDefaultSettings()
        {
            this.settings = new PreviewActionSettings();
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
            long oldInputId = settings.InputId;
            bool oldTallyForPreview = settings.TallyForPreview;
            bool oldTallyForProgram = settings.TallyForProgram;

            Tools.AutoPopulateSettings(settings, payload.Settings);

            // If IP address changed, reconnect
            if (oldIP != settings.ATEMIPAddress)
            {
                HandleReconnection();
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

        protected override void PerformAction()
        {
            SetPreviewInput();
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

        protected override void UpdateButtonStateFromCache()
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

        protected override object CreateATEMInfoPayload(ATEMSwitcherInfo switcherInfo)
        {
            return new
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
        }

        protected override void OnIPAddressChangeRequested(string newIPAddress)
        {
            settings.ATEMIPAddress = newIPAddress;
            HandleReconnection();
            SaveSettings();
        }

        #endregion

        #region Private Methods

        private void SetPreviewInput()
        {
            try
            {
                var mixEffectBlock = GetMixEffectBlock();
                if (mixEffectBlock == null) return;

                Logger.Instance.LogMessage(TracingLevel.INFO, $"Setting preview input to {settings.InputId}");
                mixEffectBlock.SetPreviewInput(settings.InputId);
                Logger.Instance.LogMessage(TracingLevel.INFO, "Preview input set successfully");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error setting preview input: {ex}");
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

        #endregion
    }
}