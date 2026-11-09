namespace Travela.Api.Notifications;

public class QueueWorkerOptions
{
    public int PollIntervalSeconds { get; set; } = 2;
    public int IdleIntervalSeconds { get; set; } = 10;
    public int ErrorIntervalSeconds { get; set; } = 30;

    public bool Valid => PollIntervalSeconds is >= 1 and <= 3600 &&
        IdleIntervalSeconds >= PollIntervalSeconds && IdleIntervalSeconds <= 3600 &&
        ErrorIntervalSeconds >= IdleIntervalSeconds && ErrorIntervalSeconds <= 3600;
}

public sealed class TravelNotificationWorkerOptions : QueueWorkerOptions
{
    public TravelNotificationWorkerOptions()
    {
        PollIntervalSeconds = 15;
        IdleIntervalSeconds = 30;
    }

    public int ReminderIntervalSeconds { get; set; } = 60;
}
