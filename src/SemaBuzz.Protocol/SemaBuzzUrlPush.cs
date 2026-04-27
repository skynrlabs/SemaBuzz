using System.Text;

namespace SemaBuzz.Protocol;

/// <summary>
/// Variable-length wire format for a URL push ("Walk the Web").
/// Format:
///   [0x53][0x42]    2-byte magic
///   [0x09]          url-push type marker
///   [url_len:u16le] URL length in bytes
///   [url:utf8]      URL bytes
/// </summary>
public static class SemaBuzzUrlPush
{
    internal const byte UrlPushByte = 0x09;
    private const int MaxUrlBytes = 2048;

    public static bool IsUrlPushPacket(byte[] data) =>
        data.Length >= 5 &&
        data[0] == SemaBuzzPacket.MagicByte1 &&
        data[1] == SemaBuzzPacket.MagicByte2 &&
        data[2] == UrlPushByte;

    public static byte[] Serialize(string url)
    {
        var urlBytes = Encoding.UTF8.GetBytes(url);
        if (urlBytes.Length > MaxUrlBytes)
            urlBytes = urlBytes[..MaxUrlBytes];

        var buf = new byte[3 + 2 + urlBytes.Length];
        buf[0] = SemaBuzzPacket.MagicByte1;
        buf[1] = SemaBuzzPacket.MagicByte2;
        buf[2] = UrlPushByte;
        buf[3] = (byte)(urlBytes.Length & 0xFF);
        buf[4] = (byte)((urlBytes.Length >> 8) & 0xFF);
        urlBytes.CopyTo(buf, 5);
        return buf;
    }

    public static string? Deserialize(byte[] data)
    {
        if (!IsUrlPushPacket(data)) return null;
        if (data.Length < 5) return null;

        var urlLen = data[3] | (data[4] << 8);
        if (urlLen <= 0 || urlLen > MaxUrlBytes) return null;
        if (data.Length < 5 + urlLen) return null;

        var url = Encoding.UTF8.GetString(data, 5, urlLen);

        // Validate: must be http or https
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        return url;
    }
}
