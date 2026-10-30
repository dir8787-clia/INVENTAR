using CommunityToolkit.Mvvm.ComponentModel;
using WizTalk.Core.Protocol;

namespace WizTalk.Core.Config;

/// <summary>
/// Типобезопасная модель всех настроек клиента, спроецированная с INI-документа.
/// Каждое поле соответствует ключу из ТЗ (раздел 5). Изменения публикуются через
/// PropertyChanged — UI и сетевой слой подписываются на живую перезагрузку конфигов.
/// </summary>
public sealed partial class WizTalkConfig : ObservableObject
{
    // ---- [Local] ----------------------------------------------------------
    [ObservableProperty] private string _localName = Environment.UserName;
    [ObservableProperty] private ClientStatus _localStatus = ClientStatus.Online;

    // ---- [Main] -----------------------------------------------------------
    [ObservableProperty] private bool _noSound;
    [ObservableProperty] private bool _showIp;
    [ObservableProperty] private bool _sortListMode;              // 0 — по группам, 1 — плоский список
    [ObservableProperty] private bool _hideOnStartup;
    [ObservableProperty] private bool _hideOnEscape = true;
    [ObservableProperty] private bool _hideOnClose = true;        // закрытие окна сворачивает в трей
    [ObservableProperty] private bool _highPriority = true;       // высокий приоритет процесса для аудио
    [ObservableProperty] private bool _hideMe;                    // невидимость для broadcast
    [ObservableProperty] private bool _remoteUpdate;              // разрешить удалённое обновление конфига
    [ObservableProperty] private int _startupDelaySec;
    [ObservableProperty] private bool _workWithOffline;           // разрешить писать оффлайн-клиентам
    [ObservableProperty] private bool _autoAway;
    [ObservableProperty] private int _autoAwayTimeMin = 10;
    [ObservableProperty] private string _language = "russian";

    // ---- [Text] -----------------------------------------------------------
    [ObservableProperty] private bool _showTime = true;
    [ObservableProperty] private bool _chatMode;                  // режим глобального чата активен
    [ObservableProperty] private bool _showNewFirst;              // новые сообщения сверху
    [ObservableProperty] private bool _restorePositions = true;   // восстанавливать позицию окон диалогов
    [ObservableProperty] private bool _bringToFront;
    [ObservableProperty] private bool _closeAfterSend;
    [ObservableProperty] private bool _sendOnEnter = true;        // Enter отправляет, Ctrl+Enter — перевод строки
    [ObservableProperty] private bool _chatSendOnEnter = true;
    [ObservableProperty] private bool _useIgnoreStrings;
    [ObservableProperty] private bool _showSmiles = true;
    [ObservableProperty] private bool _showSmilesAnimation = true;
    [ObservableProperty] private bool _sendTypeNotify = true;
    [ObservableProperty] private bool _antiSniff = true;          // кодировать текст перед отправкой
    [ObservableProperty] private List<string> _ignoreWords = [];
    [ObservableProperty] private List<string> _stamps = ["Я занят", "Обед", "Отойду на 5 минут", "Проверю и отвечу"];

    // ---- [Net] ------------------------------------------------------------
    [ObservableProperty] private int _basePort = 8765;
    public int SoundPort => BasePort + 1;   // UDP 8766
    public int FilePort => BasePort + 2;    // TCP 8767
    [ObservableProperty] private int _pingOffline;               // кол-во пропущенных циклов до «offline»
    [ObservableProperty] private int _pingOnline;                // период ping для проверки статуса (0 = выкл)
    [ObservableProperty] private bool _pingBroadcast = true;     // рассылать broadcast-анонсы
    [ObservableProperty] private int _pingCount = 1;             // попыток пинга
    [ObservableProperty] private int _ttlSec = 180;              // время жизни записи о клиенте без активности
    [ObservableProperty] private bool _broadcastMode = true;     // multicast вместо broadcast
    [ObservableProperty] private bool _useServer;
    [ObservableProperty] private bool _serverMode;               // этот экземпляр работает сервером
    [ObservableProperty] private string _serverHost = "192.168.1.1";
    [ObservableProperty] private int _serverPort = 8772;
    [ObservableProperty] private int _pingResponseWaitSec = 8;
    [ObservableProperty] private NetworkMode _networkMode = NetworkMode.PeerToPeer;
    [ObservableProperty] private List<string> _fixedClients = []; // ручные записи "ip:port"
    [ObservableProperty] private bool _tlsForServer;              // опциональный TLS в серверном режиме

    // ---- [Sound Capture] ----------------------------------------------------
    [ObservableProperty] private int _captureDeviceId;
    [ObservableProperty] private int _sampleRate = 22050;         // 8000/16000/22050/44100
    [ObservableProperty] private int _bufferLenMs = 50;
    [ObservableProperty] private AudioCodecKind _encoderType = AudioCodecKind.Opus;
    [ObservableProperty] private bool _captureAgc;                // AGC исходящего потока — опционально
    [ObservableProperty] private bool _noiseGate;
    [ObservableProperty] private double _noisePowerDb = -60;
    [ObservableProperty] private bool _vasEnabled;                // VAS (дуплекс по голосовой активности)
    [ObservableProperty] private double _vasPowerOnDb = -45;      // порог начала передачи
    [ObservableProperty] private double _vasPowerOffDb = -55;     // порог остановки (гистерезис)
    [ObservableProperty] private int _prebufferBlocks = 3;        // read-ahead, защита от обрезания старта

    // ---- [Sound Play] -------------------------------------------------------
    [ObservableProperty] private int _playbackDeviceId;
    [ObservableProperty] private int _volume = 40;
    [ObservableProperty] private int _maxBufferLenMs = 3000;
    [ObservableProperty] private int _minBufferLenMs = 25;
    [ObservableProperty] private int _addonBufferMs = 10;
    [ObservableProperty] private bool _tscEnabled = true;         // Time Shift Control
    [ObservableProperty] private bool _playbackAgc = true;        // AGC входящего потока — обязателен по ТЗ

    /// <summary>VAS и AGC не могут быть включены одновременно (см. ТЗ 3.1).</summary>
    partial void OnVasEnabledChanged(bool value)
    {
        if (value && PlaybackAgc)
            PlaybackAgc = false;
    }

    partial void OnPlaybackAgcChanged(bool value)
    {
        if (value && VasEnabled)
            VasEnabled = false;
    }

    partial void OnBasePortChanged(int value)
    {
        OnPropertyChanged(nameof(SoundPort));
        OnPropertyChanged(nameof(FilePort));
    }

    // ---- [Files] ------------------------------------------------------------
    [ObservableProperty] private int _chunkSizeKb = 512;
    [ObservableProperty] private bool _autoAccept;
    [ObservableProperty] private string _receivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WizTalk", "Received");
    [ObservableProperty] private OverwriteAction _overwriteAction = OverwriteAction.Rename;
    [ObservableProperty] private int _confirmTimeoutSec = 30;

    // ---- [Alerts] -----------------------------------------------------------
    [ObservableProperty] private string _sndTextReceived = @"Sounds\msg_received.wav";
    [ObservableProperty] private string _sndTextSent = @"Sounds\msg_sent.wav";
    [ObservableProperty] private string _sndCall = "#4";          // системный звук Windows
    [ObservableProperty] private string _sndFileRequest = @"Sounds\file_request.wav";
    [ObservableProperty] private string _sndFileAccepted = @"Sounds\file_accept.wav";
    [ObservableProperty] private string _sndFileRejected = @"Sounds\file_reject.wav";
    [ObservableProperty] private string _sndClientOnline = @"Sounds\online.wav";
    [ObservableProperty] private string _sndClientOffline = @"Sounds\offline.wav";
    [ObservableProperty] private bool _flashTaskBar = true;
    [ObservableProperty] private bool _showBalloonNotify = true;

    // ---- [Clipboard] --------------------------------------------------------
    [ObservableProperty] private bool _clipboardReceive;
    [ObservableProperty] private bool _clipboardAutoOpenImages = true;

    // ---- Answer Machine -----------------------------------------------------
    [ObservableProperty] private bool _answerMachineEnabled = true;
    [ObservableProperty] private string _answerMachineText = "Пользователь отсутствует. Это автоматический ответ.";
    [ObservableProperty] private bool _answerVoiceEnabled;
    [ObservableProperty] private string _answerVoicePath = @"Sounds\voicemail.wav";

    // ---- Звуковая история ----------------------------------------------------
    [ObservableProperty] private bool _soundHistoryEnabled;
    [ObservableProperty] private string _soundHistoryFormat = "wav-pcm"; // wav-pcm | wav-adpcm-ima | wav-adpcm-ms
    [ObservableProperty] private string _soundHistoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WizTalk", "History");

    // ---- Hotkeys (Win32 virtual-key codes) -----------------------------------
    [ObservableProperty] private int _hotkeyShowHide = 0x7B;      // F12
    [ObservableProperty] private int _hotkeyDisplayOff = 0x79;    // F10
    [ObservableProperty] private int _hotkeyMuteAll = 0x7A;       // F11
    [ObservableProperty] private int _hotkeyPushToTalk = 0x2D;    // '-'

    // ---- Сетка интерфейсов (сетевые фильтры) --------------------------------
    [ObservableProperty] private bool _allowText = true;
    [ObservableProperty] private bool _allowSound = true;
    [ObservableProperty] private bool _allowFiles = true;
    [ObservableProperty] private bool _allowP2p = true;
    [ObservableProperty] private bool _serverOnlyTraffic;
    [ObservableProperty] private bool _autoAddServers = true;
    [ObservableProperty] private bool _ignoreAllPackets;
    [ObservableProperty] private List<string> _ipAllowList = [];
    [ObservableProperty] private List<string> _ipDenyList = [];

    // ---- Позиции окон диалогов (RestorePositions) ----------------------------
    public Dictionary<string, WindowRect> DialogPositions { get; } = new();

    public sealed record WindowRect(double X, double Y, double Width, double Height);

    /// <summary>Оценка задержки аудио по формуле ТЗ:</summary>
    /// CaptureBuffer × (Prebuffer + 1) + AddonBuffer + NetworkLatency
    public double EstimatedAudioLatencyMs(double networkLatencyMs = 15) =>
        BufferLenMs * (PrebufferBlocks + 1) + AddonBufferMs + networkLatencyMs;
}
