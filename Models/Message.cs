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

        internal Message(Delta other)
        {
            CopyFrom(other);
        }
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

        public Message(Role role, IEnumerable<Content> content, string name = null)
        {
            Role = role;
            Content = content.ToList();
            Name = name;
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

        public Message(Tool tool, IEnumerable<Content> content)
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

        internal void CopyFrom(Delta other)
        {
            if (Role == (Role)0 && other != null && other.Role > (Role)0)
            {
                Role = other.Role;
            }

            if (other != null && other.Content != null)
            {
                Content += other.Content;
            }

            if (!string.IsNullOrWhiteSpace(other?.Name))
            {
                Name = other.Name;
            }

            if (other != null && other.ToolCalls != null)
            {
                if (toolCalls == null)
                {
                    toolCalls = new List<Tool>();
                }

                foreach (Tool toolCall in other.ToolCalls)
                {
                    if (toolCall == null)
                    {
                        continue;
                    }

                    if (toolCall.Index.HasValue)
                    {
                        if (toolCall.Index + 1 > toolCalls.Count)
                        {
                            toolCalls.Insert(toolCall.Index.Value, new Tool(toolCall));
                        }

                        toolCalls[toolCall.Index.Value].CopyFrom(toolCall);
                    }
                    else
                    {
                        toolCalls.Add(new Tool(toolCall));
                    }
                }
            }

            if (other?.Function != null)
            {
                if (Function == null)
                {
                    Function = new Function(other.Function);
                }
                else
                {
                    Function.CopyFrom(other.Function);
                }
            }
        }
    }
}
