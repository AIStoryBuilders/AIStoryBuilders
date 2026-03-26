using System;
using System.Text.Json.Serialization;

namespace AIStoryBuilders.Models
{
    public sealed class Message
    {
        [JsonInclude]
        [JsonPropertyName("role")]
        public Role Role { get; private set; }

        [JsonInclude]
        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public dynamic Content { get; private set; }

        [JsonInclude]
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string Name { get; private set; }

        public Message()
        {
        }

        public Message(Role role, string content, string name = null)
        {
            Role = role;
            Content = content;
            Name = name;
        }

        public override string ToString()
        {
            return Content?.ToString() ?? string.Empty;
        }

        public static implicit operator string(Message message)
        {
            return message?.ToString();
        }    
      
    }
}
