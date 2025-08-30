using Newtonsoft.Json;

namespace ATEM_StreamDeck
{
    /// <summary>
    /// Base settings class for ATEM actions
    /// </summary>
    public abstract class ATEMActionSettings
    {
        [JsonProperty(PropertyName = "atemIPAddress")]
        public string ATEMIPAddress { get; set; } = ATEMConstants.DEFAULT_ATEM_IP;

        [JsonProperty(PropertyName = "mixEffectBlock")]
        public int MixEffectBlock { get; set; } = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;

        /// <summary>
        /// Create default settings for the specific action type
        /// </summary>
        public abstract void SetDefaults();
    }

    /// <summary>
    /// Settings for input-based actions (Preview/Program)
    /// </summary>
    public class InputActionSettings : ATEMActionSettings
    {
        [JsonProperty(PropertyName = "inputId")]
        public long InputId { get; set; } = 1;

        [JsonProperty(PropertyName = "tallyForPreview")]
        public bool TallyForPreview { get; set; } = false;

        [JsonProperty(PropertyName = "tallyForProgram")]
        public bool TallyForProgram { get; set; } = false;

        // Backward compatibility
        [JsonProperty(PropertyName = "showTally")]
        public bool ShowTally
        {
            get => TallyForPreview || TallyForProgram;
            set
            {
                if (value)
                {
                    // Default to appropriate tally based on action type
                    TallyForPreview = value;
                }
                else
                {
                    TallyForPreview = false;
                    TallyForProgram = false;
                }
            }
        }

        public override void SetDefaults()
        {
            ATEMIPAddress = ATEMConstants.DEFAULT_ATEM_IP;
            MixEffectBlock = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
            InputId = 1;
        }
    }

    /// <summary>
    /// Settings for Preview Action
    /// </summary>
    public class PreviewActionSettings : InputActionSettings
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            TallyForPreview = true;
            TallyForProgram = false;
        }
    }

    /// <summary>
    /// Settings for Program Action
    /// </summary>
    public class ProgramActionSettings : InputActionSettings
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            TallyForPreview = false;
            TallyForProgram = true;
        }
    }

    /// <summary>
    /// Settings for transition-based actions
    /// </summary>
    public class TransitionActionSettings : ATEMActionSettings
    {
        [JsonProperty(PropertyName = "showTally")]
        public bool ShowTally { get; set; } = false;

        public override void SetDefaults()
        {
            ATEMIPAddress = ATEMConstants.DEFAULT_ATEM_IP;
            MixEffectBlock = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
            ShowTally = false;
        }
    }

    /// <summary>
    /// Settings for Set Next Transition Action
    /// </summary>
    public class SetNextTransitionActionSettings : TransitionActionSettings
    {
        [JsonProperty(PropertyName = "transitionStyle")]
        public int TransitionStyle { get; set; } = 0; // Mix transition

        [JsonProperty(PropertyName = "transitionDuration")]
        public double TransitionDuration { get; set; } = ATEMConstants.DEFAULT_TRANSITION_DURATION;

        public override void SetDefaults()
        {
            base.SetDefaults();
            TransitionStyle = 0;
            TransitionDuration = ATEMConstants.DEFAULT_TRANSITION_DURATION;
        }
    }

    /// <summary>
    /// Settings for basic actions (Cut)
    /// </summary>
    public class BasicActionSettings : ATEMActionSettings
    {
        public override void SetDefaults()
        {
            ATEMIPAddress = ATEMConstants.DEFAULT_ATEM_IP;
            MixEffectBlock = ATEMConstants.DEFAULT_MIX_EFFECT_BLOCK;
        }
    }
}