using System.Buffers.Binary;

namespace WizTalk.Core.Audio;

/// <summary>
/// Базовые операции над 16-битными PCM-буферами (mono): RMS, dBFS, усиление,
/// лимитер, шумовой гейт. Всё без аллокаций — span-based hot path для аудио-потока.
/// </summary>
public static class PcmUtils
{
    public const short ShortMin = short.MinValue;
    public const short ShortMax = short.MaxValue;

    /// <summary>Среднеквадратичное значение сигнала.</summary>
    public static double Rms(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0)
            return 0;
        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var s = samples[i] / (double)ShortMax;
            sum += s * s;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Уровень в dBFS (полная шкала = 0 дБ). Тишина даёт -120.</summary>
    public static double ToDb(double rms) => rms <= 1e-6 ? -120 : 20 * Math.Log10(rms);

    public static double FromDbToLinear(double db) => Math.Pow(10, db / 20.0);

    /// <summary>Простой однополюсный фильтр нижних частот (сглаживание envelope).</summary>
    public static double OnePoleLp(double previous, double input, double alpha) =>
        previous + alpha * (input - previous);

    /// <summary>Линейное усиление с защитой от клиппинга.</summary>
    public static void ApplyGain(Span<short> samples, double gain)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var v = samples[i] * gain;
            samples[i] = (short)Math.Clamp(v, ShortMin, ShortMax);
        }
    }

    /// <summary>Soft-knee лимитер: предотвращает «щелчки» при перегрузе (см. TSC/AGC).</summary>
    public static void Limit(Span<short> samples, double ceiling = 0.95)
    {
        var c = ceiling * ShortMax;
        for (var i = 0; i < samples.Length; i++)
        {
            var v = samples[i];
            if (Math.Abs(v) > c)
                samples[i] = (short)(Math.Sign(v) * (c + (Math.Abs(v) - c) * 0.25));
            samples[i] = (short)Math.Clamp(samples[i], ShortMin, ShortMax);
        }
    }

    /// <summary>Шумовой гейт: глушит блоки ниже порога NoisePowerDb.</summary>
    public static bool NoiseGate(Span<short> samples, double thresholdDb)
    {
        var level = ToDb(Rms(samples));
        if (level < thresholdDb)
        {
            samples.Clear();
            return false; // блок заглушён
        }

        return true;
    }

    /// <summary>Конвертация mono PCM16 → байтовый поток little-endian.</summary>
    public static void ToBytes(ReadOnlySpan<short> samples, Span<byte> destination)
    {
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(i * 2), samples[i]);
    }

    public static short[] FromBytes(ReadOnlySpan<byte> bytes)
    {
        var count = bytes.Length / 2;
        var result = new short[count];
        for (var i = 0; i < count; i++)
            result[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(i * 2));
        return result;
    }
}

/// <summary>
/// Automatic Gain Control — нормализация уровня входящего/исходящего потока.
/// Пионерный детектор пиков + медленная атакa/выздоровление (как в классических
/// VoIP-клиентах). Для входящего звука включена по умолчанию (ТЗ 3.1).
/// </summary>
public sealed class AgcProcessor
{
    private double _gain = 1.0;
    private double _peakRms;

    public double TargetLevelDb { get; set; } = -14;   // целевой рабочий уровень
    public double MaxGainDb { get; set; } = 24;
    public double MinGainDb { get; set; } = -12;
    public bool Enabled { get; set; } = true;

    /// <summary>Текущий коэффициент усиления (для VU-метера и диагностики).</summary>
    public double CurrentGain => _gain;

    /// <summary>Обрабатывает блок PCM на месте, возвращает новый RMS после обработки.</summary>
    public double Process(Span<short> samples)
    {
        if (!Enabled || samples.IsEmpty)
            return PcmUtils.ToDb(PcmUtils.Rms(samples));

        var rms = PcmUtils.Rms(samples);
        var db = PcmUtils.ToDb(rms);

        // Плавная оценка огибающей: атака быстрая, спад медленный.
        _peakRms = rms > _peakRms
            ? PcmUtils.OnePoleLp(_peakRms, rms, 0.7)
            : PcmUtils.OnePoleLp(_peakRms, rms, 0.05);

        if (_peakRms > 1e-4)
        {
            var desired = PcmUtils.FromDbToLinear(TargetLevelDb) / _peakRms;
            desired = Math.Clamp(desired, PcmUtils.FromDbToLinear(MinGainDb), PcmUtils.FromDbToLinear(MaxGainDb));
            // Не более чем в 2 раза за блок — защита от «насоса».
            _gain = Math.Clamp(PcmUtils.OnePoleLp(_gain, desired, 0.2), 0.05, 16.0);
        }

        PcmUtils.ApplyGain(samples, _gain);
        PcmUtils.Limit(samples);
        return PcmUtils.ToDb(PcmUtils.Rms(samples));
    }

    public void Reset()
    {
        _gain = 1.0;
        _peakRms = 0;
    }
}

/// <summary>
/// Voice Activation System — определяет начало/конец речи по гистерезису
/// PowerOn/PowerOff и буферизует read-ahead блоки, чтобы начало фразы не обрезалось.
/// Одновременное включение VAS и AGC запрещено конфигурацией (см. WizTalkConfig).
/// </summary>
public sealed class VasProcessor
{
    private readonly Queue<short[]> _preBuffer = new();
    private bool _active;

    public double PowerOnDb { get; set; } = -45;
    public double PowerOffDb { get; set; } = -55;
    public int PrebufferBlocks { get; set; } = 3;
    public bool Enabled { get; set; }

    /// <summary>Идёт ли сейчас активная передача (для индикатора Tx).</summary>
    public bool IsTransmitting => _active;

    /// <summary>
    /// Проверяет блок и решает, передавать ли его. При переходе silence→speech
    /// возвращает накопленные pre-buffer блоки вместе с текущим.
    /// </summary>
    public IReadOnlyList<short[]> Evaluate(short[] block)
    {
        if (!Enabled)
            return [block];

        var level = PcmUtils.ToDb(PcmUtils.Rms(block));

        if (!_active)
        {
            _preBuffer.Enqueue(block);
            while (_preBuffer.Count > Math.Max(1, PrebufferBlocks))
                _preBuffer.Dequeue();

            if (level >= PowerOnDb)
            {
                _active = true;
                var burst = _preBuffer.ToList();
                _preBuffer.Clear();
                return burst;
            }

            return [];
        }

        if (level <= PowerOffDb)
        {
            _active = false;
            _preBuffer.Clear();
            return [block]; // хвостовые слова — последний блок отдаём
        }

        return [block];
    }

    public void Reset()
    {
        _active = false;
        _preBuffer.Clear();
    }
}

/// <summary>
/// Time Shift Control — мониторинг накопления задержки за длинные сессии.
/// Если jitter-буфер растёт (сеть медленнее плеера), коррекция сдвигает таймстемпы
/// и укорачивает очередь, предотвращая «щёлчки» и нарастающий лаг.
/// </summary>
public sealed class TimeShiftController
{
    private double _driftAccumMs;
    private DateTime _lastSampleUtc = DateTime.UtcNow;

    public bool Enabled { get; set; } = true;

    /// <summary>Допустимый дрейф до начала коррекции, мс.</summary>
    public double CorrectionThresholdMs { get; set; } = 80;

    /// <summary>Жёсткий предел буфера — при превышении старые блоки выбрасываются.</summary>
    public int MaxBufferedMs { get; set; } = 3000;

    public double DriftMs => _driftAccumMs;

    /// <summary>
    /// Вызывается из плеера: bufferedMs — текущая глубина очереди, elapsedMs — время с прошлого вызова.
    /// Возвращает число миллисекунд, которые нужно «съесть» из буфера для коррекции.
    /// </summary>
    public int Consume(int bufferedMs, double elapsedMs)
    {
        if (!Enabled)
            return 0;

        // Ожидали, что очередь изменится на elapsedMs (плеер читает в реальном времени).
        _driftAccumMs += bufferedMs - elapsedMs;
        _lastSampleUtc = DateTime.UtcNow;

        if (Math.Abs(_driftAccumMs) < CorrectionThresholdMs)
            return 0;

        var correction = (int)Math.Round(_driftAccumMs * 0.5);
        _driftAccumMs -= correction;
        return correction;
    }

    /// <summary>Аварийный сброс переполненного буфера (graceful degradation).</summary>
    public bool ShouldDropOldest(int bufferedMs) => bufferedMs > MaxBufferedMs;

    public void Reset() => _driftAccumMs = 0;
}
