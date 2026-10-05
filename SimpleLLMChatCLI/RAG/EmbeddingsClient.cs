using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace SimpleLLMChatCLI.RAG
{
    public class EmbeddingsException : Exception
    {
        public EmbeddingsException(string message) : base(message) { }
    }

    /// <summary>
    /// OpenAI-compatible embeddings client with optional curl HTTPS fallback.
    /// </summary>
    public class EmbeddingsClient
    {
        public const int BatchSize = 32;

        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;

        public EmbeddingsClient(string endpoint, string apiKey, string model)
        {
            _endpoint = (endpoint ?? string.Empty).Trim();
            _apiKey = apiKey ?? string.Empty;
            _model = (model ?? string.Empty).Trim();
        }

        public static EmbeddingsClient CreateFromConfig(ConfigHandler config)
        {
            if (config == null)
                return null;
            string endpoint = config.GetConfigValue("embeddingsEndpoint");
            if (string.IsNullOrWhiteSpace(endpoint))
                endpoint = config.GetConfigValue("llmserver");
            string apiKey = config.GetConfigValue("embeddingsApiKey");
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = config.GetConfigValue("apikey");
            string model = config.GetConfigValue("embeddingsModel");
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
                return null;
            return new EmbeddingsClient(endpoint, apiKey, model);
        }

        public float[] Embed(string text)
        {
            List<float[]> result = EmbedBatch(new List<string> { text ?? string.Empty });
            return result.Count > 0 ? result[0] : null;
        }

        public List<float[]> EmbedBatch(IList<string> texts)
        {
            List<float[]> results = new List<float[]>();
            if (texts == null || texts.Count == 0)
                return results;

            for (int start = 0; start < texts.Count; start += BatchSize)
            {
                int count = Math.Min(BatchSize, texts.Count - start);

                JArray input = new JArray();
                for (int i = 0; i < count; i++)
                    input.Add(texts[start + i] ?? string.Empty);

                JObject payload = new JObject();
                payload["model"] = _model;
                payload["input"] = input;

                string error;
                string json = PostEmbeddings(payload, out error);
                if (json == null)
                    throw new EmbeddingsException(error ?? "Unknown embeddings error.");

                float[][] batch = ParseBatch(json, count);
                for (int i = 0; i < count; i++)
                    results.Add(batch[i]);
            }

            return results;
        }

        private static float[][] ParseBatch(string json, int expectedCount)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex) { throw new EmbeddingsException("Invalid embeddings response: " + ex.Message); }

            JToken errToken = root["error"];
            if (errToken != null && errToken.Type != JTokenType.Null)
                throw new EmbeddingsException("Embeddings API error: " + errToken.ToString());

            JArray data = root["data"] as JArray;
            if (data == null)
                throw new EmbeddingsException("Embeddings response missing 'data' array.");

            float[][] batch = new float[expectedCount][];
            int sequential = 0;

            foreach (JToken token in data)
            {
                JObject item = token as JObject;
                if (item == null)
                    continue;

                JArray embedding = item["embedding"] as JArray;
                if (embedding == null)
                    continue;

                float[] vector = new float[embedding.Count];
                for (int i = 0; i < embedding.Count; i++)
                    vector[i] = (float)embedding[i];

                int index;
                JToken indexToken = item["index"];
                if (indexToken != null && indexToken.Type == JTokenType.Integer)
                    index = (int)indexToken;
                else
                    index = sequential;

                if (index >= 0 && index < expectedCount)
                    batch[index] = vector;

                sequential++;
            }

            for (int i = 0; i < expectedCount; i++)
            {
                if (batch[i] == null)
                    throw new EmbeddingsException("Embeddings response returned fewer vectors than requested.");
            }

            return batch;
        }

        private string PostEmbeddings(JObject payload, out string error)
        {
            string url = _endpoint.TrimEnd('/') + "/embeddings";
            string detail;
            string body = JsonHttpClient.Post(url, _apiKey, payload.ToString(Formatting.None), 120000, out detail);
            if (body != null)
            {
                error = null;
                return body;
            }

            if (detail != null && detail.StartsWith("curl: "))
                error = "Embeddings request failed (curl): " + detail.Substring("curl: ".Length);
            else
                error = "Embeddings request failed: " + detail;
            return null;
        }

    }
}
