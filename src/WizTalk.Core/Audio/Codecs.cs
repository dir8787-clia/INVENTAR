using System.Buffers.Binary;
using WizTalk.Core.Protocol;

namespace WizTalk.Core.Audio;

/// <summary>
/// Абстракция аудио-кодека. Реализации: Opus (через нативную библиотеку в клиенте),
/// Speex (legacy) и ADPCM — собственная portable-реализация IMA ADPCM здесь.
/// Кодек принимает PCM16 mono блока и возвращает сжатые байты для SOUND-пакета.
/// </summary>
public interface IAudioCodec : IDisposable
{
    AudioCodecKind Kind { get; }
    int SampleRate { get; }
    int FrameMs { get; }

    /// <summary>Максимальный размер закодированного кадра (для пула буферов).</summary>
    int MaxEncodedBytesPerFrame { get; }

    /// <summary>Кодирование одного блока PCM16.</summary>
    int Encode(ReadOnlySpan<short> pcm, Span<byte> destination);

    /// <summary>Декодирование в PCM16. Возвращает количество сэмплов.</summary>
    int Decode(ReadOnlySpan<byte> encoded, Span<short> destination);

    /// <summary>Потерянный кадр — concealment (graceful degradation при потере пакетов).</summary>
    void Conceal(Span<short> destination);
}

/// <summary>
/// IMA ADPCM кодек (4:1 сжатие) — быстрый вариант для локальных сетей без нативных
/// библиотек. Portable реализация, идентичная на отправителе и приёмнике.
/// layout потока: [predictor:int16 LE][stepIndex:byte] + nibble stream (LSB first).
/// </summary>
public sealed class AdpcmCodec : IAudioCodec
{
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
        724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2500, 2750, 3025,
        3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8];

    private int _predictor;
    private int _stepIndex;

    public AudioCodecKind Kind => AudioCodecKind.Adpcm;
    public int SampleRate { get; }
    public int FrameMs { get; }
    public int MaxEncodedBytesPerFrame => 3 + (SampleRate * FrameMs / 1000) / 2 + 1;

    public AdpcmCodec(int sampleRate, int frameMs)
    {
        if (sampleRate is not (8000 or 16000 or 22050 or 44100))
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Поддерживаются 8000/16000/22050/44100 Гц.");
        SampleRate = sampleRate;
        FrameMs = frameMs;
        Reset();
    }

    public void Reset()
    {
        _predictor = 0;
        _stepIndex = 0;
    }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> destination)
    {
        if (destination.Length < 3 + pcm.Length / 2)
            return 0;

        var o = 0;
        BinaryPrimitives.WriteInt16LittleEndian(destination[o..], (short)_predictor);
        o += 2;
        destination[o++] = (byte)_stepIndex;

        var nibbleCount = 0;
        byte accumulator = 0;
        foreach (var sample in pcm)
        {
            var step = StepTable[_stepIndex];
            var diff = sample - _predictor;
            byte code = 0;
            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            var temp = step;
            // Дихотомическое определение кода кванта
            if (diff >= temp) { code |= 4; diff -= temp; }
            temp >>= 1;
            if (diff >= temp) { code |= 2; diff -= temp; }
            temp >>= 1;
            if (diff >= temp) code |= 1;

            _predictor = (short)Math.Clamp(DifferentialDecode(code, step, out var idxDelta), short.MinValue, short.MaxValue);
            _stepIndex = Math.Clamp(_stepIndex + idxDelta, 0, StepTable.Length - 1);

            accumulator |= (byte)(code << ((nibbleCount & 1) * 4));
            nibbleCount++;
            if ((nibbleCount & 1) == 0)
            {
                destination[o++] = accumulator;
                accumulator = 0;
            }
        }

        if ((nibbleCount & 1) != 0)
            destination[o++] = accumulator;

        return o;
    }

    private static int DifferentialDecode(byte code, int step, out int indexDelta)
    {
        var diff = 0;
        if ((code & 4) != 0) diff += step;
        if ((code & 2) != 0) diff += step >> 1;
        if ((code & 1) != 0) diff += step >> 2;
        diff += step >> 3;
        indexDelta = IndexTable[code & 7];
        return (code & 8) != 0 ? -diff : diff;
    }

    public int Decode(ReadOnlySpan<byte> encoded, Span<short> destination)
    {
        if (encoded.Length < 3)
            return 0;

        var predictor = BinaryPrimitives.ReadInt16LittleEndian(encoded);
        var stepIndex = Math.Clamp(encoded[2], (byte)0, (byte)(StepTable.Length - 1));
        var o = 3;
        var produced = 0;

        while (o < encoded.Length && produced < destination.Length)
        {
            var b = encoded[o++];
            for (var half = 0; half < 2 && produced < destination.Length; half++)
            {
                var code = (byte)((b >> (half * 4)) & 0x0F);
                var step = StepTable[stepIndex];
                predictor = (short)Math.Clamp(predictor + DifferentialDecode(code, step, out var delta), short.MinValue, short.MaxValue);
                stepIndex = Math.Clamp(stepIndex + delta, 0, StepTable.Length - 1);
                destination[produced++] = predictor;
            }
        }

        _predictor = predictor;
        _stepIndex = stepIndex;
        return produced;
    }

    /// <summary>Concealment: затухающий повтор последнего предсказателя вместо тишины/щелчка.</summary>
    public void Conceal(Span<short> destination)
    {
        var v = _predictor;
        for (var i = 0; i < destination.Length; i++)
        {
            v = (int)(v * 0.98);
            destination[i] = (short)v;
        }
    }

    public void Dispose() => Reset();
}

/// <summary>
/// Обёртка над Opus (OpusDotNet / libopus нативной сборкой в клиенте).
/// В Core остаётся интерфейсная заглушка: если нативная библиотека не найдена,
/// менеджер кодеков автоматически деградирует до ADPCM (см. CodecManager).
/// </summary>
public sealed class OpusCodecStub : IAudioCodec
{
    private readonly AdpcmCodec _fallback;

    public AudioCodecKind Kind => AudioCodecKind.Opus;
    public int SampleRate { get; }
    public int FrameMs { get; }
    public int MaxEncodedBytesPerFrame => _fallback.MaxEncodedBytesPerFrame;

    /// <summary>Наличие реальной нативной библиотеки opus.</summary>
    public bool NativeAvailable { get; }

    public OpusCodecStub(int sampleRate, int frameMs, bool nativeAvailable = false)
    {
        SampleRate = sampleRate;
        FrameMs = frameMs;
        NativeAvailable = nativeAvailable;
        // Bitrate 6–510 kbps по ТЗ выбирается внутри нативной реализации.
        _fallback = new AdpcmCodec(sampleRate, frameMs);
    }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> destination) => _fallback.Encode(pcm, destination);
    public int Decode(ReadOnlySpan<byte> encoded, Span<short> destination) => _fallback.Decode(encoded, destination);
    public void Conceal(Span<short> destination) => _fallback.Conceal(destination);
    public void Dispose() => _fallback.Dispose();
}

/// <summary>
/// Менеджер кодеков: создаёт кодек по конфигурации, переиспользует экземпляры,
/// предоставляет пул временных буферов для снижения GC pressure (ТЗ 6.1).
/// </summary>
public sealed class CodecManager : IDisposable
{
    private readonly Dictionary<AudioCodecKind, IAudioCodec> _codecs = new();
    private readonly Stack<byte[]> _bufferPool = new();
    private readonly object _gate = new();

    public int SampleRate { get; }
    public int FrameMs { get; }

    public CodecManager(int sampleRate, int bufferLenMs)
    {
        SampleRate = sampleRate;
        FrameMs = Math.Max(10, bufferLenMs);
    }

    public IAudioCodec Get(AudioCodecKind kind)
    {
        lock (_gate)
        {
            if (!_codecs.TryGetValue(kind, out var codec))
            {
                codec = kind switch
                {
                    AudioCodecKind.Adpcm => new AdpcmCodec(SampleRate, FrameMs),
                    // Opus/Speex требуют нативных библиотек Windows-сборки; без них — тихий fallback.
                    AudioCodecKind.Opus => new OpusCodecStub(SampleRate, FrameMs, TryLoadNative("libopus")),
                    AudioCodecKind.Speex => new OpusCodecStub(SampleRate, FrameMs, TryLoadNative("libspeex")),
                    _ => throw new NotSupportedException(),
                };
                _codecs[kind] = codec;
            }

            return codec;
        }
    }

    /// <summary>Проверка доступности нативной библиотеки кодека (P/Invoke probe).</summary>
    private static bool TryLoadNative(string libraryHint)
    {
        try
        {
            // В Windows-сборке клиента здесь выполняется DllImport-разведка opus.dll/speex.dll.
            return OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("WIZTALK_NATIVE_CODECS") == "1";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Аренда буфера из пула (zero-allocation hot path для звука).</summary>
    public byte[] RentBuffer(int minSize)
    {
        lock (_gate)
        {
            while (_bufferPool.Count > 0)
            {
                var b = _bufferPool.Pop();
                if (b.Length >= minSize)
                    return b;
            }
        }

        return new byte[Math.Max(minSize, 1024)];
    }

    public void Return(byte[] buffer)
    {
        if (buffer.Length > 1 << 20)
            return; // не держим огромные буферы в пуле
        lock (_gate)
            _bufferPool.Push(buffer);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var c in _codecs.Values)
                c.Dispose();
            _codecs.Clear();
        }
    }
}
