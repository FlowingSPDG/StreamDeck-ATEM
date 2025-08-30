using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using BMDSwitcherAPI;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.setnexttransition")]
    public class SetNextTransitionAction : ATEMActionBase
    {
        #region Private Members

        private SetNextTransitionActionSettings settings;

        #endregion

        #region Protected Properties Override

        protected override string ATEMIPAddress => settings?.ATEMIPAddress ?? ATEMConstants.DEFAULT_ATEM_IP;
        protected override int MixEffectBlock => settings?.MixEffectBlock ?? ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
        protected override bool SupportsStateMonitoring => true;

        #endregion

        #region Constructor

        public SetNextTransitionAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        #endregion

        #region Abstract Methods Implementation

        protected override void InitializeSettings(InitialPayload payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = new SetNextTransitionActionSettings();
                this.settings.SetDefaults();
                SaveSettings();
            }
            else
            {
                this.settings = payload.Settings.ToObject<SetNextTransitionActionSettings>();
            }
        }

        protected override void InitializeDefaultSettings()
        {
            this.settings = new SetNextTransitionActionSettings();
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

            Tools.AutoPopulateSettings(settings, payload.Settings);

            // If IP address changed, reconnect
            if (oldIP != settings.ATEMIPAddress)
            {
                HandleReconnection();
            }
            // If Mix Effect block or tally setting changed, update state monitoring
            else if (oldMixEffectBlock != settings.MixEffectBlock || oldShowTally != settings.ShowTally)
            {
                UpdateButtonStateFromCache();
            }

            SaveSettings();
        }

        protected override void PerformAction()
        {
            SetNextTransition();
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

        protected override object CreateATEMInfoPayload(ATEMSwitcherInfo switcherInfo)
        {
            return new
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
        }

        protected override void OnIPAddressChangeRequested(string newIPAddress)
        {
            settings.ATEMIPAddress = newIPAddress;
            HandleReconnection();
            SaveSettings();
        }

        #endregion

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
                var mixEffectBlock = GetMixEffectBlock();
                if (mixEffectBlock == null) return;

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

        #endregion
    }
}