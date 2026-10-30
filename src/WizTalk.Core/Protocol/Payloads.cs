using System.Buffers.Binary;

namespace WizTalk.Core.Protocol;

/// <summary>
/// Высокоуровневые payload'ы протокола. Каждый — компактная бинарная структура,
/// сериализуемая в Span без LINQ и лишних аллокаций (важно для аудио-потока).
/// </summary>
public static class Payloads
{
    // ---- PING / PONG ------------------------------------------------------

    /// <summary>Payload PING/PONG: UIN + статус + версия клиента.</summary>
    public static byte[] PingPong(ulong uin, ClientStatus status, ushort protocolVersion)
    {
        var b = new byte[11];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0), uin);
        b[8] = (byte)status;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(9), protocolVersion);
        return b;
    }

    public static bool TryReadPingPong(ReadOnlySpan<byte> p, out ulong uin, out ClientStatus status, out ushort protocolVersion)
    {
        uin = 0;
        status = ClientStatus.Offline;
        protocolVersion = 0;
        if (p.Length < 11)
            return false;
        uin = BinaryPrimitives.ReadUInt64LittleEndian(p);
        status = (ClientStatus)p[8];
        protocolVersion = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(9));
        return true;
    }

    // ---- BROADCAST (обнаружение) -----------------------------------------

    /// <summary>
    /// Broadcast-анонс клиента: UIN, имя (UTF-8, до 64 байт), статус, порты, признак аудио.
    /// layout: [uin:8][status:1][audioCapable:1][nameLen:2][name:N][basePort:2][soundPort:2][filePort:2]
    /// </summary>
    public static byte[] Broadcast(ulong uin, string name, ClientStatus status, bool audioCapable, int basePort, int soundPort, int filePort)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name is { Length: > 0 } ? Trim(name, 32) : "unknown");
        var b = new byte[8 + 2 + 2 + nameBytes.Length + 6];
        var o = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o), uin);
        o += 8;
        b[o++] = (byte)status;
        b[o++] = (byte)(audioCapable ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)nameBytes.Length);
        o += 2;
        nameBytes.CopyTo(b.AsSpan(o));
        o += nameBytes.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)basePort);
        o += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)soundPort);
        o += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), (ushort)filePort);
        return b;
    }

    public static bool TryReadBroadcast(ReadOnlySpan<byte> p, out ulong uin, out string name, out ClientStatus status,
        out bool audioCapable, out int basePort, out int soundPort, out int filePort)
    {
        uin = 0;
        name = string.Empty;
        status = ClientStatus.Offline;
        audioCapable = false;
        basePort = soundPort = filePort = 0;

        if (p.Length < 12)
            return false;
        var o = 0;
        uin = BinaryPrimitives.ReadUInt64LittleEndian(p[o..]);
        o += 8;
        status = (ClientStatus)p[o++];
        audioCapable = p[o++] != 0;
        var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(p[o..]);
        o += 2;
        if (p.Length < o + nameLen + 6)
            return false;
        name = System.Text.Encoding.UTF8.GetString(p.Slice(o, nameLen));
        o += nameLen;
        basePort = BinaryPrimitives.ReadUInt16LittleEndian(p[o..]);
        o += 2;
        soundPort = BinaryPrimitives.ReadUInt16LittleEndian(p[o..]);
        o += 2;
        filePort = BinaryPrimitives.ReadUInt16LittleEndian(p[o..]);
        return true;
    }

    // ---- TEXT / CHAT / ADMIN ---------------------------------------------

    /// <summary>
    /// Текстовое сообщение. layout: [msgId:8][recipientUin:8][text:N]
    /// recipientUin == 0 означает широковещательный глобальный чат.
    /// </summary>
    public static byte[] TextMsg(ulong messageId, ulong recipientUin, string text)
    {
        var t = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
        var b = new byte[16 + t.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0), messageId);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), recipientUin);
        t.CopyTo(b.AsSpan(16));
        return b;
    }

    public static bool TryReadTextMsg(ReadOnlySpan<byte> p, out ulong messageId, out ulong recipientUin, out string text)
    {
        messageId = recipientUin = 0;
        text = string.Empty;
        if (p.Length < 16)
            return false;
        messageId = BinaryPrimitives.ReadUInt64LittleEndian(p);
        recipientUin = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(8));
        text = System.Text.Encoding.UTF8.GetString(p.Slice(16));
        return true;
    }

    // ---- SOUND ------------------------------------------------------------

    /// <summary>
    /// Звуковой пакет. layout: [codec:1][sampleRate:4][frameMs:2][seq:4][ssrc:4][fecMask:1][data:N]
    /// fecMask — битовая карта «восстановленных» из FEC блоков (см. Audio.FecCodec).
    /// </summary>
    public static byte[] Sound(AudioCodecKind codec, int sampleRate, int frameMs, uint seq, uint ssrc, byte fecMask, ReadOnlySpan<byte> data)
    {
        var b = new byte[16 + data.Length];
        b[0] = (byte)codec;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), sampleRate);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(5), (ushort)frameMs);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(7), seq);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(11), ssrc);
        b[15] = fecMask;
        data.CopyTo(b.AsSpan(16));
        return b;
    }

    public sealed record SoundHeader(AudioCodecKind Codec, int SampleRate, int FrameMs, uint Sequence, uint Ssrc, byte FecMask);

    public static bool TryReadSound(ReadOnlySpan<byte> p, out SoundHeader header, out ReadOnlySpan<byte> data)
    {
        header = null!;
        data = default;
        if (p.Length < 16)
            return false;
        header = new SoundHeader(
            (AudioCodecKind)p[0],
            BinaryPrimitives.ReadInt32LittleEndian(p.Slice(1)),
            BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(5)),
            BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(7)),
            BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(11)),
            p[15]);
        data = p.Slice(16);
        return true;
    }

    // ---- FILES ------------------------------------------------------------

    /// <summary>
    /// Заголовок FILE_REQUEST: [transferId:8][totalSize:8][options:1][nameLen:2][name:N]
    /// options bit0 — это директория; bit1 — автоприём разрешён отправителем.
    /// </summary>
    public static byte[] FileRequest(ulong transferId, long totalSize, bool isDirectory, string fileName)
    {
        var n = System.Text.Encoding.UTF8.GetBytes(fileName);
        var b = new byte[19 + n.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0), transferId);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(8), totalSize);
        b[16] = (byte)(isDirectory ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(17), (ushort)n.Length);
        n.CopyTo(b.AsSpan(19));
        return b;
    }

    public sealed record FileRequestInfo(ulong TransferId, long TotalSize, bool IsDirectory, string FileName);

    public static bool TryReadFileRequest(ReadOnlySpan<byte> p, out FileRequestInfo info)
    {
        info = null!;
        if (p.Length < 19)
            return false;
        var id = BinaryPrimitives.ReadUInt64LittleEndian(p);
        var size = BinaryPrimitives.ReadInt64LittleEndian(p.Slice(8));
        var dir = (p[16] & 1) != 0;
        var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(17));
        if (p.Length < 19 + nameLen)
            return false;
        info = new FileRequestInfo(id, size, dir, System.Text.Encoding.UTF8.GetString(p.Slice(19, nameLen)));
        return true;
    }

    /// <summary>FILE_ACK: [transferId:8][bytesReceived:8][chunkSeq:4]</summary>
    public static byte[] FileAck(ulong transferId, long bytesReceived, uint chunkSeq)
    {
        var b = new byte[20];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0), transferId);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(8), bytesReceived);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), chunkSeq);
        return b;
    }

    public static bool TryReadFileAck(ReadOnlySpan<byte> p, out ulong transferId, out long bytesReceived, out uint chunkSeq)
    {
        transferId = 0;
        bytesReceived = 0;
        chunkSeq = 0;
        if (p.Length < 20)
            return false;
        transferId = BinaryPrimitives.ReadUInt64LittleEndian(p);
        bytesReceived = BinaryPrimitives.ReadInt64LittleEndian(p.Slice(8));
        chunkSeq = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(16));
        return true;
    }

    // ---- STATUS / REMOTE CONTROL / CONFIG ---------------------------------

    /// <summary>STATUS_CHANGE: [status:1]</summary>
    public static byte[] Status(ClientStatus status) => [(byte)status];

    public static bool TryReadStatus(ReadOnlySpan<byte> p, out ClientStatus status)
    {
        status = ClientStatus.Offline;
        if (p.Length < 1)
            return false;
        status = (ClientStatus)p[0];
        return true;
    }

    /// <summary>REMOTE_CONTROL: [command:1][arg:4] — команды аналога Wizmo.</summary>
    public static byte[] RemoteCommand(RemoteControlCommand command, int argument = 0)
    {
        var b = new byte[5];
        b[0] = (byte)command;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), argument);
        return b;
    }

    public static bool TryReadRemoteCommand(ReadOnlySpan<byte> p, out RemoteControlCommand command, out int argument)
    {
        command = RemoteControlCommand.None;
        argument = 0;
        if (p.Length < 1)
            return false;
        command = (RemoteControlCommand)p[0];
        if (p.Length >= 5)
            argument = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(1));
        return true;
    }

    /// <summary>CONFIG_UPDATE/READ/WRITE: [op:1][sectionLen:1][section:N][keyLen:1][key:N][value:N]</summary>
    public static byte[] ConfigOp(ConfigOperation op, string section, string key, string value)
    {
        var s = System.Text.Encoding.UTF8.GetBytes(section);
        var k = System.Text.Encoding.UTF8.GetBytes(key);
        var v = System.Text.Encoding.UTF8.GetBytes(value);
        var b = new byte[3 + s.Length + k.Length + v.Length];
        b[0] = (byte)op;
        b[1] = (byte)s.Length;
        s.CopyTo(b.AsSpan(2));
        b[2 + s.Length] = (byte)k.Length;
        k.CopyTo(b.AsSpan(3 + s.Length));
        v.CopyTo(b.AsSpan(3 + s.Length + k.Length));
        return b;
    }

    public sealed record ConfigOpInfo(ConfigOperation Operation, string Section, string Key, string Value);

    public static bool TryReadConfigOp(ReadOnlySpan<byte> p, out ConfigOpInfo info)
    {
        info = null!;
        if (p.Length < 3)
            return false;
        var op = (ConfigOperation)p[0];
        var sLen = p[1];
        if (p.Length < 2 + sLen + 1)
            return false;
        var section = System.Text.Encoding.UTF8.GetString(p.Slice(2, sLen));
        var kLen = p[2 + sLen];
        if (p.Length < 3 + sLen + kLen)
            return false;
        var key = System.Text.Encoding.UTF8.GetString(p.Slice(3 + sLen, kLen));
        var value = System.Text.Encoding.UTF8.GetString(p.Slice(3 + sLen + kLen));
        info = new ConfigOpInfo(op, section, key, value);
        return true;
    }

    private static string Trim(string s, int maxChars) => s.Length <= maxChars ? s : s[..maxChars];
}

/// <summary>Команды удалённого управления (реализуются через Win32/P-Invoke в клиенте).</summary>
public enum RemoteControlCommand : byte
{
    None = 0,

    /// <summary>Выключить дисплей (SC_MONITORPOWER).</summary>
    DisplayOff = 1,

    /// <summary>Включить дисплей обратно.</summary>
    DisplayOn = 2,

    /// <summary>Установить громкость микшера (arg = 0..100).</summary>
    SetVolume = 3,

    /// <summary>Mute/unmute мастера (arg = 1 — mute).</summary>
    ToggleMute = 4,

    /// <summary>Завершить сеанс пользователя.</summary>
    Logoff = 5,

    /// <summary>Перезагрузка компьютера.</summary>
    Reboot = 6,

    /// <summary>Выключение питания.</summary>
    Shutdown = 7,
}

/// <summary>Операции удалённого конфигурирования клиентов сервером.</summary>
public enum ConfigOperation : byte
{
    Read = 0,
    Write = 1,
    UpdateNotify = 2,
}
