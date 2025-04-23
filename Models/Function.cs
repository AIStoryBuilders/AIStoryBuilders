using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AIStoryBuilders.Models
{
    public class Function
    {
        private string parametersString;

        private JsonNode parameters;

        private string argumentsString;

        private JsonNode arguments;

        [JsonInclude]
        [JsonPropertyName("name")]
        public string Name { get; private set; }

        [JsonInclude]
        [JsonPropertyName("description")]
        public string Description { get; private set; }

        [JsonInclude]
        [JsonPropertyName("parameters")]
        public JsonNode Parameters
        {
            get
            {
                if (parameters == null && !string.IsNullOrWhiteSpace(parametersString))
                {
                    parameters = JsonNode.Parse(parametersString);
                }

                return parameters;
            }
            private set
            {
                parameters = value;
            }
        }

        [JsonInclude]
        [JsonPropertyName("arguments")]
        public JsonNode Arguments
        {
            get
            {
                if (arguments == null && !string.IsNullOrWhiteSpace(argumentsString))
                {
                    arguments = JsonValue.Create(argumentsString);
                }

                return arguments;
            }
            private set
            {
                arguments = value;
            }
        }

        public Function()
        {
        }

        internal Function(Function other)
        {
            CopyFrom(other);
        }

        public Function(string name, string description = null, JsonNode parameters = null, JsonNode arguments = null)
        {
            Name = name;
            Description = description;
            Parameters = parameters;
            Arguments = arguments;
        }

        internal void CopyFrom(Function other)
        {
            if (!string.IsNullOrWhiteSpace(other.Name))
            {
                Name = other.Name;
            }

            if (!string.IsNullOrWhiteSpace(other.Description))
            {
                Description = other.Description;
            }

            if (other.Arguments != null)
            {
                argumentsString += other.Arguments.ToString();
            }

            if (other.Parameters != null)
            {
                parametersString += other.Parameters.ToString();
            }
        }
    }
}
