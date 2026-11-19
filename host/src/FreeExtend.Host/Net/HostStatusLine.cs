using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeExtend.Host.Net;

public enum HostStatusKind { Waiting, Connected, Fps, Disconnected }

public readonly record struct HostStatus(HostStatusKind Kind, int Width = 0, int Height = 0, int Hz = 0, double Fps = 0);

/// <summary>
/// Machine-readable status lines that <c>stream --status</c> prints on stdout so a controlling program (the GUI) does not have to parse
/// human text: <c>@@fx waiting</c>, <c>@@fx connected 2304x1440@60</c>, <c>@@fx fps 59.8</c>, <c>@@fx disconnected</c>.
/// </summary>
public static class HostStatusLine
{
    public const string Prefix = "@@fx ";
    private static readonly Regex ModeRegex = new(@"^(\d{1,5})x(\d{1,5})@(\d{1,4})$", RegexOptions.Compiled);

    public static string Format(HostStatus s) => s.Kind switch
    {
        HostStatusKind.Waiting => Prefix + "waiting",
        HostStatusKind.Disconnected => Prefix + "disconnected",
        HostStatusKind.Connected => string.Create(CultureInfo.InvariantCulture, $"{Prefix}connected {s.Width}x{s.Height}@{s.Hz}"),
        HostStatusKind.Fps => string.Create(CultureInfo.InvariantCulture, $"{Prefix}fps {s.Fps}"),
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static bool TryParse(string? line, out HostStatus status)
    {
        status = default;
        if (line is null || !line.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = line[Prefix.Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        switch (parts[0])
        {
            case "waiting" when parts.Length == 1:
                status = new HostStatus(HostStatusKind.Waiting);
                return true;
            case "disconnected" when parts.Length == 1:
                status = new HostStatus(HostStatusKind.Disconnected);
                return true;
            case "connected" when parts.Length == 2 && ModeRegex.Match(parts[1]) is { Success: true } m:
                status = new HostStatus(HostStatusKind.Connected, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                return true;
            case "fps" when parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var fps):
                status = new HostStatus(HostStatusKind.Fps, Fps: fps);
                return true;
            default:
                return false;
        }
    }
}
