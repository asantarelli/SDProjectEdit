using System.Xml.Linq;
using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;
using SDProjectEdit.Core.Planning;

namespace SDProjectEdit.Core.Tests;

public class MigrationTests
{
    private static readonly MigrationOptions Options = new() { CreateBackup = false };

    private static MigrationPlan Plan(
        SolutionFixture fixture,
        Func<PropertyMatrix, IReadOnlyList<PropertyDecision>>? decisions = null,
        MigrationOptions? options = null)
    {
        var matrix = PropertyMatrix.Build(SolutionSet.Load(fixture.Root));
        return MigrationPlan.Create(
            matrix,
            decisions?.Invoke(matrix) ?? MigrationPlan.DefaultDecisions(matrix),
            options ?? Options);
    }

    private static SolutionFixture ThreeProjects()
    {
        var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        fixture.WriteTypicalProject("Tres");
        return fixture;
    }

    [Fact]
    public void Por_defecto_solo_se_unifica_lo_que_ya_es_identico_en_todos()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos", releaseVid: "full");
        var matrix = PropertyMatrix.Build(SolutionSet.Load(fixture.Root));

        var decisions = MigrationPlan.DefaultDecisions(matrix);

        var releaseVid = decisions.Single(d => d.Key.Name == "vid" && d.Key.Scope == PropertyScope.For("Release"));
        var debugVid = decisions.Single(d => d.Key.Name == "vid" && d.Key.Scope == PropertyScope.For("Debug"));
        Assert.Equal(DecisionKind.Leave, releaseVid.Kind);
        Assert.Equal(DecisionKind.Unify, debugVid.Kind);
    }

    [Fact]
    public void Aplicar_crea_Common_props_e_inserta_el_Import_en_los_tres()
    {
        using var fixture = ThreeProjects();

        var result = MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        Assert.True(result.Applied);
        Assert.True(File.Exists(fixture.CommonPropsPath));
        foreach (var name in new[] { "Uno", "Dos", "Tres" })
            Assert.Contains("<Import Project=\"$(MSBuildThisFileDirectory)Common.props\" />",
                fixture.ReadText(name + ".cwproj"));
    }

    [Fact]
    public void El_Import_va_despues_del_PropertyGroup_general()
    {
        using var fixture = ThreeProjects();
        MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        var lines = fixture.ReadText("Uno.cwproj").Split("\r\n");
        var importLine = Array.FindIndex(lines, l => l.Contains("Common.props"));
        var generalClose = Array.FindIndex(lines, l => l.Trim() == "</PropertyGroup>");
        var firstConditional = Array.FindIndex(lines, l => l.TrimStart().StartsWith("<PropertyGroup Condition="));

        Assert.True(importLine > generalClose, "el Import debe ir después del cierre del grupo general");
        Assert.True(firstConditional < 0 || importLine < firstConditional,
            "el Import debe ir antes de cualquier PropertyGroup condicional que quede");
    }

    [Fact]
    public void Todo_lo_que_no_se_migra_queda_byte_a_byte_igual()
    {
        using var fixture = ThreeProjects();
        var before = fixture.ReadText("Uno.cwproj").Split("\r\n");

        MigrationExecutor.Apply(Plan(fixture), dryRun: false);
        var after = fixture.ReadText("Uno.cwproj").Split("\r\n");

        // Las entidades de DefineConstants son el canario: un round-trip de XML las reescribiría.
        var defineConstants = before.Single(l => l.Contains("DefineConstants"));
        Assert.Contains("&gt;", defineConstants);
        Assert.Contains(defineConstants, after);

        // Toda línea sobreviviente tiene que existir tal cual en el original.
        foreach (var line in after.Where(l => !l.Contains("Common.props")))
            Assert.Contains(line, before);
    }

    [Fact]
    public void El_resultado_sigue_siendo_XML_valido()
    {
        using var fixture = ThreeProjects();
        MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        foreach (var name in new[] { "Uno.cwproj", "Dos.cwproj", "Tres.cwproj", "Common.props" })
        {
            var exception = Record.Exception(() => XDocument.Parse(fixture.ReadText(name)));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Un_PropertyGroup_que_queda_vacio_se_elimina()
    {
        using var fixture = ThreeProjects();
        MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        Assert.DoesNotContain("'$(Configuration)' == 'Debug'", fixture.ReadText("Uno.cwproj"));
    }

    [Fact]
    public void Con_keep_empty_groups_el_PropertyGroup_vacio_se_conserva()
    {
        using var fixture = ThreeProjects();
        var options = new MigrationOptions { CreateBackup = false, RemoveEmptyPropertyGroups = false };

        MigrationExecutor.Apply(Plan(fixture, options: options), dryRun: false);

        var text = fixture.ReadText("Uno.cwproj");
        Assert.Contains("'$(Configuration)' == 'Debug'", text);
        Assert.Null(Record.Exception(() => XDocument.Parse(text)));
    }

    [Fact]
    public void Unificar_conservando_overrides_deja_el_valor_distinto_en_su_cwproj()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Dos");
        fixture.WriteTypicalProject("Tres", releaseVid: "full");
        var key = new PropertyKey(PropertyScope.For("Release"), "vid");

        var plan = Plan(fixture, matrix => MigrationPlan.DefaultDecisions(matrix)
            .Select(d => d.Key.Equals(key) ? new PropertyDecision(key, DecisionKind.UnifyKeepOverrides, "off") : d)
            .ToList());
        MigrationExecutor.Apply(plan, dryRun: false);

        Assert.Contains("<vid>off</vid>", fixture.ReadText("Common.props"));
        Assert.Contains("<vid>full</vid>", fixture.ReadText("Tres.cwproj"));
        Assert.DoesNotContain("<vid>", fixture.ReadText("Uno.cwproj"));
        Assert.Empty(plan.BehaviorChanges);
    }

    [Fact]
    public void Unificar_a_secas_fuerza_el_valor_y_reporta_el_cambio_de_comportamiento()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno");
        fixture.WriteTypicalProject("Tres", releaseVid: "full");
        var key = new PropertyKey(PropertyScope.For("Release"), "vid");

        var plan = Plan(fixture, matrix => MigrationPlan.DefaultDecisions(matrix)
            .Select(d => d.Key.Equals(key) ? new PropertyDecision(key, DecisionKind.Unify, "off") : d)
            .ToList());
        MigrationExecutor.Apply(plan, dryRun: false);

        var change = Assert.Single(plan.BehaviorChanges, c => c.Key.Equals(key));
        Assert.Equal("Tres", change.Project);
        Assert.Equal("full", change.Before);
        Assert.Equal("off", change.After);
        Assert.DoesNotContain("<vid>", fixture.ReadText("Tres.cwproj"));
    }

    [Fact]
    public void Centralizar_una_propiedad_parcial_se_reporta_como_cambio_de_comportamiento()
    {
        using var fixture = new SolutionFixture();
        fixture.WriteTypicalProject("Uno", releaseExtra: ["    <GenerateMap>True</GenerateMap>"]);
        fixture.WriteTypicalProject("Dos");
        var key = new PropertyKey(PropertyScope.For("Release"), "GenerateMap");

        var plan = Plan(fixture, matrix => MigrationPlan.DefaultDecisions(matrix)
            .Select(d => d.Key.Equals(key) ? new PropertyDecision(key, DecisionKind.Unify, "True") : d)
            .ToList());

        var change = Assert.Single(plan.BehaviorChanges, c => c.Key.Equals(key));
        Assert.Equal("Dos", change.Project);
        Assert.Null(change.Before);
        Assert.Equal("True", change.After);
    }

    [Fact]
    public void No_se_puede_centralizar_una_propiedad_por_proyecto()
    {
        using var fixture = ThreeProjects();
        var key = new PropertyKey(PropertyScope.General, "DefineConstants");

        var plan = Plan(fixture, matrix => [new PropertyDecision(key, DecisionKind.Unify, "loquesea")]);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Blockers, b => b.Contains("DefineConstants"));
    }

    [Fact]
    public void Un_dry_run_no_escribe_nada()
    {
        using var fixture = ThreeProjects();
        var before = fixture.ReadText("Uno.cwproj");

        var result = MigrationExecutor.Apply(Plan(fixture), dryRun: true);

        Assert.False(result.Applied);
        Assert.NotEmpty(result.Changes);
        Assert.False(File.Exists(fixture.CommonPropsPath));
        Assert.Equal(before, fixture.ReadText("Uno.cwproj"));
    }

    [Fact]
    public void Re_aplicar_es_idempotente()
    {
        using var fixture = ThreeProjects();
        MigrationExecutor.Apply(Plan(fixture), dryRun: false);
        var afterFirst = fixture.ReadText("Uno.cwproj");
        var commonAfterFirst = fixture.ReadText("Common.props");

        var second = MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        Assert.Empty(second.Changes);
        Assert.Equal(afterFirst, fixture.ReadText("Uno.cwproj"));
        Assert.Equal(commonAfterFirst, fixture.ReadText("Common.props"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(afterFirst, "Common\\.props"));
    }

    [Fact]
    public void Re_aplicar_no_pierde_lo_que_ya_estaba_en_Common_props()
    {
        using var fixture = ThreeProjects();
        MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        var common = CommonPropsFile.Load(fixture.CommonPropsPath);
        common.Set(new PropertyKey(PropertyScope.For("Release"), "GenerateMap"), "True");
        common.Save(TextFileFormat.ClarionDefault with { TrailingNewLine = true });

        MigrationExecutor.Apply(Plan(fixture), dryRun: false);

        Assert.Contains("<GenerateMap>True</GenerateMap>", fixture.ReadText("Common.props"));
    }

    [Fact]
    public void Si_un_archivo_no_se_puede_editar_no_se_escribe_ninguno()
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
        var key = new PropertyKey(PropertyScope.For("Debug"), "vid");

        var plan = Plan(fixture, matrix => [new PropertyDecision(key, DecisionKind.Unify, "full")]);
        var result = MigrationExecutor.Apply(plan, dryRun: false);

        Assert.False(result.Applied);
        Assert.False(File.Exists(fixture.CommonPropsPath));
        Assert.Contains("<vid>", fixture.ReadText("Uno.cwproj"));
    }

    [Fact]
    public void El_backup_guarda_los_originales()
    {
        using var fixture = ThreeProjects();
        var before = fixture.ReadText("Uno.cwproj");

        MigrationExecutor.Apply(Plan(fixture, options: new MigrationOptions()), dryRun: false);

        var backupDir = Directory.GetDirectories(Path.Combine(fixture.Root, ".sdprojectedit", "backup")).Single();
        Assert.Equal(before, File.ReadAllText(Path.Combine(backupDir, "Uno.cwproj")).TrimStart('﻿'));
    }
}
