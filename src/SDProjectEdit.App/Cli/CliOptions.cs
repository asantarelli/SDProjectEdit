using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;
using SDProjectEdit.Core.Planning;

namespace SDProjectEdit.App.Cli;

internal sealed class CliOptions
{
    public string? Path { get; private set; }
    public bool AssumeYes { get; private set; }
    public bool DryRun { get; private set; }
    public bool IncludeOrphans { get; private set; }
    public bool Recursive { get; private set; }
    public bool KeepEmptyGroups { get; private set; }
    public bool NoBackup { get; private set; }
    public string CommonPropsFileName { get; private set; } = CommonPropsFile.DefaultFileName;
    public ImportPlacement ImportPlacement { get; private set; } = ImportPlacement.AfterFirstPropertyGroup;

    /// <summary>Decisiones explícitas del usuario, en orden.</summary>
    public List<(PropertyKey Key, string? Value, DecisionKind Kind)> Overrides { get; } = [];

    /// <summary>Asignaciones sueltas del comando 'set'.</summary>
    public List<(PropertyKey Key, string Value)> Assignments { get; } = [];

    public List<PropertyKey> Removals { get; } = [];

    public MigrationOptions ToMigrationOptions() => new()
    {
        ImportPlacement = ImportPlacement,
        RemoveEmptyPropertyGroups = !KeepEmptyGroups,
        CreateBackup = !NoBackup,
        CommonPropsFileName = CommonPropsFileName,
    };

    public static CliOptions Parse(string[] args, out string? error)
    {
        var options = new CliOptions();
        error = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--yes" or "-y": options.AssumeYes = true; continue;
                case "--dry-run": options.DryRun = true; continue;
                case "--all": options.IncludeOrphans = true; continue;
                case "--recursive": options.Recursive = true; continue;
                case "--keep-empty-groups": options.KeepEmptyGroups = true; continue;
                case "--no-backup": options.NoBackup = true; continue;
                case "--gui": continue;

                case "--props":
                    if (!TryNext(args, ref i, out var propsName)) { error = "--props necesita un nombre de archivo."; return options; }
                    options.CommonPropsFileName = propsName;
                    continue;

                case "--import-at":
                    if (!TryNext(args, ref i, out var where)) { error = "--import-at necesita 'project' o 'group'."; return options; }
                    options.ImportPlacement = where.ToLowerInvariant() switch
                    {
                        "project" => ImportPlacement.AfterProjectElement,
                        "group" => ImportPlacement.AfterFirstPropertyGroup,
                        _ => options.ImportPlacement,
                    };
                    if (where.ToLowerInvariant() is not ("project" or "group")) { error = $"--import-at no acepta '{where}'."; return options; }
                    continue;

                case "--unify" or "--unify-keep-overrides" or "--leave" or "--remove":
                {
                    if (!TryNext(args, ref i, out var spec)) { error = $"{arg} necesita Ambito:Propiedad."; return options; }
                    if (!TryParseSpec(spec, out var key, out var value, out var specError)) { error = specError; return options; }

                    switch (arg.ToLowerInvariant())
                    {
                        case "--unify": options.Overrides.Add((key, value, DecisionKind.Unify)); break;
                        case "--unify-keep-overrides": options.Overrides.Add((key, value, DecisionKind.UnifyKeepOverrides)); break;
                        case "--leave": options.Overrides.Add((key, null, DecisionKind.Leave)); break;
                        case "--remove": options.Removals.Add(key); break;
                    }
                    continue;
                }
            }

            if (arg.StartsWith('-')) { error = $"opción desconocida '{arg}'."; return options; }

            // Posicional: el primero es el path; el resto son asignaciones del comando 'set'.
            if (options.Path is null) { options.Path = arg; continue; }

            if (!TryParseSpec(arg, out var setKey, out var setValue, out var setError)) { error = setError; return options; }
            if (setValue is null) { error = $"'{arg}' necesita un valor: Ambito:Propiedad=Valor."; return options; }
            options.Assignments.Add((setKey, setValue));
        }

        return options;
    }

    private static bool TryNext(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length) { value = ""; return false; }
        value = args[++i];
        return true;
    }

    /// <summary>Interpreta 'Ambito:Propiedad' o 'Ambito:Propiedad=Valor'.</summary>
    private static bool TryParseSpec(string spec, out PropertyKey key, out string? value, out string? error)
    {
        key = default;
        value = null;
        error = null;

        var equals = spec.IndexOf('=');
        var left = equals >= 0 ? spec[..equals] : spec;
        if (equals >= 0) value = spec[(equals + 1)..];

        var colon = left.IndexOf(':');
        if (colon <= 0 || colon == left.Length - 1)
        {
            error = $"'{spec}' no tiene la forma Ambito:Propiedad[=Valor] (por ejemplo Release:GenerateMap=True).";
            return false;
        }

        var scopeText = left[..colon].Trim();
        var name = left[(colon + 1)..].Trim();
        if (name.Length == 0) { error = $"'{spec}': falta el nombre de la propiedad."; return false; }

        var scope = scopeText.Equals("general", StringComparison.OrdinalIgnoreCase) || scopeText == "*"
            ? PropertyScope.General
            : PropertyScope.For(scopeText);

        key = new PropertyKey(scope, name);
        return true;
    }
}
