namespace WebMVC.Views.Concierge;

public class ChatMessage
{
    public ChatMessageType MessageType { get; set; }

    public string Text { get; set; }

    public enum ChatMessageType { Sent, Received };
}
