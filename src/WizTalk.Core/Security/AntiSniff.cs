using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WizTalk.Core.Security;

/// <summary>
/// Anti-Sniff кодировщик текста (см. ТЗ 3.2/6.3). Защита от «вылова» сообщений
/// снифферами в локальной сети: XOR-маска с ключом, выведенным из пары UIN'ов,
/// плюс Base64-url обёртка и сжатие Deflate для длинных сообщений.
/// Для серверного режима поверх этого канала может работать TLS — здесь реализован
/// лёгкий переносимый слой, совместимый между сборками без обмена секретами.
/// </summary>
public sealed class AntiSniffEncoder
{
    private const byte SchemeXor = 0x01;
    private const byte SchemeAes = 0x02;
    private const int CompressThreshold = 96; // короткие сообщения не имеет смысла сжимать

    private readonly ulong _localUin;
    private readonly byte[]? _aesKey;

    public bool Enabled { get; set; } = true;

    /// <param name="localUin">UIN локального клиента — участвует в выработке маски.</param>
    /// <param name="sharedSecret">Необязательный общий секрет: при наличии включается AES-128-CTR вместо чистого XOR.</param>
    public AntiSniffEncoder(ulong localUin, string? sharedSecret = null)
    {
        _localUin = localUin;
        if (!string.IsNullOrEmpty(sharedSecret))
            _aesKey = DeriveKey(sharedSecret);
    }

    private static byte[] DeriveKey(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("wiztalk-antisniff:" + secret))[..16];

    /// <summary>Кодирует payload перед отправкой. Возвращает байты, готовые к упаковке в пакет.</summary>
    public byte[] Encode(ReadOnlySpan<byte> plaintext, ulong peerUin, uint seed)
    {
        if (!Enabled)
            return plaintext.ToArray();

        var data = plaintext.Length > CompressThreshold ? Deflate(plaintext) : plaintext.ToArray();
        var mask = BuildMask(peerUin, seed);

        byte[] body;
        byte scheme;
        if (_aesKey is not null)
        {
            scheme = SchemeAes;
            body = new byte[data.Length];
            // CTR-подобный режим: AES-ECB по счётчику даёт стабильный keystream по маске+seed.
            Span<byte> counter = stackalloc byte[16];
            mask[..8].CopyTo(counter);
            BitConverter.TryWriteBytes(counter.Slice(8), seed);
            using var ecb = Aes.Create();
            ecb.Key = _aesKey;
            ecb.Mode = CipherMode.ECB;
            ecb.Padding = PaddingMode.None;
            using var enc = ecb.CreateEncryptor();
            var ks = new byte[16];
            for (var i = 0; i < data.Length; i++)
            {
                if (i % 16 == 0)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(counter, BitConverter.ToUInt64(counter) + (uint)(i / 16));
                    enc.TransformBlock(counter.ToArray(), 0, 16, ks, 0);
                }

                body[i] = (byte)(data[i] ^ ks[i % 16]);
            }
        }
        else
        {
            scheme = SchemeXor;
            body = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
                body[i] = (byte)(data[i] ^ mask[(int)(i & 7)]);
        }

        // [scheme:1][compressed:1][seedXor:4][payload...] -> base64url
        var framed = new byte[6 + body.Length];
        framed[0] = scheme;
        framed[1] = (byte)(data != plaintext ? 1 : 0);
        BitConverter.TryWriteBytes(framed.AsSpan(2), seed ^ 0x5A5A5A5A);
        body.CopyTo(framed.AsSpan(6));
        return Encoding.UTF8.GetBytes(Convert.ToBase64String(framed)
            .Replace('+', '-').Replace('/', '_').TrimEnd('='));
    }

    /// <summary>Декодирует входящий payload. При ошибке возвращает исходные байты (graceful degradation).</summary>
    public byte[] Decode(ReadOnlySpan<byte> encoded, ulong peerUin, uint seed)
    {
        if (!Enabled || encoded.Length == 0)
            return encoded.ToArray();

        try
        {
            var b64 = Encoding.UTF8.GetString(encoded).Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            var framed = Convert.FromBase64String(b64);
            if (framed.Length < 6)
                return encoded.ToArray();

            var scheme = framed[0];
            var compressed = framed[1] == 1;
            var storedSeed = BitConverter.ToUInt32(framed, 2) ^ 0x5A5A5A5A;
            var body = framed.AsSpan(6);
            var mask = BuildMask(peerUin, storedSeed != 0 ? storedSeed : seed);

            byte[] outBuf = new byte[body.Length];
            if (scheme == SchemeAes && _aesKey is not null)
            {
                using var ecb = Aes.Create();
                ecb.Key = _aesKey;
                ecb.Mode = CipherMode.ECB;
                ecb.Padding = PaddingMode.None;
                using var enc = ecb.CreateEncryptor();
                Span<byte> counter = stackalloc byte[16];
                mask[..8].CopyTo(counter);
                BitConverter.TryWriteBytes(counter.Slice(8), storedSeed != 0 ? storedSeed : seed);
                var ks = new byte[16];
                for (var i = 0; i < body.Length; i++)
                {
                    if (i % 16 == 0)
                    {
                        BitConverter.TryWriteBytes(counter, BitConverter.ToUInt64(counter) + (uint)(i / 16));
                        enc.TransformBlock(counter.ToArray(), 0, 16, ks, 0);
                    }

                    outBuf[i] = (byte)(body[i] ^ ks[i % 16]);
                }
            }
            else if (scheme == SchemeXor)
            {
                for (var i = 0; i < body.Length; i++)
                    outBuf[i] = (byte)(body[i] ^ mask[(int)(i & 7)]);
            }
            else
            {
                return encoded.ToArray(); // неизвестная схема — отдаём как есть
            }

            return compressed ? Inflate(outBuf) : outBuf;
        }
        catch (FormatException)
        {
            return encoded.ToArray(); // текст пришёл от старого клиента без кодирования
        }
        catch (CryptographicException)
        {
            return encoded.ToArray();
        }
    }

    /// <summary>
    /// Ключевая маска: смешиваем локальный и удалённый UIN golden-ratio хэшем.
    /// Обе стороны считают одинаковый массив независимо от порядка аргументов.
    /// </summary>
    private byte[] BuildMask(ulong peerUin, uint seed)
    {
        var a = _localUin;
        var b = peerUin;
        // канонonical order — чтобы sender/receiver получили одну маску
        if (a > b) (a, b) = (b, a);
        unchecked
        {
            var h = a * 0x9E3779B97F4A7C15UL ^ b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)seed * 0x165667B19E3779F9UL;
            var m = new byte[8];
            for (var i = 0; i < 8; i++)
            {
                m[i] = (byte)(h >> (i * 8));
                h = h * 0x9E3779B97F4A7C15UL + 0x6A09E667F3BCC908UL;
            }

            return m;
        }
    }

    private static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var d = new DeflateStream(ms, CompressionLevel.Fastest, true))
            d.Write(data);
        return ms.ToArray();
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var d = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        d.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>
/// IP-фильтрация входящего трафика (allow/deny списки, поддержка подсетей CIDR).
/// Используется сетевым стеком до декодирования пакета — экономия CPU на мусоре.
/// </summary>
public sealed class IpFilter
{
    private readonly List<NetworkRange> _allow = [];
    private readonly List<NetworkRange> _deny = [];

    public bool IsEmpty => _allow.Count == 0 && _deny.Count == 0;

    public void Allow(string cidrOrIp) => _allow.Add(NetworkRange.Parse(cidrOrIp));
    public void Deny(string cidrOrIp) => _deny.Add(NetworkRange.Parse(cidrOrIp));

    public void Load(IEnumerable<string> allow, IEnumerable<string> deny)
    {
        _allow.Clear();
        _deny.Clear();
        foreach (var a in allow)
            _allow.Add(NetworkRange.Parse(a));
        foreach (var d in deny)
            _deny.Add(NetworkRange.Parse(d));
    }

    /// <summary>deny приоритетнее allow; пустой allow = разрешено всё, кроме deny.</summary>
    public bool IsAllowed(System.Net.IPAddress address)
    {
        foreach (var r in _deny)
            if (r.Contains(address))
                return false;
        if (_allow.Count == 0)
            return true;
        foreach (var r in _allow)
            if (r.Contains(address))
                return true;
        return false;
    }

    private readonly record struct NetworkRange(uint Prefix, int Bits)
    {
        public static NetworkRange Parse(string text)
        {
            var parts = text.Split('/', 2);
            if (!System.Net.IPAddress.TryParse(parts[0], out var ip))
                return new NetworkRange(0, 0); // never matches
            var bytes = ip.GetAddressBytes();
            Array.Reverse(bytes); // little-endian uint
            var value = BitConverter.ToUInt32(bytes);
            var bits = parts.Length == 2 && int.TryParse(parts[1], out var b) ? Math.Clamp(b, 0, 32) : 32;
            var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
            return new NetworkRange(value & mask, bits);
        }

        public bool Contains(System.Net.IPAddress address)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;
            var bytes = address.GetAddressBytes();
            Array.Reverse(bytes);
            var value = BitConverter.ToUInt32(bytes);
            var mask = Bits == 0 ? 0u : uint.MaxValue << (32 - Bits);
            return (value & mask) == Prefix;
        }
    }
}
