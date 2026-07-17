using DevInbox.App.Services;
using DevInbox.Core.Polling;
using DevInbox.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DevInbox.App.Toasts;

public sealed class ToastService(
    SettingsStore settings,
    NotificationSoundPlayer soundPlayer,
    ILogger<ToastService> logger)
{

    public void Show(NotificationRecord record)
    {
        try
        {
            // No horário silencioso a notificação fica só no histórico.
            if (settings.Current.QuietHours.IsQuietAt(DateTime.Now.TimeOfDay))
                return;

            var notification = record.Event;
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddArgument("url", notification.Url)
                .AddArgument("notifId", record.Id.ToString())
                .AddText(EventDisplay.Headline(notification))
                .AddText($"{notification.Repo} #{notification.PrNumber} — {notification.PrTitle}");

            if (!string.IsNullOrEmpty(notification.BodyPreview))
                builder.AddText(notification.BodyPreview);

            var playChime = settings.Current.NotificationSoundEnabled;
            if (playChime)
                builder.AddAudio(new ToastAudio { Silent = true });

            if (settings.Current.LongToastDuration)
                builder.SetToastDuration(ToastDuration.Long);

            // Clicar no corpo do toast já abre o PR; só o descarte precisa de botão.
            builder
                .AddButton(new ToastButtonDismiss("Descartar"))
                .Show();

            if (playChime)
                soundPlayer.Play();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao exibir toast; a notificação permanece no histórico.");
        }
    }
}
