using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;

namespace SDProjectEdit.Core.Planning;

public sealed record VerificationCheck(string Name, bool Passed, IReadOnlyList<string> Details)
{
    public string Icon => Passed ? "OK  " : "FALLA";
}

public sealed class VerificationReport
{
    public required IReadOnlyList<VerificationCheck> Checks { get; init; }
    public required IReadOnlyList<EffectiveValue> EffectiveTable { get; init; }
    public bool AllPassed => Checks.All(c => c.Passed);
}

/// <summary>Valor efectivo de una propiedad en un proyecto, ya resuelta la herencia de Common.props.</summary>
public sealed record EffectiveValue(string Project, PropertyKey Key, string Value, bool FromOverride);

/// <summary>Chequeos post-migración: Import presente, sin residuos y con el orden de evaluación correcto.</summary>
public static class Verifier
{
    public static VerificationReport Run(
        SolutionSet solution,
        MigrationOptions options,
        IReadOnlySet<PropertyKey>? expectedOverrides = null)
    {
        var checks = new List<VerificationCheck>();
        var commonPath = Path.Combine(solution.RootDirectory, options.CommonPropsFileName);

        // 1) Common.props existe y parsea.
        CommonPropsFile? common = null;
        if (!File.Exists(commonPath))
        {
            checks.Add(new VerificationCheck($"{options.CommonPropsFileName} existe", false,
                [$"No se encontró {commonPath}."]));
        }
        else
        {
            try
            {
                common = CommonPropsFile.Load(commonPath);
                checks.Add(new VerificationCheck($"{options.CommonPropsFileName} existe y es XML válido", true,
                    [$"{common.Values.Count} propiedades centralizadas."]));
            }
            catch (Exception ex)
            {
                checks.Add(new VerificationCheck($"{options.CommonPropsFileName} es XML válido", false, [ex.Message]));
            }
        }

        // 2) Import presente en el 100% de los .cwproj.
        var missingImport = solution.Projects
            .Where(p => p.FindCommonPropsImport(options.CommonPropsFileName) is null)
            .Select(p => p.FileName)
            .ToList();
        checks.Add(new VerificationCheck(
            $"Import de {options.CommonPropsFileName} en el 100% de los .cwproj",
            missingImport.Count == 0,
            missingImport.Count == 0
                ? [$"{solution.Projects.Count}/{solution.Projects.Count} proyectos."]
                : missingImport.Select(f => $"{f}: falta el Import.").ToList()));

        // 3) El Import se evalúa antes que los PropertyGroup condicionales del proyecto,
        //    que es lo que hace que un valor que quede en el .cwproj pise al de Common.props.
        var badOrder = new List<string>();
        foreach (var project in solution.Projects)
        {
            var import = project.FindCommonPropsImport(options.CommonPropsFileName);
            if (import is null) continue;
            var importLine = ClarionProjectFile.LineIndexOf(import);
            var firstConditional = project.PropertyGroups
                .Where(g => !g.Scope.IsGeneral && g.StartLineIndex >= 0)
                .Select(g => g.StartLineIndex)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            if (importLine >= 0 && firstConditional != int.MaxValue && importLine > firstConditional)
                badOrder.Add($"{project.FileName}: el Import (línea {importLine + 1}) está después del primer PropertyGroup condicional (línea {firstConditional + 1}); los overrides del proyecto no tendrían efecto.");
        }
        checks.Add(new VerificationCheck("Import antes de los PropertyGroup condicionales", badOrder.Count == 0,
            badOrder.Count == 0 ? ["Orden de evaluación correcto en todos los proyectos."] : badOrder));

        // 4) Residuos: propiedades que están en Common.props y siguen declaradas en algún .cwproj.
        var effective = new List<EffectiveValue>();
        var residues = new List<string>();
        var unexpected = new List<string>();

        if (common is not null)
        {
            foreach (var key in common.Keys)
            {
                foreach (var project in solution.Projects)
                {
                    var occurrence = project.Find(key);
                    var fromOverride = occurrence is not null;
                    effective.Add(new EffectiveValue(project.Name, key, occurrence?.Value ?? common.Values[key], fromOverride));

                    if (!fromOverride) continue;
                    var line = $"{project.FileName}: <{key.Name}> ({key.Scope.Display}) = '{occurrence!.Value}' pisa el valor común '{common.Values[key]}'.";
                    residues.Add(line);
                    if (expectedOverrides is not null && !expectedOverrides.Contains(key)) unexpected.Add(line);
                }
            }
        }

        if (expectedOverrides is not null)
        {
            checks.Add(new VerificationCheck("Sin residuos inesperados en los .cwproj", unexpected.Count == 0,
                unexpected.Count == 0
                    ? residues.Count == 0
                        ? ["Ninguna propiedad centralizada quedó declarada en los .cwproj."]
                        : residues.Prepend($"{residues.Count} override(s) intencional(es):").ToList()
                    : unexpected));
        }
        else
        {
            checks.Add(new VerificationCheck("Overrides por proyecto", true,
                residues.Count == 0
                    ? ["Ninguna propiedad centralizada quedó declarada en los .cwproj."]
                    : residues.Prepend($"{residues.Count} propiedad(es) siguen declaradas por proyecto (pisan a Common.props):").ToList()));
        }

        return new VerificationReport { Checks = checks, EffectiveTable = effective };
    }
}
