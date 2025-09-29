namespace OpenVideoFramework.RtspSource;

internal class NtpTimestampConverter
{
    private static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset ConvertToDateTime(uint ntpSeconds, uint ntpFraction)
    {
        var dateTime = NtpEpoch.AddSeconds(ntpSeconds);

        if (ntpFraction <= 0) return dateTime;

        var microseconds = ntpFraction * 1e6 / (2L << 32);
        dateTime = dateTime.AddMicroseconds(microseconds);

        return dateTime;
    }
}