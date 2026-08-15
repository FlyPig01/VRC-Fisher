using System.Globalization;

namespace VrcFisher.Application;

public static class DataSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public static string Format(long bytes)
    {
        var unit = SelectUnit(bytes);
        return $"{Scale(bytes, unit).ToString("N1", CultureInfo.CurrentCulture)} {Units[unit]}";
    }

    public static string FormatProgress(long downloadedBytes, long totalBytes)
    {
        var unit = SelectUnit(totalBytes > 0 ? totalBytes : downloadedBytes);
        var downloaded = Scale(Math.Max(0, downloadedBytes), unit)
            .ToString("N1", CultureInfo.CurrentCulture);
        var total = totalBytes > 0
            ? Scale(totalBytes, unit).ToString("N1", CultureInfo.CurrentCulture)
            : "?";
        return $"{downloaded} / {total} {Units[unit]}";
    }

    private static int SelectUnit(long bytes)
    {
        var value = Math.Max(0, bytes);
        if (value >= 1024L * 1024 * 1024) return 3;
        if (value >= 1024L * 1024) return 2;
        if (value >= 1024L) return 1;
        return 0;
    }

    private static double Scale(long bytes, int unit) => unit switch
    {
        3 => bytes / (1024d * 1024 * 1024),
        2 => bytes / (1024d * 1024),
        1 => bytes / 1024d,
        _ => bytes
    };
}
