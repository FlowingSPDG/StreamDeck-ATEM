using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ATEM_StreamDeck
{
    [PluginActionId("dev.flowingspdg.atem.keyaction")]
    public class KeyAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings();
                instance.OutputFileName = String.Empty;
                instance.InputString = String.Empty;
                return instance;
            }

            [FilenameProperty]
            [JsonProperty(PropertyName = "outputFileName")]
            public string OutputFileName { get; set; }

            [JsonProperty(PropertyName = "inputString")]
            public string InputString { get; set; }
        }

        #region Private Members

        private PluginSettings settings;

        #endregion
        public KeyAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "KeyAction constructor called");
                
                if (payload.Settings == null || payload.Settings.Count == 0)
                {
                    this.settings = PluginSettings.CreateDefaultSettings();
                    SaveSettings();
                }
                else
                {
                    this.settings = payload.Settings.ToObject<PluginSettings>();
                }
                
                Logger.Instance.LogMessage(TracingLevel.INFO, "KeyAction constructor completed");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyAction constructor: {ex}");
                this.settings = PluginSettings.CreateDefaultSettings();
            }
        }

        public override void Dispose()
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"KeyAction Dispose called");
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
                Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");
                // ここに実際の処理を追加
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyPressed: {ex}");
            }
        }

        public override void KeyReleased(KeyPayload payload) 
        {
            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "Key Released");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in KeyReleased: {ex}");
            }
        }

        public override void OnTick() 
        {
            // OnTick は頻繁に呼ばれるのでコメントアウト
            // Logger.Instance.LogMessage(TracingLevel.INFO, "OnTick called");
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            try
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error in ReceivedSettings: {ex}");
            }
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        #region Private Methods

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