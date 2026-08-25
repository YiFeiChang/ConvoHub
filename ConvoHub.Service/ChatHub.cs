using ConvoHub.Models;
using Microsoft.AspNetCore.SignalR;

namespace ConvoHub.Service;

public sealed class ChatHub : Hub
{
    public async Task SendMessage(SendMessageRequest request)
    {
        var userName = Context.User?.Identity?.Name
            ?? Context.GetHttpContext()?.Request.Headers["X-Windows-User"].FirstOrDefault()
            ?? "Unknown user";

        var message = new ChatMessage
        {
            UserName = userName,
            Content = request.Content.Trim(),
            Kind = request.Kind
        };

        if (message.Content.Length == 0)
        {
            return;
        }

        ChatStore.Add(message);
        await Clients.All.SendAsync("ReceiveMessage", message);
    }
}