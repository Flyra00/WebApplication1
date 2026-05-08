using Microsoft.Extensions.Options;

namespace WebApplication1.Services.Time
{
    public sealed class BusinessTimeService : IBusinessTime
    {
        private static readonly string[] DefaultTimeZoneCandidates =
        [
            "SE Asia Standard Time",
            "Asia/Bangkok"
        ];

        private readonly TimeZoneInfo _timeZone;

        public BusinessTimeService(IOptions<BusinessTimeOptions> options, ILogger<BusinessTimeService> logger)
        {
            _timeZone = ResolveTimeZone(options.Value.TimeZoneId, logger);
        }

        public DateTime BusinessNow => ToBusinessTime(DateTime.UtcNow);

        public DateTime BusinessToday => BusinessNow.Date;

        public DateTime ToBusinessTime(DateTime value)
        {
            var utcValue = NormalizeToUtc(value);
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

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static TimeZoneInfo ResolveTimeZone(string? configuredId, ILogger logger)
        {
            foreach (var candidate in BuildCandidates(configuredId, logger))
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

            throw new InvalidOperationException(
                $"Business timezone '{configuredId}' tidak ditemukan. " +
                $"Coba gunakan salah satu dari: {string.Join(", ", DefaultTimeZoneCandidates)}.");
        }

        private static IEnumerable<string> BuildCandidates(string? configuredId, ILogger logger)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    seen.Add(value.Trim());
                }
            }

            Add(configuredId);

            if (string.Equals(configuredId, "SE Asia Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                Add("Asia/Bangkok");
            }
            else if (string.Equals(configuredId, "Asia/Bangkok", StringComparison.OrdinalIgnoreCase))
            {
                Add("SE Asia Standard Time");
            }

            foreach (var fallback in DefaultTimeZoneCandidates)
            {
                Add(fallback);
            }

            foreach (var candidate in seen)
            {
                logger.LogDebug("Trying business timezone candidate {TimeZoneId}", candidate);
                yield return candidate;
            }
        }
    }
}
