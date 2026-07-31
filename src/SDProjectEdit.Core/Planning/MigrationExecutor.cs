using System.Xml.Linq;
using SDProjectEdit.Core.Io;

namespace SDProjectEdit.Core.Planning;

public sealed record FileChange(string Path, string FileName, string NewText, int LinesRemoved, bool ImportAdded)
{
    public bool IsNew { get; init; }
}

public sealed class ApplyResult
{
    public required bool Applied { get; init; }
    public required IReadOnlyList<FileChange> Changes { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public string? BackupDirectory { get; init; }

    public static ApplyResult Failed(IReadOnlyList<string> errors) =>
        new() { Applied = false, Changes = [], Errors = errors };
}

/// <summary>
/// Aplica el plan. Todo se construye y valida en memoria primero: si algún archivo no
/// sobrevive la validación, no se escribe ninguno.
/// </summary>
public static class MigrationExecutor
{
    public static ApplyResult Apply(MigrationPlan plan, bool dryRun)
    {
        var errors = new List<string>(plan.Blockers);
        if (errors.Count > 0) return ApplyResult.Failed(errors);

        var changes = new List<FileChange>();

        // ---- Common.props ----------------------------------------------------------------
        var referenceFormat = plan.Solution.Projects.FirstOrDefault()?.Format ?? TextFileFormat.ClarionDefault;
        var commonFormat = referenceFormat with { HasXmlDeclaration = false, TrailingNewLine = true };
        var commonText = plan.CommonProps.Render(commonFormat);

        if (!TryParse(commonText, plan.CommonProps.FileName, errors))
            return ApplyResult.Failed(errors);

        var commonExisted = File.Exists(plan.CommonProps.Path);
        var commonChanged = !commonExisted || ReadTextOrNull(plan.CommonProps.Path) != commonText;
        if (commonChanged)
            changes.Add(new FileChange(plan.CommonProps.Path, plan.CommonProps.FileName, commonText, 0, false) { IsNew = !commonExisted });

        // ---- .cwproj ----------------------------------------------------------------------
        foreach (var edit in plan.ChangedEdits)
        {
            var project = edit.Project;
            var editor = project.CreateEditor();

            foreach (var property in edit.RemovedProperties)
            {
                if (property.LineIndex < 0)
                {
                    errors.Add($"{project.FileName}: no se pudo ubicar la línea de <{property.Name}>.");
                    continue;
                }
                editor.DeleteLine(property.LineIndex);
            }

            foreach (var group in edit.EmptiedGroups)
            {
                var start = editor[group.StartLineIndex].Trim();
                var end = editor[group.EndLineIndex].Trim();
                if (!start.StartsWith("<PropertyGroup", StringComparison.Ordinal) || end != "</PropertyGroup>")
                {
                    // No coincide con lo esperado: dejamos el grupo vacío en lugar de arriesgar el XML.
                    continue;
                }
                editor.DeleteRange(group.StartLineIndex, group.EndLineIndex);
            }

            if (edit.NeedsImport)
            {
                if (edit.ImportLineIndex < 0)
                {
                    errors.Add($"{project.FileName}: no se pudo determinar dónde insertar el Import.");
                    continue;
                }
                editor.InsertBefore(edit.ImportLineIndex, plan.ImportLineText(project.Format));
            }

            var newText = editor.Build();
            if (!TryParse(newText, project.FileName, errors)) continue;
            if (!VerifyRemovals(newText, edit, errors)) continue;

            changes.Add(new FileChange(project.Path, project.FileName, newText, edit.RemovedProperties.Count, edit.NeedsImport));
        }

        if (errors.Count > 0) return ApplyResult.Failed(errors);
        if (dryRun) return new ApplyResult { Applied = false, Changes = changes, Errors = [] };

        // ---- Backup + escritura -----------------------------------------------------------
        string? backupDir = null;
        if (plan.Options.CreateBackup)
        {
            backupDir = Path.Combine(plan.Solution.RootDirectory, ".sdprojectedit", "backup",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backupDir);
            foreach (var change in changes.Where(c => File.Exists(c.Path)))
                File.Copy(change.Path, Path.Combine(backupDir, change.FileName), overwrite: true);
        }

        foreach (var change in changes)
        {
            var format = change.Path == plan.CommonProps.Path ? commonFormat
                : plan.Solution.Projects.First(p => p.Path == change.Path).Format;
            WriteText(change.Path, change.NewText, format);
        }

        return new ApplyResult { Applied = true, Changes = changes, Errors = [], BackupDirectory = backupDir };
    }

    private static bool TryParse(string text, string fileName, List<string> errors)
    {
        try
        {
            XDocument.Parse(text);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"{fileName}: el resultado no es XML válido, se aborta sin escribir ({ex.Message}).");
            return false;
        }
    }

    /// <summary>Reparsea el resultado y confirma que las propiedades removidas ya no estén y las conservadas sí.</summary>
    private static bool VerifyRemovals(string newText, ProjectEdit edit, List<string> errors)
    {
        var doc = XDocument.Parse(newText);
        var ns = (XNamespace)ClarionProjectFile.MsBuildNamespace;
        var remaining = doc.Root!
            .Elements(ns + "PropertyGroup")
            .SelectMany(g => g.Elements().Select(e => (Condition: (string?)g.Attribute("Condition") ?? "", e.Name.LocalName)))
            .ToList();

        var ok = true;
        foreach (var removedProperty in edit.RemovedProperties)
        {
            var scopeCondition = removedProperty.Scope.IsGeneral ? "" : removedProperty.Scope.Configuration!;
            var stillThere = remaining.Any(r =>
                string.Equals(r.LocalName, removedProperty.Name, StringComparison.OrdinalIgnoreCase)
                && (scopeCondition.Length == 0 ? r.Condition.Length == 0 : r.Condition.Contains($"'{scopeCondition}'", StringComparison.OrdinalIgnoreCase)));
            if (stillThere)
            {
                errors.Add($"{edit.Project.FileName}: <{removedProperty.Name}> ({removedProperty.Scope.Display}) seguía presente después de editar. Se aborta.");
                ok = false;
            }
        }

        foreach (var keptProperty in edit.KeptOverrides)
        {
            var stillThere = remaining.Any(r => string.Equals(r.LocalName, keptProperty.Name, StringComparison.OrdinalIgnoreCase));
            if (!stillThere)
            {
                errors.Add($"{edit.Project.FileName}: el override <{keptProperty.Name}> desapareció por error. Se aborta.");
                ok = false;
            }
        }

        return ok;
    }

    private static string? ReadTextOrNull(string path)
    {
        try
        {
            TextFileFormat.Detect(path, out var text);
            return text;
        }
        catch { return null; }
    }

    private static void WriteText(string path, string text, TextFileFormat format)
    {
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (format.Utf8Bom) stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(bytes);
    }
}
