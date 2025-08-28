namespace ATEM_StreamDeck
{
    /// <summary>
    /// Common constants used across ATEM StreamDeck plugin
    /// </summary>
    public static class ATEMConstants
    {
        // Button States
        public const string RED_BUTTON_STATE = "RED";
        public const string GREEN_BUTTON_STATE = "GREEN";
        public const string DEFAULT_BUTTON_STATE = "DEFAULT";
        
        // Default Settings
        public const string DEFAULT_ATEM_IP = "192.168.1.101";
        public const int DEFAULT_MIX_EFFECT_BLOCK = 0;
        public const double DEFAULT_TRANSITION_DURATION = 1.0;
        
        // Frame Rate Constraints
        public const uint MIN_TRANSITION_FRAMES = 1;
        public const uint MAX_TRANSITION_FRAMES = 250;
        public const double DEFAULT_FRAMERATE = 25.0;
        
        // Connection Settings
        public const int CONNECTION_RETRY_INTERVAL_SECONDS = 5;
        public const int CONNECTION_TIMEOUT_MINUTES = 5;
    }
}