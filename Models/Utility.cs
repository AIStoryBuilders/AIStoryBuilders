using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuilders.Model
{
    public class Utility
    {
        #region private List<Message> AddExistingChatMessags(List<Message> chatPrompts, string SystemMessage)
        private List<Message> AddExistingChatMessags(List<Message> chatPrompts, string SystemMessage)
        {
            List<ChatMessage> ChatMessages = new List<ChatMessage>();

            // Create a new LinkedList of ChatMessages
            LinkedList<ChatMessage> ChatPromptsLinkedList = new LinkedList<ChatMessage>();

            // Loop through the ChatMessages and add them to the LinkedList
            foreach (var item in ChatMessages)
            {
                // Do not add the system message to the chat prompts
                // because we will add this manully later
                if (item.Prompt == SystemMessage)
                {
                    continue;
                }
                ChatPromptsLinkedList.AddLast(item);
            }

            // Set the current word count to 0
            int CurrentWordCount = 0;

            // Reverse the chat messages to start from the most recent messages
            foreach (var item in ChatPromptsLinkedList.Reverse())
            {
                if (item.Prompt != null)
                {
                    int promptWordCount = item.Prompt.Split(
                        new char[] { ' ', '\t', '\n', '\r' },
                        StringSplitOptions.RemoveEmptyEntries).Length;

                    if (CurrentWordCount + promptWordCount >= 1000)
                    {
                        // This message would cause the total to exceed 1000 words,
                        // so break out of the loop
                        break;
                    }
                    // Add the message to the chat prompts
                    chatPrompts.Insert(
                        0,
                        new Message(item.Role, item.Prompt, item.FunctionName));
                    CurrentWordCount += promptWordCount;
                }
            }

            // Add the first message to the chat prompts to indicate the System message
            chatPrompts.Insert(0,
                new Message(
                    Role.System,
                    SystemMessage
                )
            );

            return chatPrompts;
        }
        #endregion
    }
}
