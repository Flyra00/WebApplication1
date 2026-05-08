using WebApplication1.Services.Time;

namespace WebApplication1.Tests;

internal static class TestBusinessTime
{
    public static IBusinessTime Create(DateTime? businessNow = null)
    {
        return new FixedBusinessTime(businessNow ?? new DateTime(2026, 5, 8, 12, 0, 0));
    }

    private sealed class FixedBusinessTime : IBusinessTime
    {
        private readonly TimeZoneInfo _timeZone;

        public FixedBusinessTime(DateTime businessNow)
        {
            _timeZone = ResolveTimeZone();
            BusinessNow = DateTime.SpecifyKind(businessNow, DateTimeKind.Unspecified);
        }

        public DateTime BusinessNow { get; }

        public DateTime BusinessToday => BusinessNow.Date;

        public DateTime ToBusinessTime(DateTime value)
        {
            var utcValue = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };

            return TimeZoneInfo.ConvertTimeFromUtc(utcValue, _timeZone);
        }

        public DateTime ToUtc(DateTime businessLocalTime)
        {
            if (businessLocalTime.Kind == DateTimeKind.Utc)
                return businessLocalTime;

            var localValue = businessLocalTime.Kind == DateTimeKind.Unspecified
                ? businessLocalTime
                : DateTime.SpecifyKind(businessLocalTime, DateTimeKind.Unspecified);

            return TimeZoneInfo.ConvertTimeToUtc(localValue, _timeZone);
        }

        public (DateTime UtcStart, DateTime UtcEnd) GetUtcDayRange(DateTime businessDate)
        {
            var start = businessDate.Date;
            return (ToUtc(start), ToUtc(start.AddDays(1)));
        }

        private static TimeZoneInfo ResolveTimeZone()
        {
            foreach (var candidate in new[] { "SE Asia Standard Time", "Asia/Bangkok" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(candidate);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            throw new InvalidOperationException("Business timezone test fixture tidak menemukan timezone Asia/Bangkok.");
        }
    }
}
