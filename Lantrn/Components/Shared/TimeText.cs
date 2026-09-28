namespace Lantrn.Components.Shared;

public static class TimeText
{
    public static string Ago(DateTime? utc)
    {
        if (utc is not { } value)
        {
            return "Never";
        }

        var elapsed = DateTime.UtcNow - value;
        return elapsed.TotalMinutes < 1 ? "Just now"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes} min ago"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours} h ago"
            : elapsed.TotalDays < 30 ? $"{(int)elapsed.TotalDays} d ago"
            : value.ToLocalTime().ToString("d");
    }
}
