using System;

namespace GithubAccelerator.UI.Services;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public class NotificationMessage
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.Info;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string TypeIcon => Type switch
    {
        NotificationType.Info => "ℹ️",
        NotificationType.Success => "✅",
        NotificationType.Warning => "⚠️",
        NotificationType.Error => "❌",
        _ => "ℹ️"
    };
}

public class NotificationService
{
    private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());
    public static NotificationService Instance => _instance.Value;

    public event Action<NotificationMessage>? OnNotification;

    public bool NotificationsEnabled { get; set; } = true;

    private NotificationService() { }

    public void Notify(string title, string message, NotificationType type = NotificationType.Info)
    {
        if (!NotificationsEnabled) return;

        var notification = new NotificationMessage
        {
            Title = title,
            Message = message,
            Type = type,
            Timestamp = DateTime.Now
        };

        OnNotification?.Invoke(notification);
    }

    public void Info(string title, string message) => Notify(title, message, NotificationType.Info);
    public void Success(string title, string message) => Notify(title, message, NotificationType.Success);
    public void Warning(string title, string message) => Notify(title, message, NotificationType.Warning);
    public void Error(string title, string message) => Notify(title, message, NotificationType.Error);
}
