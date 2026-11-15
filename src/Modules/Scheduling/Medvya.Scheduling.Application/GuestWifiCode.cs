using System.Text;

namespace Medvya.Scheduling.Application;

/// <summary>The text of a Wi-Fi join QR code (the WIFI: scheme iPhone and Android cameras read), for the token slip's stub (MDV-P1-111).</summary>
public static class GuestWifiCode
{
    /// <summary>WPA with a password, an open network without one. \ ; , : and " are escaped with a backslash, or a phone joins the wrong network.</summary>
    public static string Text(string network, string? password) => string.IsNullOrEmpty(password)
        ? $"WIFI:T:nopass;S:{Escape(network)};;"
        : $"WIFI:T:WPA;S:{Escape(network)};P:{Escape(password)};;";

    static string Escape(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (var c in value) text.Append(c is '\\' or ';' or ',' or ':' or '"' ? $"\\{c}" : c.ToString());
        return text.ToString();
    }
}
