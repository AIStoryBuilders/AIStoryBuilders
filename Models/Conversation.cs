using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AIStoryBuilders.Models
{
    public class Conversation
    {
        private readonly List<Message> messages;

        [JsonPropertyName("messages")]
        public IReadOnlyList<Message> Messages => messages;

        [JsonConstructor]
        public Conversation(List<Message> messages)
        {
            this.messages = messages;
        }

        public void AppendMessage(Message message)
        {
            messages.Add(message);
        }

        // Hold the special options here:
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Converters =
            {
            // Emit enum names as 'system','user','assistant',…
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
            }
        };

        public override string ToString()
        {
            return JsonSerializer.Serialize(this, _jsonOptions);
        }

        public static implicit operator string(Conversation conversation)
        {
            return conversation?.ToString();
        }
    }
}
