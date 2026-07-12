using System;
using UnityEngine;

namespace UTAgent
{
    /// <summary>
    /// 解析 UTAgent 桥接返回的 JSON（Python json.dumps 会在冒号后带空格）。
    /// </summary>
    public static class UTAgentJsonResult
    {
        [Serializable]
        private sealed class ResultEnvelope
        {
            public bool success;
            public string message;
        }

        public static bool IsSuccess(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var envelope = JsonUtility.FromJson<ResultEnvelope>(json);
                return envelope.success;
            }
            catch (Exception)
            {
                return json.Contains("\"success\":true", StringComparison.Ordinal)
                    || json.Contains("\"success\": true", StringComparison.Ordinal);
            }
        }

        public static string GetMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            try
            {
                var envelope = JsonUtility.FromJson<ResultEnvelope>(json);
                if (!string.IsNullOrEmpty(envelope.message))
                {
                    return envelope.message;
                }
            }
            catch (Exception)
            {
                // JsonUtility 解析失败时退回原文
            }

            return json;
        }
    }
}
