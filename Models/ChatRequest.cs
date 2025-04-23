using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AIStoryBuilders.Models
{
    public class ChatRequest
    {
        [JsonPropertyName("messages")]
        public IReadOnlyList<Message> Messages { get; }

        [JsonPropertyName("model")]
        public string Model { get; }

        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; }

        [JsonPropertyName("logit_bias")]
        public IReadOnlyDictionary<string, double> LogitBias { get; }

        [JsonPropertyName("logprobs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool? LogProbs { get; }

        [JsonPropertyName("top_logprobs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int? TopLogProbs { get; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; }

        [JsonPropertyName("n")]
        public int? Number { get; }

        [JsonPropertyName("presence_penalty")]
        public double? PresencePenalty { get; }

        [JsonPropertyName("seed")]
        public int? Seed { get; }

        [JsonPropertyName("stop")]
        public string[] Stops { get; }

        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Stream { get; internal set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; }

        [JsonPropertyName("tool_choice")]
        public dynamic ToolChoice { get; }

        [JsonPropertyName("user")]
        public string User { get; }

        [Obsolete("Use ToolChoice")]
        [JsonPropertyName("function_call")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public dynamic FunctionCall { get; }

        public ChatRequest(IEnumerable<Message> messages, IEnumerable<Tool> tools, string toolChoice = null, string model = null, double? frequencyPenalty = null, IReadOnlyDictionary<string, double> logitBias = null, int? maxTokens = null, int? number = null, double? presencePenalty = null, ChatResponseFormat responseFormat = ChatResponseFormat.Text, string[] stops = null, double? temperature = null, double? topP = null, int? topLogProbs = null, string user = null)
            : this(messages, model, frequencyPenalty, logitBias, maxTokens, number, presencePenalty, responseFormat, number, stops, temperature, topP, topLogProbs, user)
        {
            List<Tool> list = tools?.ToList();
            if (list != null && list.Any())
            {
                if (string.IsNullOrWhiteSpace(toolChoice))
                {
                    ToolChoice = "auto";
                }
                else if (!toolChoice.Equals("none") && !toolChoice.Equals("auto"))
                {
                    JsonObject jsonObject = new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = toolChoice }
                    };
                    ToolChoice = jsonObject;
                }
                else
                {
                    ToolChoice = toolChoice;
                }
            }

            Tools = list?.ToList();
        }

        public ChatRequest(IEnumerable<Message> messages, string model = null, double? frequencyPenalty = null, IReadOnlyDictionary<string, double> logitBias = null, int? maxTokens = null, int? number = null, double? presencePenalty = null, ChatResponseFormat responseFormat = ChatResponseFormat.Text, int? seed = null, string[] stops = null, double? temperature = null, double? topP = null, int? topLogProbs = null, string user = null)
        {
            Messages = messages?.ToList();
            IReadOnlyList<Message> messages2 = Messages;
            if (messages2 != null && messages2.Count == 0)
            {
                throw new ArgumentNullException("messages", "Missing required messages parameter");
            }

            Model = (string.IsNullOrWhiteSpace(model) ? ("GPT4o") : model);
            FrequencyPenalty = frequencyPenalty;
            LogitBias = logitBias;
            MaxTokens = maxTokens;
            Number = number;
            PresencePenalty = presencePenalty;
            ResponseFormat = ((ChatResponseFormat.Json == responseFormat) ? ((ResponseFormat)responseFormat) : null);
            Seed = seed;
            Stops = stops;
            Temperature = temperature;
            TopP = topP;
            LogProbs = (topLogProbs.HasValue ? new bool?(topLogProbs.Value > 0) : null);
            TopLogProbs = topLogProbs;
            User = user;
        }
    }

}
