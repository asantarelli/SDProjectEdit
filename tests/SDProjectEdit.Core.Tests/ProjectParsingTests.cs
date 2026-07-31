using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Tests;

public class ProjectParsingTests
{
    [Fact]
    public void Reconoce_el_grupo_general_y_los_condicionales()
    {
        using var fixture = new SolutionFixture();
        var project = ClarionProjectFile.Load(fixture.WriteTypicalProject("Uno"));

        Assert.Contains(project.PropertyGroups, g => g.Scope.IsGeneral);
        Assert.Equal(["Debug", "Release"], project.Configurations.ToArray());
        Assert.All(project.PropertyGroups, g => Assert.True(g.ScopeRecognized));
    }

    [Theory]
    [InlineData("'$(Configuration)' == 'Debug'", "Debug")]
    [InlineData(" '$(Configuration)' == 'Release' ", "Release")]
    [InlineData("'$(Configuration)|$(Platform)' == 'Debug|Win32'", "Debug")]
    public void Interpreta_las_dos_formas_de_Condition(string condition, string expected)
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteProject("Uno",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup>",
            "    <OutputType>Library</OutputType>",
            "  </PropertyGroup>",
            $"  <PropertyGroup Condition=\"{condition}\">",
            "    <vid>off</vid>",
            "  </PropertyGroup>",
            "</Project>");

        var project = ClarionProjectFile.Load(path);

        Assert.All(project.PropertyGroups, g => Assert.True(g.ScopeRecognized));
        Assert.Equal("off", project.GetValue(new PropertyKey(PropertyScope.For(expected), "vid")));
    }

    [Fact]
    public void Una_Condition_desconocida_deja_el_grupo_fuera_de_juego()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteProject("Uno",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup Condition=\" '$(OtraCosa)' == 'Si' \">",
            "    <vid>off</vid>",
            "  </PropertyGroup>",
            "</Project>");

        var project = ClarionProjectFile.Load(path);

        Assert.Single(project.UnrecognizedGroups);
    }

    [Fact]
    public void Una_propiedad_en_su_propia_linea_es_segura_de_remover()
    {
        using var fixture = new SolutionFixture();
        var project = ClarionProjectFile.Load(fixture.WriteTypicalProject("Uno"));

        var vid = project.Find(new PropertyKey(PropertyScope.For("Debug"), "vid"));

        Assert.NotNull(vid);
        Assert.True(vid.IsSafeToRemove);
    }

    [Fact]
    public void Una_propiedad_con_Condition_propia_no_es_segura_de_remover()
    {
        using var fixture = new SolutionFixture();
        var project = ClarionProjectFile.Load(fixture.WriteTypicalProject("Uno"));

        var configuration = project.Find(new PropertyKey(PropertyScope.General, "Configuration"));

        Assert.NotNull(configuration);
        Assert.False(configuration.IsSafeToRemove);
    }

    [Fact]
    public void Una_propiedad_repartida_en_varias_lineas_no_es_segura_de_remover()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteProject("Uno",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Release' \">",
            "    <vid>",
            "      off",
            "    </vid>",
            "  </PropertyGroup>",
            "</Project>");

        var project = ClarionProjectFile.Load(path);
        var vid = project.Find(new PropertyKey(PropertyScope.For("Release"), "vid"));

        Assert.NotNull(vid);
        Assert.False(vid.IsSafeToRemove);
    }

    [Fact]
    public void No_confunde_una_propiedad_con_otra_de_nombre_mas_largo()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteProject("Uno",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Release' \">",
            "    <vid_extra>algo</vid_extra>",
            "  </PropertyGroup>",
            "</Project>");

        var project = ClarionProjectFile.Load(path);

        Assert.Null(project.Find(new PropertyKey(PropertyScope.For("Release"), "vid")));
        Assert.NotNull(project.Find(new PropertyKey(PropertyScope.For("Release"), "vid_extra")));
    }

    [Fact]
    public void Encuentra_un_Import_de_Common_props_ya_existente()
    {
        using var fixture = new SolutionFixture();
        var path = fixture.WriteProject("Uno",
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup>",
            "    <OutputType>Library</OutputType>",
            "  </PropertyGroup>",
            "  <Import Project=\"$(MSBuildThisFileDirectory)Common.props\" />",
            "</Project>");

        var project = ClarionProjectFile.Load(path);

        Assert.NotNull(project.FindCommonPropsImport("Common.props"));
    }
}
