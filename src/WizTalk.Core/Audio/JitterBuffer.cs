namespace WizTalk.Core.Audio;

/// <summary>
/// Forward Error Correction для аудио (ТЗ 6.2): простая схема «дублирование с
/// задержкой» — каждый SOUND-пакет несёт закодированный предыдущий кадр в хвосте,
/// а fecMask в заголовке помечает восстановленные блоки. Приёмник достраивает
/// потерянные кадры из следующего пакета вместо concealment.
/// </summary>
public sealed class FecCodec
{
    private byte[] _previous = [];

    /// <summary>Число предшествующих кадров, которые можно восстановить (0..8 по bitmask).</summary>
    public int Depth { get; set; } = 1;

    /// <summary>
    /// Оборачивает payload: [lenCur:2][cur][lenPrev:2][prev]. Возвращает fecMask для заголовка.
    /// </summary>
    public (byte[] Encoded, byte Mask) Wrap(ReadOnlySpan<byte> currentFrame)
    {
        var prev = _previous;
        var result = new byte[4 + currentFrame.Length + prev.Length];
        BitConverter.TryWriteBytes(result.AsSpan(0), (ushort)currentFrame.Length);
        currentFrame.CopyTo(result.AsSpan(2));
        BitConverter.TryWriteBytes(result.AsSpan(2 + currentFrame.Length), (ushort)prev.Length);
        prev.CopyTo(result.AsSpan(4 + currentFrame.Length));

        var mask = (byte)((prev.Length > 0 ? 1 : 0) | (Depth >= 2 && prev.Length > 0 ? 2 : 0));
        _previous = currentFrame.ToArray();
        return (result, mask);
    }

    /// <summary>
    /// Распаковывает принятый блок. Если основной кадр повреждён/отсутствует,
    /// возвращает восстановленный из FEC предыдущего пакета.
    /// </summary>
    public bool Unwrap(ReadOnlySpan<byte> encoded, out byte[] mainFrame, out byte[] fecFrame)
    {
        mainFrame = [];
        fecFrame = [];
        if (encoded.Length < 4)
            return false;

        var curLen = BitConverter.ToUInt16(encoded.Slice(0, 2));
        if (2 + curLen + 2 > encoded.Length)
            return false;
        mainFrame = encoded.Slice(2, curLen).ToArray();

        var prevLen = BitConverter.ToUInt16(encoded.Slice(2 + curLen, 2));
        if (4 + curLen + prevLen > encoded.Length)
            return true; // main есть, FEC отсутствует — не фатально
        fecFrame = encoded.Slice(4 + curLen, prevLen).ToArray();
        return true;
    }

    public void Reset() => _previous = [];
}

/// <summary>
/// Jitter-buffer входящего звука: упорядочивание по sequence, детекция потерь,
/// восстановление через FEC и concealment, ограничение глубины (Min/MaxBufferLen).
/// Потокобезопасен: writer — сетевой поток, reader — аудио-поток плеера.
/// </summary>
public sealed class JitterBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly SortedDictionary<uint, short[]> _frames = [];
    private readonly FecCodec _fec = new();
    private readonly IAudioCodec _codec;
    private readonly Queue<short[]> _output = new();
    private uint? _nextSeq;
    private long _lastTickMs;

    public int SampleRate => _codec.SampleRate;

    /// <summary>Глубина очереди в блоках для оценки задержки/VU.</summary>
    public int BufferedBlocks
    {
        get
        {
            lock (_gate)
                return _frames.Count + _output.Count;
        }
    }

    public double LossRate { get; private set; }
    public long TotalReceived { get; private set; }
    public long TotalLost { get; private set; }

    public JitterBuffer(IAudioCodec codec)
    {
        _codec = codec;
    }

    /// <summary>Принимает декодированный SOUND-payload от сети.</summary>
    public void Put(uint sequence, ReadOnlySpan<byte> encodedPayload)
    {
        lock (_gate)
        {
            TotalReceived++;
            if (_nextSeq is null)
                _nextSeq = sequence;

            if (sequence < _nextSeq)
                return; // поздний пакет — уже воспроизведён/пропущен

            // FEC-содержимое кладём как черновик для seq+1, если его ещё нет.
            if (_fec.Unwrap(encodedPayload, out var main, out var fec))
            {
                DecodeTo(main, sequence);
                if (fec.Length > 0 && !_frames.ContainsKey(sequence + 1) && sequence + 1 > _nextSeq)
                    DecodeTo(fec, sequence + 1, fecRecovered: true);
            }

            DrainGaps();
        }
    }

    private void DecodeTo(byte[] encoded, uint seq, bool fecRecovered = false)
    {
        if (encoded.Length == 0 || _frames.ContainsKey(seq))
            return;
        var pcm = new short[_codec.SampleRate * _codec.FrameMs / 1000 + 64];
        var produced = _codec.Decode(encoded, pcm);
        if (produced > 0)
            _frames[seq] = pcm[..produced];
        else if (fecRecovered)
            _frames[seq] = pcm; // пустой восстановление-блок лучше тишины
    }

    /// <summary>Если пропуск слишком велик — вставляем concealment-блоки (graceful degradation).</summary>
    private void DrainGaps()
    {
        if (_nextSeq is null)
            return;
        // Оставляем ожидание FEC-восстановления: один блок задержки.
        while (_frames.Count > 0)
        {
            var first = _frames.Keys.First();
            if (first != _nextSeq)
                break;
            _output.Enqueue(_frames[first]);
            _frames.Remove(first);
            _nextSeq++;
        }

        // Пропущенные кадры, для которых FEC не помог, скрываем concealment'ом.
        if (_frames.Count > 0 && _nextSeq.HasValue)
        {
            var first = _frames.Keys.First();
            if (first > _nextSeq && first - _nextSeq <= 3)
            {
                var gap = (int)(first - _nextSeq!.Value);
                TotalLost += gap;
                for (var i = 0; i < gap; i++)
                {
                    var c = new short[_codec.SampleRate * _codec.FrameMs / 1000];
                    _codec.Conceal(c);
                    _output.Enqueue(c);
                    _nextSeq++;
                }
            }
        }

        var total = TotalReceived + TotalLost;
        LossRate = total > 0 ? (double)TotalLost / total : 0;
    }

    /// <summary>Забирает следующий готовый PCM-блок для воспроизведения.</summary>
    public short[]? Read()
    {
        lock (_gate)
        {
            if (_output.Count == 0)
                return null;
            var block = _output.Dequeue();
            _lastTickMs = Environment.TickCount64;
            return block;
        }
    }

    /// <summary>Сброс при завершении звонка.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
            _output.Clear();
            _nextSeq = null;
            _fec.Reset();
            _codec.ResetStateSafe();
            TotalReceived = TotalLost = 0;
        }
    }

    public void Dispose() => Clear();
}

internal static class CodecExtensions
{
    /// <summary>Сбрасывает состояние кодеков, реализующих Reset (ADPCM/Speex state).</summary>
    public static void ResetStateSafe(this IAudioCodec codec)
    {
        switch (codec)
        {
            case AdpcmCodec a:
                a.Reset();
                break;
            case OpusCodecStub o:
                o.Dispose();
                break;
        }
    }
}
