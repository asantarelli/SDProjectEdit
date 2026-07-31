using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;
using SDProjectEdit.Core.Planning;
using SDProjectEdit.Core.Reporting;

namespace SDProjectEdit.App.Cli;

internal static class CommandLine
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitVerificationFailed = 2;
    public const int ExitBadUsage = 3;

    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "analizar", "analyze", "plan", "aplicar", "apply", "verificar", "verify", "set",
        "-h", "--help", "help", "/?", "ayuda", "--version",
    };

    /// <summary>True si el token es un comando de la CLI (y no, por ejemplo, un path suelto).</summary>
    public static bool IsCommand(string token) => Commands.Contains(token);

    public static int Run(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return ExitError;
        }
    }

    private static int Dispatch(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        if (command is "-h" or "--help" or "help" or "/?" or "ayuda") { PrintUsage(); return ExitOk; }
        if (command is "--version") { Console.WriteLine("SDProjectEdit 1.0.0"); return ExitOk; }

        var options = CliOptions.Parse(args, out var error);
        if (error is not null) { Console.Error.WriteLine($"ERROR: {error}"); PrintUsage(); return ExitBadUsage; }
        if (options.Path is null) { Console.Error.WriteLine("ERROR: falta el path del solution."); return ExitBadUsage; }

        return command switch
        {
            "analizar" or "analyze" => Analyze(options),
            "plan" => Plan(options),
            "aplicar" or "apply" => Apply(options),
            "verificar" or "verify" => Verify(options),
            "set" => Set(options),
            _ => Unknown(command),
        };

        static int Unknown(string command)
        {
            Console.Error.WriteLine($"ERROR: comando desconocido '{command}'.");
            PrintUsage();
            return ExitBadUsage;
        }
    }

    // ---- comandos ------------------------------------------------------------------------

    private static int Analyze(CliOptions options)
    {
        var matrix = LoadMatrix(options);
        Console.WriteLine(TextReport.Survey(matrix));
        Console.WriteLine(TextReport.Divergences(matrix));
        return matrix.Solution.LoadErrors.Count > 0 ? ExitError : ExitOk;
    }

    private static int Plan(CliOptions options)
    {
        var (plan, warnings) = BuildPlan(options);
        foreach (var warning in warnings) Console.WriteLine(warning);
        Console.WriteLine(TextReport.Plan(plan));

        var dryRun = MigrationExecutor.Apply(plan, dryRun: true);
        Console.WriteLine(TextReport.ApplyOutcome(dryRun));
        return plan.CanApply && dryRun.Errors.Count == 0 ? ExitOk : ExitError;
    }

    private static int Apply(CliOptions options)
    {
        var (plan, warnings) = BuildPlan(options);
        foreach (var warning in warnings) Console.WriteLine(warning);
        Console.WriteLine(TextReport.Plan(plan));

        if (!plan.CanApply)
        {
            Console.Error.WriteLine("No se aplica nada: hay bloqueos.");
            return ExitError;
        }

        if (plan.ChangedEdits.Count() == 0 && File.Exists(plan.CommonProps.Path))
            Console.WriteLine("Nada que cambiar en los .cwproj.");

        if (!options.AssumeYes)
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Se requiere confirmación. Volvé a correrlo con --yes.");
                return ExitError;
            }
            Console.Write($"\n¿Aplicar los cambios a {plan.ChangedEdits.Count()} .cwproj + {plan.CommonProps.FileName}? [s/N] ");
            var answer = Console.ReadLine();
            if (answer is null || !answer.Trim().StartsWith('s') && !answer.Trim().StartsWith('S'))
            {
                Console.WriteLine("Cancelado. No se escribió nada.");
                return ExitOk;
            }
        }

        var result = MigrationExecutor.Apply(plan, dryRun: false);
        Console.WriteLine(TextReport.ApplyOutcome(result));
        if (!result.Applied) return ExitError;

        var reloaded = SolutionSet.Load(options.Path!, !options.IncludeOrphans, options.Recursive);
        var report = Verifier.Run(reloaded, options.ToMigrationOptions(), plan.ExpectedOverrides);
        Console.WriteLine();
        Console.WriteLine(TextReport.Verification(report));
        return report.AllPassed ? ExitOk : ExitVerificationFailed;
    }

    private static int Verify(CliOptions options)
    {
        var solution = SolutionSet.Load(options.Path!, !options.IncludeOrphans, options.Recursive);
        var report = Verifier.Run(solution, options.ToMigrationOptions());
        Console.WriteLine(TextReport.Verification(report));
        return report.AllPassed ? ExitOk : ExitVerificationFailed;
    }

    private static int Set(CliOptions options)
    {
        if (options.Assignments.Count == 0 && options.Removals.Count == 0)
        {
            Console.Error.WriteLine("ERROR: 'set' necesita al menos un Ambito:Propiedad=Valor o --remove Ambito:Propiedad.");
            return ExitBadUsage;
        }

        var solution = SolutionSet.Load(options.Path!, !options.IncludeOrphans, options.Recursive);
        var migrationOptions = options.ToMigrationOptions();
        var commonPath = Path.Combine(solution.RootDirectory, migrationOptions.CommonPropsFileName);
        var common = CommonPropsFile.Load(commonPath);

        if (!common.ExistedOnDisk)
        {
            Console.Error.WriteLine($"ERROR: no existe {commonPath}. Corré primero 'aplicar' para crearlo.");
            return ExitError;
        }

        Console.WriteLine($"Editando {common.FileName}");
        foreach (var (key, value) in options.Assignments)
        {
            if (PropertyCatalog.IsNeverUnify(key.Name))
            {
                Console.Error.WriteLine($"ERROR: {key.Name} es una propiedad por-proyecto, no se centraliza.");
                return ExitError;
            }
            var before = common.Get(key);
            common.Set(key, value);
            Console.WriteLine($"  {key,-34} {before ?? "(no estaba)"}  ->  {value}");

            var shadowed = solution.Projects.Where(p => p.Find(key) is not null).Select(p => p.Name).ToList();
            if (shadowed.Count > 0)
                Console.WriteLine($"    aviso: {shadowed.Count} proyecto(s) lo declaran localmente y NO se ven afectados: {string.Join(", ", shadowed)}");
        }

        foreach (var key in options.Removals)
            Console.WriteLine(common.Remove(key)
                ? $"  {key,-34} eliminada de {common.FileName}"
                : $"  {key,-34} no estaba en {common.FileName}");

        var format = (solution.Projects.FirstOrDefault()?.Format ?? TextFileFormat.ClarionDefault)
            with { HasXmlDeclaration = false, TrailingNewLine = true };

        if (options.DryRun)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {common.FileName} (simulación) ---");
            Console.WriteLine(common.Render(format));
            return ExitOk;
        }

        if (migrationOptions.CreateBackup && File.Exists(commonPath))
        {
            var backupDir = Path.Combine(solution.RootDirectory, ".sdprojectedit", "backup",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backupDir);
            File.Copy(commonPath, Path.Combine(backupDir, common.FileName), overwrite: true);
            Console.WriteLine($"Backup: {backupDir}");
        }

        common.Save(format);
        Console.WriteLine($"{common.FileName} guardado.");
        Console.WriteLine();

        var report = Verifier.Run(solution, migrationOptions);
        Console.WriteLine(TextReport.Verification(report));
        return report.AllPassed ? ExitOk : ExitVerificationFailed;
    }

    // ---- helpers -------------------------------------------------------------------------

    private static PropertyMatrix LoadMatrix(CliOptions options)
    {
        var solution = SolutionSet.Load(options.Path!, !options.IncludeOrphans, options.Recursive);
        if (solution.Projects.Count == 0)
            throw new InvalidOperationException($"No se encontró ningún .cwproj en '{solution.RootDirectory}'.");
        return PropertyMatrix.Build(solution);
    }

    private static (MigrationPlan Plan, List<string> Warnings) BuildPlan(CliOptions options)
    {
        var matrix = LoadMatrix(options);
        var decisions = MigrationPlan.DefaultDecisions(matrix)
            .ToDictionary(d => d.Key, d => d);
        var warnings = new List<string>();

        foreach (var (key, value, kind) in options.Overrides)
        {
            var row = matrix[key];
            if (row is null)
            {
                warnings.Add($"aviso: {key} no aparece en ningún .cwproj; se agrega igual a Common.props.");
                decisions[key] = new PropertyDecision(key, kind, value ?? "");
                continue;
            }
            decisions[key] = new PropertyDecision(key, kind, value ?? row.MajorityValue);
        }

        var plan = MigrationPlan.Create(matrix, decisions.Values.ToList(), options.ToMigrationOptions());

        var undecided = matrix.Divergences
            .Where(r => decisions[r.Key].Kind == DecisionKind.Leave)
            .ToList();
        if (undecided.Count > 0)
        {
            warnings.Add($"aviso: {undecided.Count} propiedad(es) divergentes quedan por-proyecto (nadie decidió qué hacer):");
            foreach (var row in undecided)
                warnings.Add($"        {row.Key} — {row.StatusText}; usá --unify / --unify-keep-overrides para centralizarla.");
            warnings.Add("");
        }

        return (plan, warnings);
    }

    private static void PrintUsage() => Console.WriteLine("""
        SDProjectEdit — Editor de proyectos multi-DLL de Clarion

        Uso:
          SDProjectEdit                                   Abre la ventana.
          SDProjectEdit gui [<path>]                      Abre la ventana con ese solution cargado.
          SDProjectEdit <comando> <path> [opciones]

        <path> puede ser la carpeta del solution, un .sln o un .cwproj suelto.

        Comandos
          analizar    Relevamiento y detección de divergencias (pasos 1 y 2). No escribe nada.
          plan        Muestra el Common.props propuesto, los archivos a tocar y los cambios
                      de comportamiento reales (paso 4). No escribe nada.
          aplicar     Crea Common.props, inserta el Import y limpia los .cwproj (pasos 3 y 5),
                      y después verifica (paso 6).
          verificar   Sólo los chequeos del paso 6 sobre un solution ya migrado.
          set         Cambia valores dentro de un Common.props ya existente y verifica.

        Opciones de decisión (Ambito es 'general' o el nombre de la Configuration)
          --unify Ambito:Prop[=Valor]                Centraliza y la quita de TODOS los .cwproj.
          --unify-keep-overrides Ambito:Prop[=Valor] Centraliza; los proyectos con otro valor
                                                     lo conservan como override explícito.
          --leave Ambito:Prop                        La deja por-proyecto (anula el default).
          --remove Ambito:Prop                       Sólo para 'set': la borra de Common.props.

        Por defecto se centralizan únicamente las propiedades idénticas en el 100% de los
        proyectos. Toda divergencia queda intacta hasta que la decidas con --unify/--leave.

        Otras opciones
          --yes                     No pedir confirmación al aplicar.
          --dry-run                 Simula (para 'set'; 'plan' ya es simulación).
          --all                     Incluye .cwproj que están en disco pero no en el .sln.
          --recursive               Busca .cwproj también en subcarpetas.
          --import-at project|group Dónde va el Import: después de <Project> o después del
                                    PropertyGroup general (default: group).
          --keep-empty-groups       No eliminar los PropertyGroup que queden vacíos.
          --no-backup               No copiar los originales a .sdprojectedit\backup\.
          --props <archivo>         Nombre del archivo común (default: Common.props).

        Códigos de salida: 0 ok · 1 error · 2 verificación fallida · 3 uso incorrecto

        Ejemplos
          SDProjectEdit analizar X:\MiSolution
          SDProjectEdit plan X:\MiSolution --unify Release:GenerateMap=True
          SDProjectEdit aplicar X:\MiSolution --unify-keep-overrides Release:vid=off --yes
          SDProjectEdit set X:\MiSolution Release:GenerateMap=True Release:line_numbers=True
        """);
}
