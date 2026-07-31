using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Planning;

public enum DecisionKind
{
    /// <summary>No tocar: la propiedad queda como está, en cada .cwproj.</summary>
    Leave,

    /// <summary>Al Common.props, y se quita de todos los .cwproj.</summary>
    Unify,

    /// <summary>Al Common.props, pero los proyectos con otro valor lo conservan como override explícito.</summary>
    UnifyKeepOverrides,
}

public sealed record PropertyDecision(PropertyKey Key, DecisionKind Kind, string Value);

public enum ImportPlacement
{
    /// <summary>Justo después de la etiqueta &lt;Project&gt;.</summary>
    AfterProjectElement,

    /// <summary>
    /// Después del primer PropertyGroup (el general). Es el default: ese grupo es el que fija
    /// <c>&lt;Configuration Condition=" '$(Configuration)' == '' "&gt;Debug&lt;/Configuration&gt;</c>,
    /// así que importando después nos aseguramos de que $(Configuration) ya tenga valor cuando
    /// se evalúan los PropertyGroup condicionales del Common.props.
    /// </summary>
    AfterFirstPropertyGroup,
}

public sealed class MigrationOptions
{
    public ImportPlacement ImportPlacement { get; init; } = ImportPlacement.AfterFirstPropertyGroup;
    public bool RemoveEmptyPropertyGroups { get; init; } = true;
    public bool CreateBackup { get; init; } = true;
    public string CommonPropsFileName { get; init; } = CommonPropsFile.DefaultFileName;
}

/// <summary>Un cambio real de comportamiento: el valor efectivo de una propiedad en un proyecto cambia.</summary>
public sealed record EffectiveChange(string Project, PropertyKey Key, string? Before, string? After)
{
    public string BeforeText => Before ?? "(no definida — default del compilador)";
    public string AfterText => After ?? "(no definida)";
}

/// <summary>Qué se le va a hacer a un .cwproj concreto.</summary>
public sealed class ProjectEdit
{
    public required ClarionProjectFile Project { get; init; }
    public required bool NeedsImport { get; init; }
    public required int ImportLineIndex { get; init; }
    public required IReadOnlyList<ProjectProperty> RemovedProperties { get; init; }
    public required IReadOnlyList<ProjectProperty> KeptOverrides { get; init; }
    public required IReadOnlyList<ProjectPropertyGroup> EmptiedGroups { get; init; }

    public bool HasChanges => NeedsImport || RemovedProperties.Count > 0;
}

public sealed class MigrationPlan
{
    private MigrationPlan(
        PropertyMatrix matrix,
        MigrationOptions options,
        IReadOnlyList<PropertyDecision> decisions,
        CommonPropsFile commonProps,
        IReadOnlyList<ProjectEdit> edits,
        IReadOnlyList<EffectiveChange> behaviorChanges,
        IReadOnlyList<string> blockers)
    {
        Matrix = matrix;
        Options = options;
        Decisions = decisions;
        CommonProps = commonProps;
        Edits = edits;
        BehaviorChanges = behaviorChanges;
        Blockers = blockers;
    }

    public PropertyMatrix Matrix { get; }
    public SolutionSet Solution => Matrix.Solution;
    public MigrationOptions Options { get; }
    public IReadOnlyList<PropertyDecision> Decisions { get; }
    public CommonPropsFile CommonProps { get; }
    public IReadOnlyList<ProjectEdit> Edits { get; }
    public IReadOnlyList<EffectiveChange> BehaviorChanges { get; }

    /// <summary>Motivos por los que el plan no se puede aplicar tal cual.</summary>
    public IReadOnlyList<string> Blockers { get; }

    public bool CanApply => Blockers.Count == 0;
    public IEnumerable<ProjectEdit> ChangedEdits => Edits.Where(e => e.HasChanges);

    /// <summary>
    /// Propiedades que, tras aplicar, pueden seguir declaradas en un .cwproj sin que eso sea
    /// un residuo: las que se decidió dejar por-proyecto y las que se conservan como override
    /// explícito. Cualquier otra que aparezca es un error de la migración.
    /// </summary>
    public IReadOnlySet<PropertyKey> ExpectedOverrides => Decisions
        .Where(d => d.Kind == DecisionKind.Leave)
        .Select(d => d.Key)
        .Concat(Edits.SelectMany(e => e.KeptOverrides).Select(p => p.Key))
        .ToHashSet();

    public string ImportLineText(TextFileFormat format) =>
        $"{format.Indent}<Import Project=\"$(MSBuildThisFileDirectory){Options.CommonPropsFileName}\" />";

    /// <summary>
    /// Decisiones por defecto: unificar sólo lo que ya es idéntico en el 100% de los proyectos.
    /// Toda divergencia queda en <see cref="DecisionKind.Leave"/> a la espera de que decida el usuario.
    /// </summary>
    public static List<PropertyDecision> DefaultDecisions(PropertyMatrix matrix) => matrix.Rows
        .Select(row => new PropertyDecision(
            row.Key,
            row.IsCandidate && row.Status == UnificationStatus.Uniform ? DecisionKind.Unify : DecisionKind.Leave,
            row.MajorityValue))
        .ToList();

    public static MigrationPlan Create(
        PropertyMatrix matrix,
        IReadOnlyList<PropertyDecision> decisions,
        MigrationOptions options)
    {
        var solution = matrix.Solution;
        var blockers = new List<string>();

        var commonPropsPath = Path.Combine(solution.RootDirectory, options.CommonPropsFileName);
        var commonProps = CommonPropsFile.Load(commonPropsPath);
        var previousCommon = commonProps.Values.ToDictionary(kv => kv.Key, kv => kv.Value);

        var active = decisions.Where(d => d.Kind != DecisionKind.Leave).ToList();

        foreach (var decision in active)
        {
            var row = matrix[decision.Key];
            if (row is null)
            {
                blockers.Add($"{decision.Key}: no existe en el relevamiento.");
                continue;
            }
            if (row.Status == UnificationStatus.Blocked)
                blockers.Add($"{decision.Key}: es una propiedad por-proyecto, no se centraliza.");
            else if (!row.SafeToEdit)
                blockers.Add($"{decision.Key}: no es editable automáticamente — {row.UnsafeReason}");
        }

        foreach (var decision in active)
            commonProps.Set(decision.Key, decision.Value);

        // ---- Qué se saca de cada .cwproj -------------------------------------------------
        var edits = new List<ProjectEdit>();
        var changes = new List<EffectiveChange>();

        foreach (var project in solution.Projects)
        {
            var removed = new List<ProjectProperty>();
            var kept = new List<ProjectProperty>();

            foreach (var decision in active)
            {
                var occurrence = project.Find(decision.Key);
                var before = occurrence?.Value ?? previousCommon.GetValueOrDefault(decision.Key);

                var keepOverride = occurrence is not null
                    && decision.Kind == DecisionKind.UnifyKeepOverrides
                    && !string.Equals(occurrence.Value, decision.Value, StringComparison.Ordinal);

                if (occurrence is not null)
                {
                    if (keepOverride) kept.Add(occurrence);
                    else removed.Add(occurrence);
                }

                var after = keepOverride ? occurrence!.Value : decision.Value;
                if (!string.Equals(before, after, StringComparison.Ordinal))
                    changes.Add(new EffectiveChange(project.Name, decision.Key, before, after));
            }

            var emptied = options.RemoveEmptyPropertyGroups
                ? FindEmptiedGroups(project, removed)
                : [];

            var hasImport = project.FindCommonPropsImport(options.CommonPropsFileName) is not null;
            var importLine = ResolveImportLine(project, options.ImportPlacement, out var importProblem);
            if (!hasImport && importProblem is not null) blockers.Add($"{project.FileName}: {importProblem}");

            edits.Add(new ProjectEdit
            {
                Project = project,
                NeedsImport = !hasImport,
                ImportLineIndex = importLine,
                RemovedProperties = removed,
                KeptOverrides = kept,
                EmptiedGroups = emptied,
            });
        }

        // Propiedades que quedaron sólo en Common.props sin que ningún proyecto las declare:
        // válido en re-ejecuciones, pero conviene avisar si además nadie las usa.
        foreach (var error in solution.LoadErrors)
            blockers.Add($"No se pudo leer un proyecto — {error}");

        return new MigrationPlan(matrix, options, decisions, commonProps, edits, changes, blockers);
    }

    private static IReadOnlyList<ProjectPropertyGroup> FindEmptiedGroups(
        ClarionProjectFile project, List<ProjectProperty> removed)
    {
        var result = new List<ProjectPropertyGroup>();
        foreach (var group in project.PropertyGroups)
        {
            if (!group.ScopeRecognized) continue;
            if (group.StartLineIndex < 0 || group.EndLineIndex < 0) continue;
            if (group.Element.Nodes().OfType<System.Xml.Linq.XComment>().Any()) continue;

            var children = group.Element.Elements().ToList();
            if (children.Count == 0) continue;
            if (children.All(child => removed.Any(r => ReferenceEquals(r.Element, child))))
                result.Add(group);
        }
        return result;
    }

    private static int ResolveImportLine(ClarionProjectFile project, ImportPlacement placement, out string? problem)
    {
        problem = null;

        if (placement == ImportPlacement.AfterFirstPropertyGroup)
        {
            var general = project.PropertyGroups.FirstOrDefault(g => g.Scope.IsGeneral);
            if (general is not null && general.EndLineIndex >= 0) return general.EndLineIndex + 1;
            // Sin PropertyGroup general: caemos a insertar después de <Project>.
        }

        var root = project.Document.Root!;
        var rootLine = ClarionProjectFile.LineIndexOf(root);
        if (rootLine < 0)
        {
            problem = "no se pudo ubicar la línea de la etiqueta <Project>.";
            return -1;
        }
        return rootLine + 1;
    }
}
