using Android.Speech.Tts;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AIStoryBuilders.Models
{
    public sealed class ChatResponse 
    {
        [JsonIgnore]
        public OpenAIClient Client { get; internal set; }

        [JsonIgnore]
        public TimeSpan ProcessingTime { get; internal set; }

        [JsonIgnore]
        public string Organization { get; internal set; }

        [JsonIgnore]
        public string RequestId { get; internal set; }

        [JsonIgnore]
        public string OpenAIVersion { get; internal set; }

        [JsonIgnore]
        public int? LimitRequests { get; internal set; }

        [JsonIgnore]
        public int? LimitTokens { get; internal set; }

        [JsonIgnore]
        public int? RemainingRequests { get; internal set; }

        [JsonIgnore]
        public int? RemainingTokens { get; internal set; }

        [JsonIgnore]
        public string ResetRequests { get; internal set; }

        [JsonIgnore]
        public string ResetTokens { get; internal set; }

        [JsonInclude]
        [JsonPropertyName("id")]
        public string Id { get; private set; }

        [JsonInclude]
        [JsonPropertyName("object")]
        public string Object { get; private set; }

        [JsonInclude]
        [JsonPropertyName("created")]
        public int CreatedAtUnixTimeSeconds { get; private set; }

        [JsonIgnore]
        public DateTime CreatedAt => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnixTimeSeconds).DateTime;

        [JsonInclude]
        [JsonPropertyName("model")]
        public string Model { get; private set; }

        [JsonInclude]
        [JsonPropertyName("system_fingerprint")]
        public string SystemFingerprint { get; private set; }
        [JsonIgnore]

        private List<Choice> choices;

        [JsonInclude]
        [JsonPropertyName("choices")]
        public IReadOnlyList<Choice> Choices
        {
            get
            {
                return choices;
            }
            private set
            {
                choices = value.ToList();
            }
        }

        public Choice FirstChoice => Choices?.FirstOrDefault((Choice choice) => choice.Index == 0);
        public ChatResponse()
        {
        }

        public static implicit operator string(ChatResponse response)
        {
            return response?.ToString();
        }     
    }
}
