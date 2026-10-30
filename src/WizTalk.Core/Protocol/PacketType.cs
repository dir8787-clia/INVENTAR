namespace WizTalk.Core.Protocol;

/// <summary>
/// Типы сервисных и пользовательских пакетов протокола WizTalk (см. раздел 4 ТЗ).
/// Нумерация стабильна во времени — новые типы добавляются только в конец,
/// что сохраняет обратную совместимость со старыми сборками клиентов.
/// </summary>
public enum PacketType : ushort
{
    /// <summary>Запрос статуса клиента.</summary>
    Ping = 0x0001,

    /// <summary>Ответ на PING.</summary>
    Pong = 0x0002,

    /// <summary>Broadcast-обнаружение клиентов в локальной сети.</summary>
    Broadcast = 0x0010,

    /// <summary>Текстовое сообщение (личное или групповое).</summary>
    Text = 0x0020,

    /// <summary>Уведомление «собеседник печатает».</summary>
    TypingNotify = 0x0021,

    /// <summary>Смена статуса отправителя (Online/Away/DND/Offline).</summary>
    StatusChange = 0x0030,

    /// <summary>Сообщение глобального чата.</summary>
    Chat = 0x0040,

    /// <summary>Присоединение к глобальному чату / выход из него.</summary>
    ChatJoin = 0x0041,
    ChatLeave = 0x0042,

    /// <summary>Звуковой RTP-подобный пакет (UDP 8766).</summary>
    Sound = 0x0100,
    SoundRequest = 0x0101,
    SoundAccept = 0x0102,
    SoundReject = 0x0103,
    SoundHangUp = 0x0104,

    /// <summary>Передача файлов (TCP 8767): согласование и данные.</summary>
    FileRequest = 0x0200,
    FileAccept = 0x0201,
    FileReject = 0x0202,
    FileData = 0x0203,
    FileAck = 0x0204,
    FileDone = 0x0205,
    DirEntry = 0x0206,

    /// <summary>Административные сообщения сервера и бан-система.</summary>
    AdminMsg = 0x0300,
    BanInfo = 0x0301,

    /// <summary>Удалённое конфигурирование клиентов через сервер.</summary>
    ConfigRead = 0x0310,
    ConfigWrite = 0x0311,
    ConfigUpdate = 0x0312,

    /// <summary>Команды удалённого управления (аналог Wizmo).</summary>
    RemoteControl = 0x0400,

    /// <summary>Серверная маршрутизация: P2P-соединение минуя сервер.</summary>
    P2pRoute = 0x0500,

    /// <summary>Автоответчик (Answer Machine) — текстовый или голосовой ответ.</summary>
    AutoReplyText = 0x0600,
    AutoReplyVoice = 0x0601,

    /// <summary>Передача содержимого буфера обмена.</summary>
    ClipboardSend = 0x0700,

    /// <summary>Служебное подтверждение доставки.</summary>
    Ack = 0x0F00,

    /// <summary>Клиент корректно завершает работу.</summary>
    Bye = 0x0FFF,
}

/// <summary>Статусы клиента (совпадают с числовыми значениями INI-раздела [Local] Status).</summary>
public enum ClientStatus : byte
{
    Offline = 0,
    Online = 1,
    Away = 2,
    DoNotDisturb = 3,
}

/// <summary>Режимы работы с сервером (Net.UseServer / ServerMode).</summary>
public enum NetworkMode
{
    /// <summary>Чистый P2P: broadcast/multicast discovery, соединения напрямую.</summary>
    PeerToPeer = 0,

    /// <summary>Весь трафик идёт через выделенный сервер.</summary>
    ServerOnly = 1,

    /// <summary>Сервер используется для discovery и администрирования, но игнорируется для рассылки.</summary>
    IgnoreServer = 2,
}

/// <summary>Типы кодеков аудио (Sound Capture.EncoderType).</summary>
public enum AudioCodecKind : byte
{
    Opus = 0,
    Speex = 1,
    Adpcm = 2,
}

/// <summary>Действие при конфликте имён входящего файла (Files.OverwriteAction).</summary>
public enum OverwriteAction
{
    Overwrite = 0,
    Rename = 1,
    Skip = 2,
}
