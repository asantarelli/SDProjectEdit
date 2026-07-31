using System.Text;

namespace SDProjectEdit.Core.Io;

/// <summary>
/// Rasgos físicos del archivo que hay que preservar al reescribirlo: BOM, fin de línea,
/// declaración XML, salto final e indentación. Los .cwproj que genera Clarion son
/// UTF-8 con BOM, CRLF, sin declaración XML y sin salto de línea final.
/// </summary>
public sealed record TextFileFormat(
    bool Utf8Bom,
    string LineEnding,
    bool HasXmlDeclaration,
    bool TrailingNewLine,
    string Indent)
{
    public static readonly TextFileFormat ClarionDefault =
        new(Utf8Bom: true, LineEnding: "\r\n", HasXmlDeclaration: false, TrailingNewLine: false, Indent: "  ");

    public Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: Utf8Bom);

    /// <summary>Lee el archivo y deduce su formato físico, devolviendo también el texto sin BOM.</summary>
    public static TextFileFormat Detect(string path, out string text)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

        var crlf = CountOccurrences(text, "\r\n");
        var lf = CountOccurrences(text, "\n");
        var lineEnding = crlf > 0 && crlf >= lf - crlf ? "\r\n" : "\n";

        var hasDecl = text.TrimStart('﻿').StartsWith("<?xml", StringComparison.Ordinal);
        var trailing = text.EndsWith('\n');
        var indent = DetectIndent(text);

        return new TextFileFormat(bom, lineEnding, hasDecl, trailing, indent);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            count++;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    private static string DetectIndent(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] is not (' ' or '\t')) continue;
            var indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            if (indent.Length > 0) return indent;
        }
        return "  ";
    }
}
