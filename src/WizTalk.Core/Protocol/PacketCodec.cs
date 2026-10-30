using System.Buffers.Binary;
using System.Text;

namespace WizTalk.Core.Protocol;

/// <summary>
/// Бинарный кодек пакетов WizTalk. Формат (little-endian, см. раздел 4 ТЗ):
/// <code>
/// ┌──────────────────────────────────────────────────────────┐
/// │ Magic "WZTK" (4) │ Version(1) │ Type(2) │ Flags(1)       │
/// │ SenderUin(8)     │ Sequence(4)│ Timestamp(8)             │
/// │ PayloadLength(4) │ Payload(N) │ Crc32(4)                 │
/// └──────────────────────────────────────────────────────────┘
/// </code>
/// Заголовок занимает <see cref="HeaderSize"/> = 32 байта, CRC-32 считается по
/// заголовку и полезной нагрузке. Пакеты v1 (без поля Flags/Timestamp) читаются
/// через <see cref="TryDecodeLegacy"/> для обратной совместимости со старыми сборками.
/// </summary>
public static partial class PacketCodec
{
    /// <summary>Магическая сигнатура протокола.</summary>
    public const uint Magic = 0x4B545A57; // "WZTK" little-endian

    /// <summary>Текущая версия протокола.</summary>
    public const byte CurrentVersion = 2;

    /// <summary>Минимальная поддерживаемая версия (обратная совместимость).</summary>
    public const byte MinSupportedVersion = 1;

    /// <summary>Размер заголовка v2 в байтах.</summary>
    public const int HeaderSize = 32;

    /// <summary>Жёсткий предел размера пакета — защита от переполнения буфера (см. 6.3).</summary>
    public const int MaxPacketSize = 4 * 1024 * 1024;

    /// <summary>Флаг: payload сжат Deflate.</summary>
    public const byte FlagCompressed = 0x01;

    /// <summary>Флаг: payload зашифрован anti-sniff кодировщиком.</summary>
    public const byte FlagEncrypted = 0x02;

    /// <summary>Флаг: пакет адресован группе/чату, а не одному клиенту.</summary>
    public const byte FlagMulticast = 0x04;

    /// <summary>Флаг: автоответчик (Answer Machine).</summary>
    public const byte FlagAutoReply = 0x08;

    [Flags]
    public enum DecodeStatus
    {
        Ok = 0,

        /// <summary>Буфер короче минимального пакета — нужно дождаться данных.</summary>
        NeedMoreData = 1,

        /// <summary>Неверная магическая сигнатура или версия.</summary>
        BadMagic = 2,

        /// <summary>Объявленная длина payload превышает лимиты / выходит за границы буфера.</summary>
        MalformedLength = 3,

        /// <summary>CRC-32 не совпала — пакет повреждён при передаче.</summary>
        ChecksumMismatch = 4,
    }

    /// <summary>Декодированный пакет.</summary>
    public sealed record DecodedPacket(
        byte Version,
        PacketType Type,
        byte Flags,
        ulong SenderUin,
        uint Sequence,
        long TimestampUnixMs,
        ReadOnlyMemory<byte> Payload);
}

/// <summary>
/// Конструктор исходящего пакета: пишет заголовок и payload в один непрерывный
/// массив без лишних аллокаций. Возвращает ArraySegment — zero-copy для UDP-отправки.
/// </summary>
public sealed class PacketBuilder
{
    private readonly byte[] _buffer;
    private int _payloadLength;

    public PacketBuilder(int capacity = PacketCodec.HeaderSize + 64 * 1024)
    {
        if (capacity is < PacketCodec.HeaderSize or > PacketCodec.MaxPacketSize)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new byte[capacity];
    }

    /// <summary>Доступное место под payload.</summary>
    public int RemainingPayload => _buffer.Length - PacketCodec.HeaderSize - 4;

    /// <summary>Копирует payload в буфер сразу после заголовка.</summary>
    public void WritePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > RemainingPayload)
            throw new InvalidOperationException("Payload не помещается в PacketBuilder.");
        payload.CopyTo(_buffer.AsSpan(PacketCodec.HeaderSize));
        _payloadLength = payload.Length;
    }

    /// <summary>
    /// Финализирует пакет: пишет заголовок, досчитывает CRC-32 и возвращает
    /// готовый datagram как ArraySegment (без копирования). Буфер переиспользуется
    /// следующим вызовом — отправлять данные нужно синхронно до него.
    /// </summary>
    public ArraySegment<byte> Build(PacketType type, ulong senderUin, uint sequence, byte flags = 0, long timestampMs = 0)
    {
        var span = _buffer.AsSpan(0, PacketCodec.HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], PacketCodec.Magic);
        span[4] = PacketCodec.CurrentVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(5, 2), (ushort)type);
        span[7] = flags;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8, 8), senderUin);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(20, 8),
            timestampMs != 0 ? timestampMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)_payloadLength);

        var total = PacketCodec.HeaderSize + _payloadLength;
        var crc = WizTalk.Core.Utilities.Crc32.Compute(_buffer.AsSpan(0, total));
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(total, 4), crc);
        return new ArraySegment<byte>(_buffer, 0, total + 4);
    }
}

/// <summary>
/// Статические методы кодирования/декодирования. Декодирование всегда проверяет
/// magic, версию, длину payload и CRC-32 — пакеты с ошибками отбрасываются на границе парсинга.
/// </summary>
public static partial class PacketCodec
{
    /// <summary>Собирает однострочный пакет из уже готового payload.</summary>
    public static byte[] Encode(PacketType type, ulong senderUin, uint sequence, ReadOnlySpan<byte> payload, byte flags = 0)
    {
        var total = HeaderSize + payload.Length + 4;
        if (total > MaxPacketSize)
            throw new ArgumentException("Пакет превышает максимальный размер протокола.");
        var buffer = new byte[total];
        WriteHeader(buffer, type, senderUin, sequence, payload.Length, flags);
        payload.CopyTo(buffer.AsSpan(HeaderSize));
        var crc = WizTalk.Core.Utilities.Crc32.Compute(buffer.AsSpan(0, total - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(total - 4, 4), crc);
        return buffer;
    }

    /// <summary>Запись заголовка v2 в произвольный буфер.</summary>
    public static void WriteHeader(Span<byte> buffer, PacketType type, ulong senderUin, uint sequence, int payloadLength, byte flags = 0)
    {
        var span = buffer[..HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], Magic);
        span[4] = CurrentVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(5, 2), (ushort)type);
        span[7] = flags;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8, 8), senderUin);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(20, 8), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)payloadLength);
    }

    /// <summary>
    /// Пытается разобрать datagram v2. Возвращает статус вместо исключения —
    /// сетевой слой логирует проблему и продолжает приём.
    /// </summary>
    public static bool TryDecode(ReadOnlyMemory<byte> datagram, out DecodedPacket? packet, out DecodeStatus status)
    {
        packet = null;
        status = DecodeStatus.Ok;
        var span = datagram.Span;

        if (span.Length < HeaderSize + 4)
        {
            status = DecodeStatus.NeedMoreData;
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != Magic)
        {
            status = DecodeStatus.BadMagic;
            return false;
        }

        var version = span[4];
        if (version is < MinSupportedVersion or > CurrentVersion)
        {
            status = DecodeStatus.BadMagic;
            return false;
        }

        // Старые сборки (v1) используют укороченный заголовок — пробуем legacy-парсер.
        if (version == 1 && span.Length >= 24 && BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(28, 4)) > (uint)(span.Length - HeaderSize - 4))
            return TryDecodeLegacy(datagram, out packet);

        var type = (PacketType)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(5, 2));
        var flags = span[7];
        var sender = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8, 8));
        var seq = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));
        var ts = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(20, 8));
        var payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(28, 4));

        if (payloadLen > MaxPacketSize ||
            (long)HeaderSize + payloadLen + 4 > span.Length)
        {
            status = DecodeStatus.MalformedLength;
            return false;
        }

        var bodyEnd = HeaderSize + (int)payloadLen;
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(bodyEnd, 4));
        if (WizTalk.Core.Utilities.Crc32.Compute(span[..bodyEnd]) != storedCrc)
        {
            status = DecodeStatus.ChecksumMismatch;
            return false;
        }

        packet = new DecodedPacket(version, type, flags, sender, seq, ts,
            datagram.Slice(HeaderSize, (int)payloadLen));
        return true;
    }

    /// <summary>
    /// Обратная совместимость: старые пакеты v1 имели заголовок 20 байт
    /// (magic, version, type, sender(uint32), seq, len) без флагов и метки времени.
    /// </summary>
    public static bool TryDecodeLegacy(ReadOnlyMemory<byte> datagram, out DecodedPacket? packet)
    {
        packet = null;
        var span = datagram.Span;
        const int legacyHeader = 20;
        if (span.Length < legacyHeader + 4)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(span[..4]) != Magic)
            return false;
        if (span[4] != 1)
            return false;

        var type = (PacketType)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(5, 2));
        var sender = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(7, 4));
        var seq = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(11, 4));
        var payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(15, 4));
        if (payloadLen > MaxPacketSize || legacyHeader + payloadLen + 4 > span.Length)
            return false;

        var bodyEnd = legacyHeader + (int)payloadLen;
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(bodyEnd, 4));
        if (WizTalk.Core.Utilities.Crc32.Compute(span[..bodyEnd]) != storedCrc)
            return false;

        packet = new DecodedPacket(1, type, 0, sender, seq, 0,
            datagram.Slice(legacyHeader, (int)payloadLen));
        return true;
    }
}

/// <summary>Утилиты для текстовых payload'ов (UTF-8).</summary>
public static class TextPayload
{
    /// <summary>Превращает строку в UTF-8 payload.</summary>
    public static byte[] FromString(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Читает строку из UTF-8 payload.</summary>
    public static string ToString(ReadOnlySpan<byte> payload) => Encoding.UTF8.GetString(payload);
}
