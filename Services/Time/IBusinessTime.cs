namespace WebApplication1.Services.Time
{
    public interface IBusinessTime
    {
        DateTime BusinessNow { get; }

        DateTime BusinessToday { get; }

        DateTime ToBusinessTime(DateTime value);

        DateTime ToUtc(DateTime businessLocalTime);

        (DateTime UtcStart, DateTime UtcEnd) GetUtcDayRange(DateTime businessDate);
    }
}
