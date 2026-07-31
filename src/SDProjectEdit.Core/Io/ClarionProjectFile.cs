using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Io;

/// <summary>Una propiedad tal como aparece físicamente en un .cwproj.</summary>
public sealed record ProjectProperty(
    PropertyKey Key,
    string Value,
    string? OwnCondition,
    int LineIndex,
    bool SingleLine,
    XElement Element)
{
    public string Name => Key.Name;
    public PropertyScope Scope => Key.Scope;

    /// <summary>Editable automáticamente sólo si ocupa exactamente una línea y no trae Condition propia.</summary>
    public bool IsSafeToRemove => SingleLine && string.IsNullOrEmpty(OwnCondition);
}

/// <summary>Un PropertyGroup del .cwproj, con su ámbito ya resuelto.</summary>
public sealed record ProjectPropertyGroup(
    PropertyScope Scope,
    string? RawCondition,
    bool ScopeRecognized,
    int StartLineIndex,
    int EndLineIndex,
    XElement Element);

/// <summary>
/// Un .cwproj cargado: el XML para analizar y las líneas crudas para editar.
/// </summary>
public sealed class ClarionProjectFile
{
    public const string MsBuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

    private static readonly Regex ConfigurationOnly = new(
        @"^\s*'\$\(Configuration\)'\s*==\s*'(?<cfg>[^']*)'\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConfigurationAndPlatform = new(
        @"^\s*'\$\(Configuration\)\|\$\(Platform\)'\s*==\s*'(?<cfg>[^'|]*)\|(?<plat>[^']*)'\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private ClarionProjectFile(
        string path,
        string text,
        TextFileFormat format,
        XDocument document,
        IReadOnlyList<ProjectPropertyGroup> groups,
        IReadOnlyList<ProjectProperty> properties)
    {
        Path = path;
        OriginalText = text;
        Format = format;
        Document = document;
        PropertyGroups = groups;
        Properties = properties;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string FileName => System.IO.Path.GetFileName(Path);
    public string OriginalText { get; }
    public TextFileFormat Format { get; }
    public XDocument Document { get; }
    public IReadOnlyList<ProjectPropertyGroup> PropertyGroups { get; }
    public IReadOnlyList<ProjectProperty> Properties { get; }

    /// <summary>Configurations que este proyecto declara mediante PropertyGroup condicionales.</summary>
    public IEnumerable<string> Configurations => PropertyGroups
        .Where(g => !g.Scope.IsGeneral && g.ScopeRecognized)
        .Select(g => g.Scope.Configuration!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>PropertyGroup condicionales cuya Condition no supimos interpretar. Nunca se tocan.</summary>
    public IEnumerable<ProjectPropertyGroup> UnrecognizedGroups => PropertyGroups.Where(g => !g.ScopeRecognized);

    public static ClarionProjectFile Load(string path)
    {
        var format = TextFileFormat.Detect(path, out var text);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{System.IO.Path.GetFileName(path)}: XML inválido ({ex.Message}).", ex);
        }

        var root = doc.Root ?? throw new InvalidDataException($"{System.IO.Path.GetFileName(path)}: sin elemento raíz.");
        var lines = LineEditor.SplitLines(text);

        var groups = new List<ProjectPropertyGroup>();
        var properties = new List<ProjectProperty>();

        foreach (var groupElement in root.Elements(XName.Get("PropertyGroup", MsBuildNamespace)))
        {
            var condition = (string?)groupElement.Attribute("Condition");
            var scope = ParseScope(condition, out var recognized);
            var startLine = LineIndexOf(groupElement);
            var endLine = FindClosingLine(lines, startLine, "</PropertyGroup>");

            groups.Add(new ProjectPropertyGroup(scope, condition, recognized, startLine, endLine, groupElement));

            foreach (var propElement in groupElement.Elements())
            {
                var lineIndex = LineIndexOf(propElement);
                var name = propElement.Name.LocalName;
                var singleLine = lineIndex >= 0 && lineIndex < lines.Count && IsWholeLineElement(lines[lineIndex], name);
                properties.Add(new ProjectProperty(
                    new PropertyKey(scope, name),
                    propElement.Value,
                    (string?)propElement.Attribute("Condition"),
                    lineIndex,
                    singleLine,
                    propElement));
            }
        }

        return new ClarionProjectFile(path, text, format, doc, groups, properties);
    }

    /// <summary>Índice 0-based de la línea donde arranca el elemento, o -1 si el XML no traía line info.</summary>
    public static int LineIndexOf(XElement element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber - 1 : -1;

    /// <summary>
    /// Verifica que la línea contenga el elemento completo y nada más: <c>&lt;Name&gt;valor&lt;/Name&gt;</c>
    /// o <c>&lt;Name /&gt;</c>. Si no, no la borramos automáticamente.
    /// </summary>
    private static bool IsWholeLineElement(string line, string elementName)
    {
        var t = line.Trim();
        if (!t.StartsWith('<') || !t.EndsWith('>')) return false;
        if (!t.StartsWith("<" + elementName, StringComparison.Ordinal)) return false;
        var after = t.Length > elementName.Length + 1 ? t[elementName.Length + 1] : '\0';
        if (after is not ('>' or ' ' or '/' or '\t')) return false; // evita <vidx> matcheando <vid>
        return t.EndsWith("</" + elementName + ">", StringComparison.Ordinal) || t.EndsWith("/>", StringComparison.Ordinal);
    }

    private static int FindClosingLine(List<string> lines, int startLine, string closeTag)
    {
        if (startLine < 0) return -1;
        for (var i = startLine; i < lines.Count; i++)
            if (lines[i].Trim() == closeTag) return i;
        return -1;
    }

    private static PropertyScope ParseScope(string? condition, out bool recognized)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            recognized = true;
            return PropertyScope.General;
        }

        var m = ConfigurationOnly.Match(condition);
        if (m.Success)
        {
            recognized = true;
            return PropertyScope.For(m.Groups["cfg"].Value);
        }

        m = ConfigurationAndPlatform.Match(condition);
        if (m.Success)
        {
            recognized = true;
            return PropertyScope.For(m.Groups["cfg"].Value);
        }

        // Condition que no entendemos: la aislamos en un ámbito propio para que nunca se unifique.
        recognized = false;
        return PropertyScope.For("?" + condition.Trim());
    }

    public ProjectProperty? Find(PropertyKey key) => Properties.FirstOrDefault(p => p.Key.Equals(key));

    public string? GetValue(PropertyKey key) => Find(key)?.Value;

    /// <summary>El Import a Common.props si ya está presente, o null.</summary>
    public XElement? FindCommonPropsImport(string commonPropsFileName) =>
        Document.Root?
            .Elements(XName.Get("Import", MsBuildNamespace))
            .FirstOrDefault(e =>
            {
                var project = (string?)e.Attribute("Project") ?? "";
                return project.EndsWith(commonPropsFileName, StringComparison.OrdinalIgnoreCase);
            });

    public LineEditor CreateEditor() => new(OriginalText, Format);
}
