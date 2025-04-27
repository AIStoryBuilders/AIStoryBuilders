using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AIStoryBuilders.Models
{
    public sealed class Message
    {
        private List<Tool> toolCalls;

        [JsonInclude]
        [JsonPropertyName("role")]
        public Role Role { get; private set; }

        [JsonInclude]
        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public dynamic Content { get; private set; }

        [JsonInclude]
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<Tool> ToolCalls
        {
            get
            {
                return toolCalls;
            }
            private set
            {
                toolCalls = value.ToList();
            }
        }

        [JsonInclude]
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string ToolCallId { get; private set; }

        [JsonInclude]
        [Obsolete("Replaced by ToolCalls")]
        [JsonPropertyName("function_call")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Function Function { get; private set; }

        [JsonInclude]
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string Name { get; private set; }

        public Message()
        {
        }

        [Obsolete("Use new constructor args")]
        public Message(Role role, string content, string name, Function function)
            : this(role, content, name)
        {
            Name = name;
            Function = function;
        }

        public Message(Role role, string content, string name = null)
        {
            Role = role;
            Content = content;
            Name = name;
        }

        public Message(Tool tool, string content)
            : this(Role.Tool, content, tool.Function.Name)
        {
            ToolCallId = tool.Id;
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
