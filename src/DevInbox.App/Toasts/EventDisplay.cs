using DevInbox.Core.Polling;

namespace DevInbox.App.Toasts;

public static class EventDisplay
{
    public static string Headline(NotificationEvent notification) => notification.Type switch
    {
        NotificationEventType.NewComment => $"💬 Novo comentário de {notification.Actor ?? "alguém"}",
        NotificationEventType.ReviewRequested => "👀 Review solicitado para você",
        NotificationEventType.ThreadResolved => "✅ Conversa resolvida",
        NotificationEventType.ReviewReceived => notification.Subtype switch
        {
            "APPROVED" => $"✅ PR aprovado por {notification.Actor ?? "alguém"}",
            "CHANGES_REQUESTED" => $"✖ Mudanças solicitadas por {notification.Actor ?? "alguém"}",
            _ => $"💬 Review de {notification.Actor ?? "alguém"}",
        },
        NotificationEventType.ChecksFailed => "❌ Checks falharam",
        NotificationEventType.MergeConflict => "⚠ Conflito de merge com a base",
        NotificationEventType.Mentioned => $"📣 Você foi mencionado por {notification.Actor ?? "alguém"}",
        _ => "Notificação do GitHub",
    };

    public static string Label(string eventType, string? subtype) => eventType switch
    {
        nameof(NotificationEventType.NewComment) => "Comentário",
        nameof(NotificationEventType.ReviewRequested) => "Review pedido",
        nameof(NotificationEventType.ThreadResolved) => "Conversa resolvida",
        nameof(NotificationEventType.ReviewReceived) => subtype switch
        {
            "APPROVED" => "Aprovado",
            "CHANGES_REQUESTED" => "Mudanças pedidas",
            _ => "Review",
        },
        nameof(NotificationEventType.ChecksFailed) => "Checks falharam",
        nameof(NotificationEventType.MergeConflict) => "Conflito",
        nameof(NotificationEventType.Mentioned) => "Menção",
        _ => eventType,
    };
}
