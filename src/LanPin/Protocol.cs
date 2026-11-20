using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanPin;

static class Subnet
{
    public static uint Number(IPAddress ip) => BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
    public static bool IsPrivate(uint n) => n >> 24 == 10 || n >> 20 == 0xac1 || n >> 16 == 0xc0a8;
}

static class Protocol
{
    public static string NewPin() => string.Join("-", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).Chunk(8).Select(x => new string(x)));
    public static byte[] Pin(string pin)
    {
        var s = pin.Replace("-", "").Replace(" ", "").Trim();
        if (s.Length != 32) throw new ArgumentException("PIN 应为 32 位十六进制字符（可含分隔横线）");
        return Convert.FromHexString(s);
    }
    public static X509Certificate2 Certificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=LanPin ephemeral", key, HashAlgorithmName.SHA256);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // Import into a temporary Windows key container for Schannel compatibility on Windows 10.
        var pfx = generated.Export(X509ContentType.Pfx);
        try { return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    static byte[] Proof(byte[] pin, byte role, byte[] binding, byte[] a, byte[] b) =>
        HMACSHA256.HashData(pin, new byte[] { 1, role }.Concat(binding).Concat(a).Concat(b).ToArray());
    public static async Task Authenticate(SslStream stream, byte[] pin, bool server, byte[] certificate, CancellationToken ct)
    {
        // Bind both proofs to the TLS server certificate to prevent relaying through a different TLS endpoint.
        byte[] local = RandomNumberGenerator.GetBytes(32), remote = new byte[32];
        await stream.WriteAsync(local, ct);
        await stream.ReadExactlyAsync(remote, ct);
        var a = server ? local : remote; var b = server ? remote : local;
        var binding = SHA256.HashData(certificate);
        await stream.WriteAsync(Proof(pin, server ? (byte)1 : (byte)2, binding, a, b), ct);
        byte[] received = new byte[32]; await stream.ReadExactlyAsync(received, ct);
        if (!CryptographicOperations.FixedTimeEquals(received, Proof(pin, server ? (byte)2 : (byte)1, binding, a, b)))
            throw new AuthenticationException("PIN 不匹配或连接验证失败");
    }
    public static async Task WriteFrame(Stream s, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (bytes.Length is < 1 or > 65535) throw new IOException("帧长度无效");
        byte[] h = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(h, (ushort)bytes.Length);
        await s.WriteAsync(h, ct); await s.WriteAsync(bytes, ct);
    }
    public static async Task<byte[]> ReadFrame(Stream s, CancellationToken ct)
    {
        byte[] h = new byte[2]; await s.ReadExactlyAsync(h, ct);
        int n = BinaryPrimitives.ReadUInt16BigEndian(h);
        if (n == 0) throw new IOException("空帧");
        var data = new byte[n]; await s.ReadExactlyAsync(data, ct); return data;
    }
}
