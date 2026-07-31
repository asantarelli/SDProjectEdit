namespace SDProjectEdit.Core.Io;

/// <summary>
/// Editor de texto por líneas. Trabajamos sobre las líneas crudas del archivo en lugar de
/// serializar de nuevo el XDocument: un round-trip por XmlWriter reescribiría el escapado de
/// entidades (por ejemplo <c>&amp;gt;</c> dentro de DefineConstants pasaría a <c>&gt;</c>),
/// generando diffs enormes en archivos que ni siquiera queríamos tocar. Editando líneas puntuales,
/// todo lo que no se modifica queda byte a byte idéntico.
/// </summary>
public sealed class LineEditor
{
    private readonly List<string> _lines;
    private readonly HashSet<int> _deleted = [];
    private readonly Dictionary<int, List<string>> _insertBefore = [];

    public LineEditor(string text, TextFileFormat format)
    {
        Format = format;
        _lines = SplitLines(text);
    }

    public TextFileFormat Format { get; }

    public int LineCount => _lines.Count;

    public bool HasChanges => _deleted.Count > 0 || _insertBefore.Count > 0;

    /// <summary>Línea en índice 0. Los números de línea de <c>IXmlLineInfo</c> son 1-based.</summary>
    public string this[int index] => _lines[index];

    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(text[start..end]);
            start = i + 1;
        }
        // Resto posterior al último salto. Si el texto termina en salto no agregamos una línea
        // vacía: ese salto final lo reconstruye Build() a partir de Format.TrailingNewLine.
        if (start < text.Length || text.Length == 0) lines.Add(text[start..]);
        return lines;
    }

    public void DeleteLine(int index)
    {
        if (index < 0 || index >= _lines.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Línea {index} fuera de rango (0..{_lines.Count - 1}).");
        _deleted.Add(index);
    }

    public void DeleteRange(int firstIndex, int lastIndex)
    {
        for (var i = firstIndex; i <= lastIndex; i++) DeleteLine(i);
    }

    public void InsertBefore(int index, params string[] newLines)
    {
        if (index < 0 || index > _lines.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (!_insertBefore.TryGetValue(index, out var list))
            _insertBefore[index] = list = [];
        list.AddRange(newLines);
    }

    public bool IsDeleted(int index) => _deleted.Contains(index);

    /// <summary>Busca hacia adelante la primera línea cuyo contenido recortado coincida exactamente.</summary>
    public int FindLine(string trimmedContent, int fromIndex)
    {
        for (var i = fromIndex; i < _lines.Count; i++)
            if (_lines[i].Trim() == trimmedContent) return i;
        return -1;
    }

    public string Build()
    {
        var sb = new System.Text.StringBuilder();
        var emitted = new List<string>();

        for (var i = 0; i < _lines.Count; i++)
        {
            if (_insertBefore.TryGetValue(i, out var pre)) emitted.AddRange(pre);
            if (!_deleted.Contains(i)) emitted.Add(_lines[i]);
        }
        if (_insertBefore.TryGetValue(_lines.Count, out var tail)) emitted.AddRange(tail);

        // La última línea del archivo original no lleva terminador salvo que TrailingNewLine sea true.
        for (var i = 0; i < emitted.Count; i++)
        {
            sb.Append(emitted[i]);
            if (i < emitted.Count - 1 || Format.TrailingNewLine) sb.Append(Format.LineEnding);
        }
        return sb.ToString();
    }

    public void Save(string path)
    {
        var text = Build();
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (Format.Utf8Bom) stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(bytes);
    }
}
