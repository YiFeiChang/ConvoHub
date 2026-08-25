using ConvoHub.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ConvoHub.Service.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    [HttpGet("messages")]
    public ActionResult<IReadOnlyCollection<ChatMessage>> GetMessages() => Ok(ChatStore.GetMessages());

    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<ActionResult<ChatMessage>> Upload(IFormFile file)
    {
        if (file.Length == 0 || file.Length > 100 * 1024 * 1024)
        {
            return BadRequest("File is empty or exceeds the 100 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var videoExtensions = new[] { ".mp4", ".webm", ".mov", ".avi" };
        var kind = imageExtensions.Contains(extension) ? MessageKind.Image :
            videoExtensions.Contains(extension) ? MessageKind.Video : MessageKind.Markdown;

        if (kind == MessageKind.Markdown)
        {
            return BadRequest("Only image and video files are supported.");
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var uploadsPath = Path.Combine(AppContext.BaseDirectory, "uploads");
        Directory.CreateDirectory(uploadsPath);
        await using var stream = System.IO.File.Create(Path.Combine(uploadsPath, fileName));
        await file.CopyToAsync(stream);

        var userName = User.Identity?.Name ?? Request.Headers["X-Windows-User"].FirstOrDefault() ?? "Unknown user";
        var message = new ChatMessage
        {
            UserName = userName,
            Content = $"/uploads/{fileName}",
            Kind = kind
        };
        ChatStore.Add(message);
        await HttpContext.RequestServices.GetRequiredService<IHubContext<ChatHub>>()
            .Clients.All.SendAsync("ReceiveMessage", message);
        return Ok(message);
    }
}