using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Analysis;

public enum UnificationStatus
{
    /// <summary>Presente en todos los proyectos con el mismo valor. Se puede centralizar tal cual.</summary>
    Uniform,

    /// <summary>Presente en todos los proyectos pero con valores distintos. Requiere decisión del usuario.</summary>
    Divergent,

    /// <summary>Falta en uno o más proyectos, pero donde está tiene siempre el mismo valor.</summary>
    Partial,

    /// <summary>Falta en algunos proyectos Y difiere entre los que la tienen.</summary>
    PartialDivergent,

    /// <summary>Propiedad inherentemente por-proyecto. Nunca se centraliza.</summary>
    Blocked,
}

/// <summary>Una propiedad (ámbito + nombre) vista a lo largo de todos los proyectos del solution.</summary>
public sealed class PropertyRow
{
    public required PropertyKey Key { get; init; }
    public required IReadOnlyDictionary<string, string> ValuesByProject { get; init; }
    public required IReadOnlyList<string> PresentIn { get; init; }
    public required IReadOnlyList<string> AbsentIn { get; init; }
    public required IReadOnlyList<string> DistinctValues { get; init; }
    public required string MajorityValue { get; init; }
    public required int MajorityCount { get; init; }
    public required UnificationStatus Status { get; init; }

    /// <summary>False si alguna ocurrencia no se puede borrar automáticamente (multilínea o con Condition propia).</summary>
    public required bool SafeToEdit { get; init; }
    public required string? UnsafeReason { get; init; }

    public PropertyInfo Info => PropertyCatalog.Describe(Key.Name);

    public bool IsCandidate => Status != UnificationStatus.Blocked && SafeToEdit;

    public bool NeedsDecision => Status is UnificationStatus.Divergent
        or UnificationStatus.Partial or UnificationStatus.PartialDivergent;

    /// <summary>Proyectos cuyo valor difiere del mayoritario (los que quedarían como override).</summary>
    public IReadOnlyList<string> Outliers => PresentIn
        .Where(p => !string.Equals(ValuesByProject[p], MajorityValue, StringComparison.Ordinal))
        .ToList();

    public string StatusText => Status switch
    {
        UnificationStatus.Uniform => "Uniforme",
        UnificationStatus.Divergent => "Divergente",
        UnificationStatus.Partial => "Parcial",
        UnificationStatus.PartialDivergent => "Parcial + divergente",
        UnificationStatus.Blocked => "Por-proyecto",
        _ => Status.ToString(),
    };
}

/// <summary>Tabla comparativa de todas las propiedades contra todos los proyectos.</summary>
public sealed class PropertyMatrix
{
    private readonly Dictionary<PropertyKey, PropertyRow> _rows;

    private PropertyMatrix(SolutionSet solution, Dictionary<PropertyKey, PropertyRow> rows)
    {
        Solution = solution;
        _rows = rows;
        Rows = rows.Values.OrderBy(r => r.Key).ToList();
    }

    public SolutionSet Solution { get; }
    public IReadOnlyList<PropertyRow> Rows { get; }
    public IReadOnlyList<string> ProjectNames => Solution.Projects.Select(p => p.Name).ToList();

    public PropertyRow? this[PropertyKey key] => _rows.GetValueOrDefault(key);

    public IEnumerable<PropertyRow> Candidates => Rows.Where(r => r.IsCandidate);
    public IEnumerable<PropertyRow> Divergences => Rows.Where(r => r.IsCandidate && r.NeedsDecision);
    public IEnumerable<PropertyRow> Unsafe => Rows.Where(r => r.Status != UnificationStatus.Blocked && !r.SafeToEdit);

    public static PropertyMatrix Build(SolutionSet solution)
    {
        var projects = solution.Projects;
        var allNames = projects.Select(p => p.Name).ToList();
        var rows = new Dictionary<PropertyKey, PropertyRow>();

        var keys = projects
            .SelectMany(p => p.Properties)
            .Where(p => !p.Key.Scope.Display.StartsWith('?')) // grupos con Condition no reconocida
            .Select(p => p.Key)
            .Distinct()
            .ToList();

        foreach (var key in keys)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? unsafeReason = null;

            foreach (var project in projects)
            {
                var occurrence = project.Find(key);
                if (occurrence is null) continue;
                values[project.Name] = occurrence.Value;

                if (unsafeReason is null && !occurrence.IsSafeToRemove)
                {
                    unsafeReason = !string.IsNullOrEmpty(occurrence.OwnCondition)
                        ? $"{project.FileName} la declara con Condition propia ({occurrence.OwnCondition.Trim()})."
                        : $"{project.FileName} la declara repartida en varias líneas (línea {occurrence.LineIndex + 1}).";
                }
            }

            var presentIn = allNames.Where(values.ContainsKey).ToList();
            var absentIn = allNames.Where(n => !values.ContainsKey(n)).ToList();
            var distinct = values.Values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();

            var majorityGroup = values.Values
                .GroupBy(v => v, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .First();

            var blocked = PropertyCatalog.IsNeverUnify(key.Name);
            var status = blocked
                ? UnificationStatus.Blocked
                : (absentIn.Count, distinct.Count) switch
                {
                    (0, 1) => UnificationStatus.Uniform,
                    (0, _) => UnificationStatus.Divergent,
                    (_, 1) => UnificationStatus.Partial,
                    _ => UnificationStatus.PartialDivergent,
                };

            rows[key] = new PropertyRow
            {
                Key = key,
                ValuesByProject = values,
                PresentIn = presentIn,
                AbsentIn = absentIn,
                DistinctValues = distinct,
                MajorityValue = majorityGroup.Key,
                MajorityCount = majorityGroup.Count(),
                Status = status,
                SafeToEdit = unsafeReason is null,
                UnsafeReason = unsafeReason,
            };
        }

        return new PropertyMatrix(solution, rows);
    }
}
