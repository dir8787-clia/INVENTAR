using System.Globalization;
using System.Text;

namespace WizTalk.Core.Config;

/// <summary>
/// Парсер/писер INI-файлов конфигурации (default.ini) в стиле классических
/// клиентов семейства WizSafe: разделы [Section], ключ = значение, комментарии «;» и «#».
/// Сохраняет порядок секций и ключей, значения читаются строго типизированными методами.
/// </summary>
public sealed class IniDocument
{
    private readonly List<Section> _sections = [];
    private readonly Dictionary<string, Section> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Section> Sections => _sections;

    public sealed class Section
    {
        public string Name { get; }
        public List<KeyValuePair<string, string?>> Entries { get; } = [];
        internal Section(string name) => Name = name;
    }

    public bool TryGet(string section, string key, out string? value)
    {
        if (_byName.TryGetValue(section, out var s))
        {
            for (var i = s.Entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(s.Entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = s.Entries[i].Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    public string GetString(string section, string key, string defaultValue = "") =>
        TryGet(section, key, out var v) ? v ?? defaultValue : defaultValue;

    public int GetInt(string section, string key, int defaultValue = 0) =>
        TryGet(section, key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : defaultValue;

    public long GetLong(string section, string key, long defaultValue = 0) =>
        TryGet(section, key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : defaultValue;

    public double GetDouble(string section, string key, double defaultValue = 0) =>
        TryGet(section, key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : defaultValue;

    /// <summary>INI-флаги хранятся как 0/1.</summary>
    public bool GetBool(string section, string key, bool defaultValue = false) =>
        TryGet(section, key, out var v)
            ? v is "1" or "true" or "yes" or "on"
            : defaultValue;

    public void Set(string section, string key, string value)
    {
        var s = GetOrCreateSection(section);
        for (var i = 0; i < s.Entries.Count; i++)
        {
            if (string.Equals(s.Entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                s.Entries[i] = new KeyValuePair<string, string?>(key, value);
                return;
            }
        }

        s.Entries.Add(new KeyValuePair<string, string?>(key, value));
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");
    public void SetInt(string section, string key, int value) => Set(section, key, value.ToString(CultureInfo.InvariantCulture));

    private Section GetOrCreateSection(string name)
    {
        if (!_byName.TryGetValue(name, out var s))
        {
            s = new Section(name);
            _sections.Add(s);
            _byName[name] = s;
        }

        return s;
    }

    /// <summary>Загружает документ из строки. Повреждённые линии пропускаются без падения.</summary>
    public static IniDocument Parse(string text)
    {
        var doc = new IniDocument();
        Section? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;
            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                if (end > 1)
                {
                    var name = line[1..end].Trim();
                    current = doc.GetOrCreateSection(name);
                }

                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            // Срезаем инлайновые комментарии только для значений без кавычек.
            if (!value.StartsWith('"'))
            {
                var cm = value.IndexOf(" ;", StringComparison.Ordinal);
                if (cm > 0)
                    value = value[..cm].TrimEnd();
            }
            else if (value.Length > 1 && value[^1] == '"')
            {
                value = value[1..^1];
            }

            current.Entries.Add(new KeyValuePair<string, string?>(key, value));
            // Позволяем записи до объявления секции ([Local] выше по файлу).
            doc._byName.TryAdd(current.Name, current);
        }

        return doc;
    }

    public static IniDocument Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new IniDocument();

    /// <summary>Сохраняет документ с сохранением исходного порядка секций/ключей.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        foreach (var s in _sections)
        {
            sb.Append('[').Append(s.Name).Append(']').Append("\r\n");
            foreach (var e in s.Entries)
                sb.Append(e.Key).Append(" = ").Append(e.Value).Append("\r\n");
            sb.Append("\r\n");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Возвращает копию документа (для атомарной перезагрузки настроек).</summary>
    public IniDocument Clone()
    {
        var copy = new IniDocument();
        foreach (var s in _sections)
        {
            var ns = copy.GetOrCreateSection(s.Name);
            ns.Entries.AddRange(s.Entries);
        }

        return copy;
    }
}
