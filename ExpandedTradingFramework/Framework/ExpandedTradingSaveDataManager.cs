using Newtonsoft.Json;

namespace ExpandedTradingFramework.Framework
{
    internal sealed class ExpandedTradingSaveDataManager
    {
        private readonly ModDataManager m_DataManager = new("ExpandedTradingFramework", true);
        private readonly JsonSerializerSettings m_Settings = new()
        {
            TypeNameHandling = TypeNameHandling.None
        };

        internal void Save(ExpandedTradingSaveDataProxy data)
        {
            try
            {
                string dataString = JsonConvert.SerializeObject(data ?? new ExpandedTradingSaveDataProxy(), m_Settings);
                m_DataManager.Save(dataString);
            }
            catch (Exception e)
            {
                Core.Log?.Error($"Could not save Expanded Trading Framework data: {e}");
            }
        }

        internal ExpandedTradingSaveDataProxy Load()
        {
            try
            {
                string dataString = m_DataManager.Load();
                if (string.IsNullOrWhiteSpace(dataString)) return new ExpandedTradingSaveDataProxy();

                ExpandedTradingSaveDataProxy data = JsonConvert.DeserializeObject<ExpandedTradingSaveDataProxy>(dataString, m_Settings);
                return data ?? new ExpandedTradingSaveDataProxy();
            }
            catch (Exception e)
            {
                Core.Log?.Warning($"Could not load Expanded Trading Framework data. Starting with empty framework state for this session. Details: {e.Message}");
                return new ExpandedTradingSaveDataProxy();
            }
        }
    }
}