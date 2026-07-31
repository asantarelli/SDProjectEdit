using System.Xml.Linq;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Io;

/// <summary>
/// El Common.props compartido en la raíz del solution. Este archivo lo genera y regenera
/// la herramienta: se conservan todas las propiedades que tenga (incluidas las agregadas a mano),
/// pero no los comentarios propios ni el orden manual.
/// </summary>
public sealed class CommonPropsFile
{
    public const string DefaultFileName = "Common.props";

    private readonly Dictionary<PropertyKey, string> _values = [];

    public CommonPropsFile(string path) => Path = path;

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool ExistedOnDisk { get; private set; }

    public IReadOnlyDictionary<PropertyKey, string> Values => _values;

    public IEnumerable<PropertyKey> Keys => _values.Keys.OrderBy(k => k);

    public static CommonPropsFile Load(string path)
    {
        var file = new CommonPropsFile(path);
        if (!File.Exists(path)) return file;

        file.ExistedOnDisk = true;
        var project = ClarionProjectFile.Load(path);
        foreach (var property in project.Properties)
            file._values[property.Key] = property.Value;
        return file;
    }

    public string? Get(PropertyKey key) => _values.GetValueOrDefault(key);

    public void Set(PropertyKey key, string value) => _values[key] = value;

    public bool Remove(PropertyKey key) => _values.Remove(key);

    public void Clear() => _values.Clear();

    /// <summary>Serializa el archivo completo. No escribe nada en disco.</summary>
    public string Render(TextFileFormat format)
    {
        var nl = format.LineEnding;
        var indent = format.Indent;
        var sb = new System.Text.StringBuilder();

        sb.Append($"<Project xmlns=\"{ClarionProjectFile.MsBuildNamespace}\">").Append(nl);
        sb.Append(indent).Append("<!-- Generado por SDProjectEdit. Configuración de build compartida por todos los .cwproj").Append(nl);
        sb.Append(indent).Append("     del solution. Cada .cwproj lo importa antes de sus PropertyGroup condicionales, así que").Append(nl);
        sb.Append(indent).Append("     una propiedad que siga declarada en un .cwproj pisa a la de acá (override por proyecto). -->").Append(nl);

        foreach (var scope in _values.Keys.Select(k => k.Scope).Distinct().OrderBy(ScopeOrder).ThenBy(s => s.Display, StringComparer.OrdinalIgnoreCase))
        {
            var keys = _values.Keys.Where(k => k.Scope.Equals(scope)).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (keys.Count == 0) continue;

            sb.Append(indent).Append(scope.IsGeneral
                ? "<PropertyGroup>"
                : $"<PropertyGroup Condition=\" '$(Configuration)' == '{Escape(scope.Configuration!)}' \">").Append(nl);

            foreach (var key in keys)
                sb.Append(indent).Append(indent)
                  .Append($"<{key.Name}>{Escape(_values[key])}</{key.Name}>").Append(nl);

            sb.Append(indent).Append("</PropertyGroup>").Append(nl);
        }

        sb.Append("</Project>");
        if (format.TrailingNewLine) sb.Append(nl);
        return sb.ToString();
    }

    public void Save(TextFileFormat format)
    {
        var text = Render(format);
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
        using var stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (format.Utf8Bom) stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(bytes);
        ExistedOnDisk = true;
    }

    private static int ScopeOrder(PropertyScope scope) => scope.IsGeneral ? 0
        : scope.Configuration!.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? 1
        : scope.Configuration!.Equals("Release", StringComparison.OrdinalIgnoreCase) ? 2 : 3;

    private static string Escape(string value) => new XText(value).ToString();
}
