namespace AIStoryBuilders.Model
{
    public class ChatMessage
    {
        public string Prompt { get; set; }
        public OpenAI.Chat.Role Role { get; set; }
        public string FunctionName { get; set; }
        public int Tokens { get; set; }
    }
}
