using System.Buffers.Binary;

namespace WizTalk.Core.Utilities;

/// <summary>
/// Табличный CRC-32 (полином 0xEDB88320, реверсированный) — контрольная сумма
/// пакетов протокола. Реализация без аллокаций, работает через Span&lt;byte&gt;.
/// Совместим с классическим zlib/CRC-32, что упрощает отладку внешними утилитами.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    /// <summary>Вычисляет CRC-32 от произвольного диапазона байт.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Проверяет, что последние 4 байта буфера содержат корректную CRC-32 от предыдущих.</summary>
    public static bool Verify(ReadOnlySpan<byte> packetWithChecksum)
    {
        if (packetWithChecksum.Length < 4)
            return false;
        var body = packetWithChecksum[..^4];
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(packetWithChecksum[^4..]);
        return Compute(body) == stored;
    }
}
