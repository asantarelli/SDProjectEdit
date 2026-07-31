using SDProjectEdit.Core.Io;

namespace SDProjectEdit.Core.Tests;

public class FileFormatTests
{
    [Fact]
    public void Detecta_el_formato_fisico_que_genera_Clarion()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteTypicalProject("Uno");

        var format = TextFileFormat.Detect(path, out var text);

        Assert.True(format.Utf8Bom);
        Assert.Equal("\r\n", format.LineEnding);
        Assert.False(format.HasXmlDeclaration);
        Assert.False(format.TrailingNewLine);
        Assert.Equal("  ", format.Indent);
        Assert.StartsWith("<Project", text);
    }

    [Fact]
    public void Un_editor_sin_cambios_reconstruye_el_texto_identico()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteTypicalProject("Uno");
        var project = ClarionProjectFile.Load(path);

        var rebuilt = project.CreateEditor().Build();

        Assert.Equal(project.OriginalText, rebuilt);
    }

    [Theory]
    [InlineData("a\r\nb\r\nc")]
    [InlineData("a\r\nb\r\nc\r\n")]
    [InlineData("a\nb\nc")]
    [InlineData("")]
    [InlineData("una sola linea")]
    public void El_round_trip_de_lineas_no_pierde_ni_agrega_nada(string text)
    {
        var format = new TextFileFormat(
            Utf8Bom: true,
            LineEnding: text.Contains("\r\n") ? "\r\n" : "\n",
            HasXmlDeclaration: false,
            TrailingNewLine: text.EndsWith('\n'),
            Indent: "  ");

        var editor = new LineEditor(text, format);

        Assert.Equal(text, editor.Build());
    }

    [Fact]
    public void Borrar_e_insertar_lineas_respeta_el_orden()
    {
        var format = TextFileFormat.ClarionDefault;
        var editor = new LineEditor("uno\r\ndos\r\ntres", format);

        editor.DeleteLine(1);
        editor.InsertBefore(2, "  nuevo");

        Assert.Equal("uno\r\n  nuevo\r\ntres", editor.Build());
    }

    [Fact]
    public void Guardar_preserva_el_BOM_y_no_agrega_salto_final()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteTypicalProject("Uno");
        var project = ClarionProjectFile.Load(path);

        project.CreateEditor().Save(path);

        var bytes = fixture.ReadBytes("Uno.cwproj");
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        Assert.NotEqual((byte)'\n', bytes[^1]);
    }
}
