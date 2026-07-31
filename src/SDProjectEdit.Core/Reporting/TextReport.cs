using System.Text;
using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Planning;

namespace SDProjectEdit.Core.Reporting;

/// <summary>Render de texto plano de cada paso, compartido por la CLI y por los paneles de la GUI.</summary>
public static class TextReport
{
    public static string Survey(PropertyMatrix matrix)
    {
        var sb = new StringBuilder();
        var solution = matrix.Solution;
        var n = solution.Projects.Count;

        sb.AppendLine("PASO 1 — RELEVAMIENTO");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"Raíz          : {solution.RootDirectory}");
        sb.AppendLine($"Solution      : {(solution.SolutionPath is null ? "(ninguno)" : Path.GetFileName(solution.SolutionPath))}");
        sb.AppendLine($"Proyectos     : {n} — {string.Join(", ", matrix.ProjectNames)}");
        sb.AppendLine($"Configurations: {string.Join(", ", solution.Configurations)}");
        sb.AppendLine();

        foreach (var warning in solution.Warnings) sb.AppendLine($"  aviso: {warning.Message}");
        foreach (var error in solution.LoadErrors) sb.AppendLine($"  ERROR: {error}");
        if (solution.Warnings.Count > 0 || solution.LoadErrors.Count > 0) sb.AppendLine();

        sb.AppendLine($"{"ÁMBITO",-10} {"PROPIEDAD",-22} {"ESTADO",-21} {"PRESENTE",-9} VALOR");
        sb.AppendLine(new string('-', 78));
        foreach (var row in matrix.Rows)
        {
            var value = row.DistinctValues.Count == 1 ? Truncate(row.DistinctValues[0], 24) : $"{row.DistinctValues.Count} valores distintos";
            sb.AppendLine($"{row.Key.Scope.Display,-10} {row.Key.Name,-22} {row.StatusText,-21} {$"{row.PresentIn.Count}/{n}",-9} {value}");
        }
        return sb.ToString();
    }

    public static string Divergences(PropertyMatrix matrix)
    {
        var sb = new StringBuilder();
        var n = matrix.Solution.Projects.Count;
        var divergences = matrix.Divergences.ToList();
        var unsafeRows = matrix.Unsafe.ToList();

        sb.AppendLine("PASO 2 — DIVERGENCIAS (requieren decisión explícita)");
        sb.AppendLine(new string('=', 78));

        var uniform = matrix.Candidates.Where(r => r.Status == UnificationStatus.Uniform).ToList();
        sb.AppendLine($"Seguras para centralizar tal cual (idénticas en {n}/{n}): {uniform.Count}");
        foreach (var row in uniform)
            sb.AppendLine($"  {row.Key,-34} = {Truncate(row.MajorityValue, 30)}");
        sb.AppendLine();

        if (divergences.Count == 0)
        {
            sb.AppendLine("No hay divergencias. Todas las propiedades candidatas son uniformes.");
        }
        else
        {
            sb.AppendLine($"Divergencias encontradas: {divergences.Count}");
            sb.AppendLine();
            foreach (var row in divergences)
            {
                sb.AppendLine($"  [{row.Key}]  {row.StatusText}");
                foreach (var group in row.ValuesByProject
                             .GroupBy(kv => kv.Value, StringComparer.Ordinal)
                             .OrderByDescending(g => g.Count()))
                    sb.AppendLine($"      {$"'{Truncate(group.Key, 26)}'",-28} → {group.Count(),2} proyecto(s): {string.Join(", ", group.Select(kv => kv.Key))}");

                if (row.AbsentIn.Count > 0)
                    sb.AppendLine($"      {"(no declarada)",-28} → {row.AbsentIn.Count,2} proyecto(s): {string.Join(", ", row.AbsentIn)}");

                var spec = $"{CliScope(row.Key.Scope)}:{row.Key.Name}";
                sb.AppendLine($"      opciones: --unify {spec}=<valor>  (fuerza el mismo valor en todos)");
                sb.AppendLine($"                --unify-keep-overrides {spec}=<valor>  (los que difieren lo conservan)");
                sb.AppendLine($"                --leave {spec}  (queda por-proyecto)");
                sb.AppendLine();
            }
        }

        var blocked = matrix.Rows.Where(r => r.Status == UnificationStatus.Blocked).ToList();
        if (blocked.Count > 0)
        {
            sb.AppendLine($"Propiedades por-proyecto, nunca centralizadas ({blocked.Count}):");
            sb.AppendLine($"  {string.Join(", ", blocked.Select(r => r.Key.Name).Distinct(StringComparer.OrdinalIgnoreCase))}");
            sb.AppendLine();
        }

        if (unsafeRows.Count > 0)
        {
            sb.AppendLine($"No editables automáticamente ({unsafeRows.Count}):");
            foreach (var row in unsafeRows) sb.AppendLine($"  {row.Key}: {row.UnsafeReason}");
        }

        return sb.ToString();
    }

    public static string Plan(MigrationPlan plan, IReadOnlyList<FileChange>? changes = null)
    {
        var sb = new StringBuilder();
        var format = plan.Solution.Projects.FirstOrDefault()?.Format ?? Io.TextFileFormat.ClarionDefault;

        sb.AppendLine("PASO 4 — PLAN (nada se escribió todavía)");
        sb.AppendLine(new string('=', 78));

        var active = plan.Decisions.Where(d => d.Kind != DecisionKind.Leave).ToList();
        sb.AppendLine($"Propiedades a centralizar: {active.Count}");
        foreach (var decision in active.OrderBy(d => d.Key))
            sb.AppendLine($"  {decision.Key,-34} = {Truncate(decision.Value, 24),-26} {(decision.Kind == DecisionKind.UnifyKeepOverrides ? "(conserva overrides)" : "")}");
        sb.AppendLine();

        sb.AppendLine($"Contenido propuesto de {plan.CommonProps.FileName}:");
        sb.AppendLine(new string('-', 78));
        foreach (var line in plan.CommonProps.Render(format with { TrailingNewLine = false }).Split('\n'))
            sb.AppendLine("  " + line.TrimEnd('\r'));
        sb.AppendLine(new string('-', 78));
        sb.AppendLine();

        var touched = plan.ChangedEdits.ToList();
        sb.AppendLine($"Archivos .cwproj a modificar: {touched.Count} de {plan.Solution.Projects.Count}");
        foreach (var edit in touched)
        {
            sb.AppendLine($"  {edit.Project.FileName}");
            if (edit.NeedsImport)
                sb.AppendLine($"    + línea {edit.ImportLineIndex + 1}: {plan.ImportLineText(edit.Project.Format).Trim()}");
            foreach (var property in edit.RemovedProperties.OrderBy(p => p.LineIndex))
                sb.AppendLine($"    - línea {property.LineIndex + 1}: {edit.Project.OriginalText.Split('\n')[property.LineIndex].Trim('\r', ' ', '\t')}   [{property.Scope.Display}]");
            foreach (var group in edit.EmptiedGroups)
                sb.AppendLine($"    - líneas {group.StartLineIndex + 1}-{group.EndLineIndex + 1}: PropertyGroup de {group.Scope.Display} queda vacío, se elimina");
            foreach (var kept in edit.KeptOverrides)
                sb.AppendLine($"    = línea {kept.LineIndex + 1}: <{kept.Name}>{kept.Value}</{kept.Name}> se CONSERVA como override");
        }
        sb.AppendLine();

        var unchanged = plan.Edits.Where(e => !e.HasChanges).ToList();
        if (unchanged.Count > 0)
            sb.AppendLine($"Sin cambios: {string.Join(", ", unchanged.Select(e => e.Project.FileName))}");

        sb.AppendLine();
        sb.AppendLine("CAMBIOS DE COMPORTAMIENTO REALES");
        sb.AppendLine(new string('-', 78));
        if (plan.BehaviorChanges.Count == 0)
        {
            sb.AppendLine("  Ninguno. El valor efectivo de cada propiedad en cada proyecto queda igual que antes.");
        }
        else
        {
            foreach (var group in plan.BehaviorChanges.GroupBy(c => c.Key).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [{group.Key}]");
                foreach (var change in group)
                    sb.AppendLine($"    {change.Project,-14} {change.BeforeText}  ->  {change.AfterText}");
            }
        }

        if (plan.Blockers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BLOQUEOS — el plan no se puede aplicar:");
            foreach (var blocker in plan.Blockers) sb.AppendLine($"  {blocker}");
        }

        if (changes is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Archivos que se escribirían: {changes.Count}");
            foreach (var change in changes)
                sb.AppendLine($"  {(change.IsNew ? "nuevo " : "editar")} {change.FileName}");
        }

        return sb.ToString();
    }

    public static string ApplyOutcome(ApplyResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PASO 5 — APLICACIÓN");
        sb.AppendLine(new string('=', 78));

        if (result.Errors.Count > 0)
        {
            sb.AppendLine("No se escribió NINGÚN archivo. Errores:");
            foreach (var error in result.Errors) sb.AppendLine($"  {error}");
            return sb.ToString();
        }

        if (!result.Applied)
        {
            sb.AppendLine($"Simulación (dry-run): {result.Changes.Count} archivo(s) se habrían escrito.");
            foreach (var change in result.Changes) sb.AppendLine($"  {(change.IsNew ? "nuevo " : "editar")} {change.FileName}");
            return sb.ToString();
        }

        sb.AppendLine($"{result.Changes.Count} archivo(s) escritos.");
        if (result.BackupDirectory is not null) sb.AppendLine($"Backup: {result.BackupDirectory}");
        foreach (var change in result.Changes)
            sb.AppendLine($"  {(change.IsNew ? "nuevo " : "editar")} {change.FileName}" +
                          (change.LinesRemoved > 0 ? $"  (-{change.LinesRemoved} propiedades)" : "") +
                          (change.ImportAdded ? "  (+Import)" : ""));
        return sb.ToString();
    }

    public static string Verification(VerificationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PASO 6 — VERIFICACIÓN");
        sb.AppendLine(new string('=', 78));
        foreach (var check in report.Checks)
        {
            sb.AppendLine($"[{check.Icon}] {check.Name}");
            foreach (var detail in check.Details) sb.AppendLine($"        {detail}");
        }
        sb.AppendLine();
        sb.AppendLine(report.AllPassed
            ? "Todo verificado. Corré un REBUILD SOLUTION completo (no Compile incremental) para confirmar\nque MSBuild resuelve bien el Import antes de seguir trabajando."
            : "Hay chequeos en falla. Revisalos antes de compilar.");
        return sb.ToString();
    }

    /// <summary>Cómo se escribe el ámbito en la línea de comandos (el display lleva paréntesis).</summary>
    private static string CliScope(Core.Model.PropertyScope scope) => scope.IsGeneral ? "general" : scope.Configuration!;

    private static string Truncate(string value, int max)
    {
        value = value.Replace("\r", "").Replace("\n", " ");
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
