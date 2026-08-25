using System.Collections.Concurrent;
using ConvoHub.Models;

namespace ConvoHub.Service;

public static class ChatStore
{
    private static readonly ConcurrentQueue<ChatMessage> Messages = new();

    public static IReadOnlyCollection<ChatMessage> GetMessages() => Messages.ToArray();

    public static void Add(ChatMessage message)
    {
        Messages.Enqueue(message);
        while (Messages.Count > 200 && Messages.TryDequeue(out _))
        {
        }
    }
}