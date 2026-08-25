namespace ConvoHub.Models;

public enum MessageKind
{
	Markdown,
	Image,
	Video
}

public sealed class ChatMessage
{
	public Guid Id { get; init; } = Guid.NewGuid();
	public string UserName { get; init; } = string.Empty;
	public string Content { get; init; } = string.Empty;
	public MessageKind Kind { get; init; } = MessageKind.Markdown;
	public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SendMessageRequest
{
	public string Content { get; set; } = string.Empty;
	public MessageKind Kind { get; set; } = MessageKind.Markdown;
}
