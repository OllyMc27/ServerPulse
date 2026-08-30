namespace ServerPulse;

public static class AnalyticsTime
{
    public const string DefaultTimeZoneId = "Europe/London";
    private static TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public static string ConfigurationLabel { get; private set; } = DefaultTimeZoneId;

    public static bool Configure(string? requested)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? DefaultTimeZoneId : requested.Trim();
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(value);
            ConfigurationLabel = value;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return ConfigureFallback();
        }
        catch (InvalidTimeZoneException)
        {
            return ConfigureFallback();
        }
    }

    public static DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);
    public static DateTimeOffset Local(DateTimeOffset value, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
            return Local(value);
        try
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
        }
        catch
        {
            return Local(value);
        }
    }
    public static string Display(DateTimeOffset value) => Local(value).ToString("HH:mm:ss dd/MM/yyyy");
    public static string Short(DateTimeOffset value) => Local(value).ToString("dd/MM HH:mm");

    private static bool ConfigureFallback()
    {
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
        catch
        {
            _zone = TimeZoneInfo.Utc;
        }

        ConfigurationLabel = DefaultTimeZoneId;
        return false;
    }
}
