using System.Text.RegularExpressions;
using SDProjectEdit.Core.Io;

namespace SDProjectEdit.Core.Analysis;

/// <summary>Un .cwproj que está en disco pero no referenciado por el .sln, o al revés.</summary>
public sealed record SolutionWarning(string Message);

/// <summary>
/// El conjunto de .cwproj sobre el que trabaja la herramienta, más el .sln que los agrupa.
/// </summary>
public sealed class SolutionSet
{
    private SolutionSet(
        string rootDirectory,
        string? solutionPath,
        IReadOnlyList<ClarionProjectFile> projects,
        IReadOnlyList<SolutionWarning> warnings,
        IReadOnlyList<string> loadErrors)
    {
        RootDirectory = rootDirectory;
        SolutionPath = solutionPath;
        Projects = projects;
        Warnings = warnings;
        LoadErrors = loadErrors;
    }

    public string RootDirectory { get; }
    public string? SolutionPath { get; }
    public IReadOnlyList<ClarionProjectFile> Projects { get; }
    public IReadOnlyList<SolutionWarning> Warnings { get; }
    public IReadOnlyList<string> LoadErrors { get; }

    public string CommonPropsPath => Path.Combine(RootDirectory, "Common.props");

    private static readonly Regex SlnProjectLine = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""(?<name>[^""]*)""\s*,\s*""(?<path>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Carga los proyectos de <paramref name="pathOrDirectory"/>. Acepta una carpeta, un .sln
    /// o un .cwproj suelto. Si hay .sln y <paramref name="onlySolutionProjects"/> es true,
    /// se trabaja solo sobre lo que el .sln referencia.
    /// </summary>
    public static SolutionSet Load(string pathOrDirectory, bool onlySolutionProjects = true, bool recursive = false)
    {
        var full = Path.GetFullPath(pathOrDirectory);
        string root;
        string? slnPath = null;

        if (File.Exists(full) && full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            slnPath = full;
            root = Path.GetDirectoryName(full)!;
        }
        else if (File.Exists(full) && full.EndsWith(".cwproj", StringComparison.OrdinalIgnoreCase))
        {
            root = Path.GetDirectoryName(full)!;
        }
        else if (Directory.Exists(full))
        {
            root = full;
            var slns = Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly);
            if (slns.Length == 1) slnPath = slns[0];
            else if (slns.Length > 1) slnPath = slns.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).First();
        }
        else
        {
            throw new DirectoryNotFoundException($"No existe la ruta '{pathOrDirectory}'.");
        }

        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var onDisk = full.EndsWith(".cwproj", StringComparison.OrdinalIgnoreCase) && File.Exists(full)
            ? [full]
            : Directory.GetFiles(root, "*.cwproj", search);

        var warnings = new List<SolutionWarning>();
        var selected = onDisk
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (slnPath is not null)
        {
            var referenced = SlnProjectLine.Matches(File.ReadAllText(slnPath))
                .Select(m => Path.GetFullPath(Path.Combine(root, m.Groups["path"].Value)))
                .Where(p => p.EndsWith(".cwproj", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var missing in referenced.Where(r => !File.Exists(r)))
                warnings.Add(new SolutionWarning($"El .sln referencia '{Path.GetFileName(missing)}' pero el archivo no existe en disco."));

            var orphans = selected.Where(p => !referenced.Contains(p)).ToList();
            foreach (var orphan in orphans)
                warnings.Add(new SolutionWarning(
                    $"'{Path.GetFileName(orphan)}' está en disco pero no lo referencia el .sln" +
                    (onlySolutionProjects ? " — queda EXCLUIDO." : " — se incluye igual (--all).")));

            if (onlySolutionProjects) selected = selected.Where(referenced.Contains).ToList();
        }

        var projects = new List<ClarionProjectFile>();
        var errors = new List<string>();
        foreach (var path in selected)
        {
            try { projects.Add(ClarionProjectFile.Load(path)); }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }

        foreach (var project in projects)
            foreach (var group in project.UnrecognizedGroups)
                warnings.Add(new SolutionWarning(
                    $"{project.FileName}: PropertyGroup con Condition no reconocida ({group.RawCondition?.Trim()}) — se ignora por completo."));

        return new SolutionSet(root, slnPath, projects, warnings, errors);
    }

    /// <summary>Todas las Configurations vistas en el solution, en orden Debug, Release, resto.</summary>
    public IReadOnlyList<string> Configurations => Projects
        .SelectMany(p => p.Configurations)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? 0
                    : c.Equals("Release", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
        .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
