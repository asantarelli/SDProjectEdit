using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Tests;

public class DivergenceTests
{
    private static PropertyMatrix Analyze(SolutionFixture fixture) =>
        PropertyMatrix.Build(SolutionSet.Load(fixture.Root));

    private static PropertyRow Row(PropertyMatrix matrix, string scope, string name)
    {
        var key = new PropertyKey(
            scope == "general" ? PropertyScope.General : PropertyScope.For(scope), name);
        var row = matrix[key];
        Assert.NotNull(row);
        return row;
    }

    [Fact]
    public void Una_propiedad_identica_en_todos_es_Uniforme()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        fixture.WriteTypicalProject("Tres");

        var row = Row(Analyze(fixture), "Debug", "vid");

        Assert.Equal(UnificationStatus.Uniform, row.Status);
        Assert.Equal("full", row.MajorityValue);
        Assert.True(row.IsCandidate);
        Assert.False(row.NeedsDecision);
    }

    [Fact]
    public void Una_propiedad_con_valores_distintos_es_Divergente()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        fixture.WriteTypicalProject("Tres", releaseVid: "full");

        var row = Row(Analyze(fixture), "Release", "vid");

        Assert.Equal(UnificationStatus.Divergent, row.Status);
        Assert.Equal("off", row.MajorityValue);
        Assert.Equal(["Tres"], row.Outliers);
        Assert.True(row.NeedsDecision);
    }

    [Fact]
    public void Una_propiedad_que_falta_en_algunos_es_Parcial()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno", releaseExtra: ["    <GenerateMap>True</GenerateMap>"]);
        fixture.WriteTypicalProject("Dos");
        fixture.WriteTypicalProject("Tres");

        var row = Row(Analyze(fixture), "Release", "GenerateMap");

        Assert.Equal(UnificationStatus.Partial, row.Status);
        Assert.Equal(["Uno"], row.PresentIn);
        Assert.Equal(["Dos", "Tres"], row.AbsentIn);
        Assert.True(row.NeedsDecision);
    }

    [Fact]
    public void Falta_en_algunos_y_ademas_difiere_es_Parcial_mas_divergente()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno", releaseExtra: ["    <GenerateMap>True</GenerateMap>"]);
        fixture.WriteTypicalProject("Dos", releaseExtra: ["    <GenerateMap>False</GenerateMap>"]);
        fixture.WriteTypicalProject("Tres");

        var row = Row(Analyze(fixture), "Release", "GenerateMap");

        Assert.Equal(UnificationStatus.PartialDivergent, row.Status);
    }

    [Fact]
    public void Las_propiedades_por_proyecto_nunca_son_candidatas()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        var matrix = Analyze(fixture);

        foreach (var name in new[] { "DefineConstants", "Model", "OutputType", "AssemblyName", "ProjectGuid" })
        {
            var row = Row(matrix, "general", name);
            Assert.Equal(UnificationStatus.Blocked, row.Status);
            Assert.False(row.IsCandidate);
        }
    }

    [Fact]
    public void Una_propiedad_no_editable_queda_marcada_como_insegura()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteProject("Dos",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Debug' \">",
            "    <vid>",
            "      full",
            "    </vid>",
            "  </PropertyGroup>",
            "</Project>");

        var row = Row(Analyze(fixture), "Debug", "vid");

        Assert.False(row.SafeToEdit);
        Assert.False(row.IsCandidate);
        Assert.NotNull(row.UnsafeReason);
    }

    [Fact]
    public void Un_cwproj_fuera_del_sln_queda_excluido_y_se_avisa()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Dentro");
        fixture.WriteTypicalProject("Fuera");
        File.WriteAllText(Path.Combine(fixture.Root, "Test.sln"),
            "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
            "Project(\"{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}\") = \"Dentro\", \"Dentro.cwproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
            "EndProject\r\n");

        var solution = SolutionSet.Load(fixture.Root);

        Assert.Equal(["Dentro"], solution.Projects.Select(p => p.Name).ToArray());
        Assert.Contains(solution.Warnings, w => w.Message.Contains("Fuera.cwproj"));
    }

    [Fact]
    public void Con_all_se_incluye_el_cwproj_huerfano()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Dentro");
        fixture.WriteTypicalProject("Fuera");
        File.WriteAllText(Path.Combine(fixture.Root, "Test.sln"),
            "Project(\"{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}\") = \"Dentro\", \"Dentro.cwproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n");

        var solution = SolutionSet.Load(fixture.Root, onlySolutionProjects: false);

        Assert.Equal(2, solution.Projects.Count);
    }
}
