using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AIStoryBuilders.Models
{
    public class OpenAiModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelInfo> Data { get; set; }
    }

    public class ModelInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Object { get; set; }

        [JsonPropertyName("owned_by")]
        public string OwnedBy { get; set; }

        // add other properties if you need them
    }

    public class OpenAiModelFetcher
    {
        private readonly HttpClient _http;

        public OpenAiModelFetcher(string apiKey)
        {
            _http = new HttpClient();
            _http.BaseAddress = new Uri("https://api.openai.com/");
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<OpenAiModelsResponse> GetModelsAsync()
        {
            using var resp = await _http.GetAsync("v1/models");
            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<OpenAiModelsResponse>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}