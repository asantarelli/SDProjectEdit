using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;
using SDProjectEdit.Core.Planning;

namespace SDProjectEdit.Core.Tests;

public class VerificationTests
{
    private static readonly MigrationOptions Options = new() { CreateBackup = false };

    private static MigrationPlan Migrate(SolutionFixture fixture)
    {
        var matrix = PropertyMatrix.Build(SolutionSet.Load(fixture.Root));
        var plan = MigrationPlan.Create(matrix, MigrationPlan.DefaultDecisions(matrix), Options);
        MigrationExecutor.Apply(plan, dryRun: false);
        return plan;
    }

    private static VerificationReport Verify(SolutionFixture fixture, IReadOnlySet<PropertyKey>? expected = null) =>
        Verifier.Run(SolutionSet.Load(fixture.Root), Options, expected);

    [Fact]
    public void Un_solution_migrado_pasa_todos_los_chequeos()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        var plan = Migrate(fixture);

        var report = Verify(fixture, plan.ExpectedOverrides);

        Assert.True(report.AllPassed);
    }

    [Fact]
    public void Sin_Common_props_la_verificacion_falla()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");

        var report = Verify(fixture);

        Assert.False(report.AllPassed);
        Assert.Contains(report.Checks, c => !c.Passed && c.Name.Contains("existe"));
    }

    [Fact]
    public void Si_a_un_cwproj_le_falta_el_Import_la_verificacion_falla()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        Migrate(fixture);

        var path = Path.Combine(fixture.Root, "Dos.cwproj");
        File.WriteAllText(path, fixture.ReadText("Dos.cwproj")
            .Replace("  <Import Project=\"$(MSBuildThisFileDirectory)Common.props\" />\r\n", ""));

        var report = Verify(fixture);

        Assert.False(report.AllPassed);
        Assert.Contains(report.Checks, c => !c.Passed && c.Details.Any(d => d.Contains("Dos.cwproj")));
    }

    [Fact]
    public void Un_Import_despues_de_los_grupos_condicionales_se_detecta()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteProject("Uno",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup>",
            "    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>",
            "  </PropertyGroup>",
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Release' \">",
            "    <vid>full</vid>",
            "  </PropertyGroup>",
            "  <Import Project=\"$(MSBuildThisFileDirectory)Common.props\" />",
            "</Project>");
        File.WriteAllText(fixture.CommonPropsPath,
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Release' \">\r\n" +
            "    <vid>off</vid>\r\n" +
            "  </PropertyGroup>\r\n" +
            "</Project>\r\n");

        var report = Verify(fixture);

        Assert.Contains(report.Checks, c => !c.Passed && c.Name.Contains("antes de los PropertyGroup"));
    }

    [Fact]
    public void Un_residuo_inesperado_hace_fallar_la_verificacion()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        Migrate(fixture);

        // Alguien vuelve a declarar a mano una propiedad ya centralizada.
        var path = Path.Combine(fixture.Root, "Dos.cwproj");
        File.WriteAllText(path, fixture.ReadText("Dos.cwproj").Replace(
            "  <Import Project=\"$(MSBuildThisFileDirectory)Common.props\" />",
            "  <Import Project=\"$(MSBuildThisFileDirectory)Common.props\" />\r\n" +
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Debug' \">\r\n" +
            "    <vid>off</vid>\r\n" +
            "  </PropertyGroup>"));

        var report = Verify(fixture, new HashSet<PropertyKey>());

        Assert.False(report.AllPassed);
        Assert.Contains(report.Checks, c => !c.Passed && c.Name.Contains("residuos"));
    }

    [Fact]
    public void Un_override_esperado_no_hace_fallar_la_verificacion()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos", releaseVid: "full");
        var key = new PropertyKey(PropertyScope.For("Release"), "vid");

        var matrix = PropertyMatrix.Build(SolutionSet.Load(fixture.Root));
        var plan = MigrationPlan.Create(matrix, MigrationPlan.DefaultDecisions(matrix)
            .Select(d => d.Key.Equals(key) ? new PropertyDecision(key, DecisionKind.UnifyKeepOverrides, "off") : d)
            .ToList(), Options);
        MigrationExecutor.Apply(plan, dryRun: false);

        var report = Verify(fixture, plan.ExpectedOverrides);

        Assert.True(report.AllPassed);
        Assert.Contains(report.Checks, c => c.Details.Any(d => d.Contains("Dos.cwproj")));
    }

    [Fact]
    public void Una_propiedad_dejada_por_proyecto_no_cuenta_como_residuo()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno", releaseExtra: ["    <GenerateMap>True</GenerateMap>"]);
        fixture.WriteTypicalProject("Dos");
        var plan = Migrate(fixture); // GenerateMap es Parcial -> queda en Leave

        // Después alguien la agrega a Common.props a mano, sin sacarla de Uno.cwproj.
        var common = CommonPropsFile.Load(fixture.CommonPropsPath);
        common.Set(new PropertyKey(PropertyScope.For("Release"), "GenerateMap"), "True");
        common.Save(TextFileFormat.ClarionDefault with { TrailingNewLine = true });

        var report = Verify(fixture, plan.ExpectedOverrides);

        Assert.True(report.AllPassed);
    }
}
