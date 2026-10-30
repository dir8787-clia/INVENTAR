using System.Globalization;
using WizTalk.Core.Protocol;

namespace WizTalk.Core.Config;

/// <summary>
/// Менеджер конфигурации: загрузка default.ini (или JSON-альтернативы), маппинг
/// «INI-ключ ↔ свойство типобезопасной модели», автосохранение с debounce и
/// поддержка удалённого редактирования (ConfigUpdate через сервер).
/// </summary>
public sealed class ConfigManager
{
    private readonly string _path;
    private readonly object _gate = new();
    private IniDocument _doc;

    public WizTalkConfig Config { get; } = new();

    /// <summary>Событие полной перезагрузки настроек (после Remote Update).</summary>
    public event EventHandler? Reloaded;

    public ConfigManager(string iniPath)
    {
        _path = Path.GetFullPath(iniPath);
        _doc = IniDocument.Load(_path);
        ApplyTo(_doc, Config);
    }

    /// <summary>Загружает файл с диска и проецирует в модель.</summary>
    public void Load()
    {
        lock (_gate)
        {
            _doc = IniDocument.Load(_path);
            ApplyTo(_doc, Config);
        }

        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Сохраняет текущее состояние модели в INI и на диск.</summary>
    public void Save()
    {
        lock (_gate)
        {
            WriteFrom(Config, _doc);
            _doc.Save(_path);
        }
    }

    /// <summary>
    /// Точечное изменение одного ключа — используется сервером для удалённого
    /// конфигурирования клиентов (см. 3.6 «чтение/запись config.ini»).
    /// </summary>
    public void ApplyRemotePatch(string sectionName, string key, string value)
    {
        lock (_gate)
        {
            _doc.Set(sectionName, key, value);
            ApplyTo(_doc, Config);
        }

        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    public string ExportIniText()
    {
        lock (_gate)
        {
            WriteFrom(Config, _doc);
            var sb = new System.Text.StringBuilder();
            foreach (var s in _doc.Sections)
            {
                sb.Append('[').Append(s.Name).Append("]\r\n");
                foreach (var e in s.Entries)
                    sb.Append(e.Key).Append(" = ").Append(e.Value).Append("\r\n");
                sb.Append("\r\n");
            }

            return sb.ToString();
        }
    }

    // ---- Маппинг -----------------------------------------------------------

    private static void ApplyTo(IniDocument d, WizTalkConfig c)
    {
        c.LocalName = d.GetString("Local", "Name", c.LocalName);
        c.LocalStatus = (ClientStatus)d.GetInt("Local", "Status", 1);

        c.NoSound = d.GetBool("Main", "NoSound");
        c.ShowIp = d.GetBool("Main", "ShowIP");
        c.SortListMode = d.GetBool("Main", "SortListMode");
        c.HideOnStartup = d.GetBool("Main", "HideOnStartup");
        c.HideOnEscape = d.GetBool("Main", "HideOnEscape", true);
        c.HideOnClose = d.GetBool("Main", "HideOnClose", true);
        c.HighPriority = d.GetBool("Main", "HighPriority", true);
        c.HideMe = d.GetBool("Main", "HideMe");
        c.RemoteUpdate = d.GetBool("Main", "RemoteUpdate");
        c.StartupDelaySec = d.GetInt("Main", "StartupDelaySec");
        c.WorkWithOffline = d.GetBool("Main", "WorkWithOffline");
        c.AutoAway = d.GetBool("Main", "AutoAway");
        c.AutoAwayTimeMin = d.GetInt("Main", "AutoAwayTimeMin", 10);
        c.Language = d.GetString("Main", "Language", "russian");

        c.ShowTime = d.GetBool("Text", "ShowTime", true);
        c.ChatMode = d.GetBool("Text", "ChatMode");
        c.ShowNewFirst = d.GetBool("Text", "ShowNewFirst");
        c.RestorePositions = d.GetBool("Text", "RestorePositions", true);
        c.BringToFront = d.GetBool("Text", "BringToFront");
        c.CloseAfterSend = d.GetBool("Text", "CloseAfterSend");
        c.SendOnEnter = d.GetBool("Text", "SendOnEnter", true);
        c.ChatSendOnEnter = d.GetBool("Text", "ChatSendOnEnter", true);
        c.UseIgnoreStrings = d.GetBool("Text", "UseIgnoreStrings");
        c.ShowSmiles = d.GetBool("Text", "ShowSmiles", true);
        c.ShowSmilesAnimation = d.GetBool("Text", "ShowSmilesAnimation", true);
        c.SendTypeNotify = d.GetBool("Text", "SendTypeNotify", true);
        c.AntiSniff = d.GetBool("Text", "AntiSniff", true);
        c.IgnoreWords = SplitList(d.GetString("Text", "IgnoreWords"));
        c.Stamps = SplitList(d.GetString("Text", "Stamps", string.Join("|", c.Stamps)));

        c.BasePort = d.GetInt("Net", "BasePort", 8765);
        c.PingOffline = d.GetInt("Net", "PingOffline");
        c.PingOnline = d.GetInt("Net", "PingOnline");
        c.PingBroadcast = d.GetBool("Net", "PingBroadcast", true);
        c.PingCount = d.GetInt("Net", "PingCount", 1);
        c.TtlSec = d.GetInt("Net", "TTLsec", 180);
        c.BroadcastMode = d.GetBool("Net", "BroadcastMode", true);
        c.UseServer = d.GetBool("Net", "UseServer");
        c.ServerMode = d.GetBool("Net", "ServerMode");
        c.ServerHost = d.GetString("Net", "ServerHost", c.ServerHost);
        c.ServerPort = d.GetInt("Net", "ServerPort", 8772);
        c.PingResponseWaitSec = d.GetInt("Net", "PingResponseWaitSec", 8);
        c.NetworkMode = (NetworkMode)d.GetInt("Net", "Mode", (int)c.NetworkMode);
        c.FixedClients = SplitList(d.GetString("Net", "FixedClients"));
        c.TlsForServer = d.GetBool("Net", "Tls");

        c.CaptureDeviceId = d.GetInt("Sound Capture", "DeviceID");
        c.SampleRate = d.GetInt("Sound Capture", "SampleRate", 22050);
        c.BufferLenMs = d.GetInt("Sound Capture", "BufferLen", 50);
        c.EncoderType = (AudioCodecKind)d.GetInt("Sound Capture", "EncoderType");
        c.CaptureAgc = d.GetBool("Sound Capture", "AGC");
        c.NoiseGate = d.GetBool("Sound Capture", "NoiseGate");
        c.NoisePowerDb = d.GetDouble("Sound Capture", "NoisePower", -60);
        c.VasEnabled = d.GetBool("Sound Capture", "VAS");
        c.VasPowerOnDb = d.GetDouble("Sound Capture", "PowerOn", -45);
        c.VasPowerOffDb = d.GetDouble("Sound Capture", "PowerOff", -55);
        c.PrebufferBlocks = d.GetInt("Sound Capture", "Prebuffer", 3);

        c.PlaybackDeviceId = d.GetInt("Sound Play", "DeviceID");
        c.Volume = d.GetInt("Sound Play", "Volume", 40);
        c.MaxBufferLenMs = d.GetInt("Sound Play", "MaxBufferLen", 3000);
        c.MinBufferLenMs = d.GetInt("Sound Play", "MinBufferLen", 25);
        c.AddonBufferMs = d.GetInt("Sound Play", "AddonBuffer", 10);
        c.TscEnabled = d.GetBool("Sound Play", "TSC", true);
        c.PlaybackAgc = d.GetBool("Sound Play", "AGC", true);

        c.ChunkSizeKb = d.GetInt("Files", "ChunkSizeKB", 512);
        c.AutoAccept = d.GetBool("Files", "AutoAccept");
        c.ReceivePath = d.GetString("Files", "Path", c.ReceivePath);
        c.OverwriteAction = (OverwriteAction)d.GetInt("Files", "OverwriteAction", 1);
        c.ConfirmTimeoutSec = d.GetInt("Files", "ConfirmTimeoutSec", 30);

        c.SndTextReceived = d.GetString("Alerts", "TextReceived", c.SndTextReceived);
        c.SndTextSent = d.GetString("Alerts", "TextSent", c.SndTextSent);
        c.SndCall = d.GetString("Alerts", "SoundCall", "#4");
        c.SndFileRequest = d.GetString("Alerts", "FileRequest", c.SndFileRequest);
        c.SndFileAccepted = d.GetString("Alerts", "FileAccept", c.SndFileAccepted);
        c.SndFileRejected = d.GetString("Alerts", "FileReject", c.SndFileRejected);
        c.SndClientOnline = d.GetString("Alerts", "ClientOnline", c.SndClientOnline);
        c.SndClientOffline = d.GetString("Alerts", "ClientOffline", c.SndClientOffline);
        c.FlashTaskBar = d.GetBool("Alerts", "FlashTaskBar", true);
        c.ShowBalloonNotify = d.GetBool("Alerts", "ShowBalloonNotify", true);

        c.ClipboardReceive = d.GetBool("Clipboard", "Receive");
        c.ClipboardAutoOpenImages = d.GetBool("Clipboard", "AutoOpenImages", true);

        c.AnswerMachineEnabled = d.GetBool("Answer", "Enabled", true);
        c.AnswerMachineText = d.GetString("Answer", "Text", c.AnswerMachineText);
        c.AnswerVoiceEnabled = d.GetBool("Answer", "Voice");
        c.AnswerVoicePath = d.GetString("Answer", "VoicePath", c.AnswerVoicePath);

        c.SoundHistoryEnabled = d.GetBool("SoundHistory", "Enabled");
        c.SoundHistoryFormat = d.GetString("SoundHistory", "Format", "wav-pcm");
        c.SoundHistoryPath = d.GetString("SoundHistory", "Path", c.SoundHistoryPath);

        c.HotkeyShowHide = d.GetInt("Hotkeys", "ShowHide", 0x7B);
        c.HotkeyDisplayOff = d.GetInt("Hotkeys", "DisplayOff", 0x79);
        c.HotkeyMuteAll = d.GetInt("Hotkeys", "MuteAll", 0x7A);
        c.HotkeyPushToTalk = d.GetInt("Hotkeys", "PushToTalk", 0x2D);

        c.AllowText = d.GetBool("Filters", "AllowText", true);
        c.AllowSound = d.GetBool("Filters", "AllowSound", true);
        c.AllowFiles = d.GetBool("Filters", "AllowFiles", true);
        c.AllowP2p = d.GetBool("Filters", "AllowP2P", true);
        c.ServerOnlyTraffic = d.GetBool("Filters", "ServerOnly");
        c.AutoAddServers = d.GetBool("Filters", "AutoAddServers", true);
        c.IgnoreAllPackets = d.GetBool("Filters", "IgnoreAll");
        c.IpAllowList = SplitList(d.GetString("Filters", "IpAllow"));
        c.IpDenyList = SplitList(d.GetString("Filters", "IpDeny"));

        // Позиции окон диалогов хранятся как [WindowPos] uin|group = x,y,w,h
        c.DialogPositions.Clear();
        foreach (var s in d.Sections.Where(s => s.Name.Equals("WindowPos", StringComparison.OrdinalIgnoreCase)))
        foreach (var e in s.Entries)
        {
            var parts = (e.Value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                c.DialogPositions[e.Key] = new WizTalkConfig.WindowRect(x, y, w, h);
        }
    }

    private static void WriteFrom(WizTalkConfig c, IniDocument d)
    {
        d.Set("Local", "Name", c.LocalName);
        d.SetInt("Local", "Status", (int)c.LocalStatus);

        d.SetBool("Main", "NoSound", c.NoSound);
        d.SetBool("Main", "ShowIP", c.ShowIp);
        d.SetBool("Main", "SortListMode", c.SortListMode);
        d.SetBool("Main", "HideOnStartup", c.HideOnStartup);
        d.SetBool("Main", "HideOnEscape", c.HideOnEscape);
        d.SetBool("Main", "HideOnClose", c.HideOnClose);
        d.SetBool("Main", "HighPriority", c.HighPriority);
        d.SetBool("Main", "HideMe", c.HideMe);
        d.SetBool("Main", "RemoteUpdate", c.RemoteUpdate);
        d.SetInt("Main", "StartupDelaySec", c.StartupDelaySec);
        d.SetBool("Main", "WorkWithOffline", c.WorkWithOffline);
        d.SetBool("Main", "AutoAway", c.AutoAway);
        d.SetInt("Main", "AutoAwayTimeMin", c.AutoAwayTimeMin);
        d.Set("Main", "Language", c.Language);

        d.SetBool("Text", "ShowTime", c.ShowTime);
        d.SetBool("Text", "ChatMode", c.ChatMode);
        d.SetBool("Text", "ShowNewFirst", c.ShowNewFirst);
        d.SetBool("Text", "RestorePositions", c.RestorePositions);
        d.SetBool("Text", "BringToFront", c.BringToFront);
        d.SetBool("Text", "CloseAfterSend", c.CloseAfterSend);
        d.SetBool("Text", "SendOnEnter", c.SendOnEnter);
        d.SetBool("Text", "ChatSendOnEnter", c.ChatSendOnEnter);
        d.SetBool("Text", "UseIgnoreStrings", c.UseIgnoreStrings);
        d.SetBool("Text", "ShowSmiles", c.ShowSmiles);
        d.SetBool("Text", "ShowSmilesAnimation", c.ShowSmilesAnimation);
        d.SetBool("Text", "SendTypeNotify", c.SendTypeNotify);
        d.SetBool("Text", "AntiSniff", c.AntiSniff);
        d.Set("Text", "IgnoreWords", string.Join("|", c.IgnoreWords));
        d.Set("Text", "Stamps", string.Join("|", c.Stamps));

        d.SetInt("Net", "BasePort", c.BasePort);
        d.SetInt("Net", "PingOffline", c.PingOffline);
        d.SetInt("Net", "PingOnline", c.PingOnline);
        d.SetBool("Net", "PingBroadcast", c.PingBroadcast);
        d.SetInt("Net", "PingCount", c.PingCount);
        d.SetInt("Net", "TTLsec", c.TtlSec);
        d.SetBool("Net", "BroadcastMode", c.BroadcastMode);
        d.SetBool("Net", "UseServer", c.UseServer);
        d.SetBool("Net", "ServerMode", c.ServerMode);
        d.Set("Net", "ServerHost", c.ServerHost);
        d.SetInt("Net", "ServerPort", c.ServerPort);
        d.SetInt("Net", "PingResponseWaitSec", c.PingResponseWaitSec);
        d.SetInt("Net", "Mode", (int)c.NetworkMode);
        d.Set("Net", "FixedClients", string.Join("|", c.FixedClients));
        d.SetBool("Net", "Tls", c.TlsForServer);

        d.SetInt("Sound Capture", "DeviceID", c.CaptureDeviceId);
        d.SetInt("Sound Capture", "SampleRate", c.SampleRate);
        d.SetInt("Sound Capture", "BufferLen", c.BufferLenMs);
        d.SetInt("Sound Capture", "EncoderType", (int)c.EncoderType);
        d.SetBool("Sound Capture", "AGC", c.CaptureAgc);
        d.SetBool("Sound Capture", "NoiseGate", c.NoiseGate);
        d.Set("Sound Capture", "NoisePower", c.NoisePowerDb.ToString(CultureInfo.InvariantCulture));
        d.SetBool("Sound Capture", "VAS", c.VasEnabled);
        d.Set("Sound Capture", "PowerOn", c.VasPowerOnDb.ToString(CultureInfo.InvariantCulture));
        d.Set("Sound Capture", "PowerOff", c.VasPowerOffDb.ToString(CultureInfo.InvariantCulture));
        d.SetInt("Sound Capture", "Prebuffer", c.PrebufferBlocks);

        d.SetInt("Sound Play", "DeviceID", c.PlaybackDeviceId);
        d.SetInt("Sound Play", "Volume", c.Volume);
        d.SetInt("Sound Play", "MaxBufferLen", c.MaxBufferLenMs);
        d.SetInt("Sound Play", "MinBufferLen", c.MinBufferLenMs);
        d.SetInt("Sound Play", "AddonBuffer", c.AddonBufferMs);
        d.SetBool("Sound Play", "TSC", c.TscEnabled);
        d.SetBool("Sound Play", "AGC", c.PlaybackAgc);

        d.SetInt("Files", "ChunkSizeKB", c.ChunkSizeKb);
        d.SetBool("Files", "AutoAccept", c.AutoAccept);
        d.Set("Files", "Path", c.ReceivePath);
        d.SetInt("Files", "OverwriteAction", (int)c.OverwriteAction);
        d.SetInt("Files", "ConfirmTimeoutSec", c.ConfirmTimeoutSec);

        d.Set("Alerts", "TextReceived", c.SndTextReceived);
        d.Set("Alerts", "TextSent", c.SndTextSent);
        d.Set("Alerts", "SoundCall", c.SndCall);
        d.Set("Alerts", "FileRequest", c.SndFileRequest);
        d.Set("Alerts", "FileAccept", c.SndFileAccepted);
        d.Set("Alerts", "FileReject", c.SndFileRejected);
        d.Set("Alerts", "ClientOnline", c.SndClientOnline);
        d.Set("Alerts", "ClientOffline", c.SndClientOffline);
        d.SetBool("Alerts", "FlashTaskBar", c.FlashTaskBar);
        d.SetBool("Alerts", "ShowBalloonNotify", c.ShowBalloonNotify);

        d.SetBool("Clipboard", "Receive", c.ClipboardReceive);
        d.SetBool("Clipboard", "AutoOpenImages", c.ClipboardAutoOpenImages);

        d.SetBool("Answer", "Enabled", c.AnswerMachineEnabled);
        d.Set("Answer", "Text", c.AnswerMachineText);
        d.SetBool("Answer", "Voice", c.AnswerVoiceEnabled);
        d.Set("Answer", "VoicePath", c.AnswerVoicePath);

        d.SetBool("SoundHistory", "Enabled", c.SoundHistoryEnabled);
        d.Set("SoundHistory", "Format", c.SoundHistoryFormat);
        d.Set("SoundHistory", "Path", c.SoundHistoryPath);

        d.SetInt("Hotkeys", "ShowHide", c.HotkeyShowHide);
        d.SetInt("Hotkeys", "DisplayOff", c.HotkeyDisplayOff);
        d.SetInt("Hotkeys", "MuteAll", c.HotkeyMuteAll);
        d.SetInt("Hotkeys", "PushToTalk", c.HotkeyPushToTalk);

        d.SetBool("Filters", "AllowText", c.AllowText);
        d.SetBool("Filters", "AllowSound", c.AllowSound);
        d.SetBool("Filters", "AllowFiles", c.AllowFiles);
        d.SetBool("Filters", "AllowP2P", c.AllowP2p);
        d.SetBool("Filters", "ServerOnly", c.ServerOnlyTraffic);
        d.SetBool("Filters", "AutoAddServers", c.AutoAddServers);
        d.SetBool("Filters", "IgnoreAll", c.IgnoreAllPackets);
        d.Set("Filters", "IpAllow", string.Join("|", c.IpAllowList));
        d.Set("Filters", "IpDeny", string.Join("|", c.IpDenyList));

        // Позиции окон: перезаписываем секцию целиком.
        foreach (var kv in c.DialogPositions)
            d.Set("WindowPos", kv.Key, FormattableString.Invariant(
                $"{kv.Value.X},{kv.Value.Y},{kv.Value.Width},{kv.Value.Height}"));
    }

    private static List<string> SplitList(string s) =>
        string.IsNullOrWhiteSpace(s)
            ? []
            : s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
