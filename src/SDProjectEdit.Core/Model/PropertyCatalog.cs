namespace SDProjectEdit.Core.Model;

public enum PropertyEditorKind
{
    Text,
    Boolean,
    Choice,
    Integer,
    Path,
}

/// <summary>Metadatos de una propiedad conocida de un .cwproj de Clarion.</summary>
public sealed record PropertyInfo(
    string Name,
    string Label,
    PropertyEditorKind Kind,
    string Description,
    IReadOnlyList<string> Choices)
{
    public static PropertyInfo Unknown(string name) =>
        new(name, name, PropertyEditorKind.Text, "Propiedad no catalogada; se trata como texto libre.", []);
}

/// <summary>
/// Catálogo de propiedades: cuáles jamás se centralizan y metadatos de edición
/// para las que sí. Lo que no está catalogado se admite igual, como texto libre.
/// </summary>
public static class PropertyCatalog
{
    /// <summary>
    /// Propiedades inherentemente por-proyecto. Nunca se mueven a Common.props ni se editan en masa:
    /// identifican al proyecto o definen qué linkea cada uno.
    /// </summary>
    public static readonly IReadOnlySet<string> NeverUnify = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectGuid",
        "ProjectName",
        "ProjectTypeGuids",
        "AssemblyName",
        "OutputName",
        "RootNamespace",
        "ApplicationIcon",
        "DefineConstants",
        "Model",
        "OutputType",
        "CWOutputType",
        "TargetName",
        "Configuration",
        "Platform",
        "RedirectionFile",
        "SolutionDir",
        "ProjectView",
        "AppGenAppFile",
        "DictionaryFile",
    };

    private static readonly IReadOnlyList<string> BoolChoices = ["True", "False"];

    private static readonly Dictionary<string, PropertyInfo> Known =
        new(StringComparer.OrdinalIgnoreCase);

    static PropertyCatalog()
    {
        Add("DebugSymbols", "Generar símbolos de depuración", PropertyEditorKind.Boolean,
            "Emite información de depuración para el debugger.", BoolChoices);
        Add("DebugType", "Tipo de info de depuración", PropertyEditorKind.Choice,
            "Nivel de información de depuración generada por el compilador.", ["Full", "None", "PdbOnly"]);
        Add("vid", "Debug info (vid)", PropertyEditorKind.Choice,
            "Información de variables para el debugger de Clarion.", ["full", "off"]);
        Add("check_stack", "Verificar stack en runtime", PropertyEditorKind.Boolean,
            "Agrega chequeo de desbordamiento de stack en tiempo de ejecución. Cuesta performance.", BoolChoices);
        Add("check_index", "Verificar índices en runtime", PropertyEditorKind.Boolean,
            "Agrega chequeo de límites de arrays en tiempo de ejecución. Cuesta performance.", BoolChoices);
        Add("check_case", "Verificar CASE en runtime", PropertyEditorKind.Boolean,
            "Chequea estructuras CASE sin rama coincidente en tiempo de ejecución.", BoolChoices);
        Add("warnings", "Warnings del compilador", PropertyEditorKind.Choice,
            "Habilita la emisión de warnings del compilador.", ["on", "off"]);
        Add("GenerateMap", "Generar archivo .map", PropertyEditorKind.Boolean,
            "Genera el archivo de mapa del linker. Necesario para resolver direcciones en un GPF.", BoolChoices);
        Add("line_numbers", "Números de línea", PropertyEditorKind.Boolean,
            "Incluye números de línea en el ejecutable. Mejora los reportes de error a costa de tamaño.", BoolChoices);
        Add("stack_size", "Tamaño de stack", PropertyEditorKind.Integer,
            "Tamaño del stack del hilo principal, en bytes.", []);
        Add("OutputPath", "Carpeta de salida", PropertyEditorKind.Path,
            "Carpeta donde se deja el binario compilado.", []);
        Add("IntermediateOutputPath", "Carpeta de intermedios", PropertyEditorKind.Path,
            "Carpeta de objetos intermedios (.obj).", []);
        Add("dep", "DEP (Data Execution Prevention)", PropertyEditorKind.Boolean,
            "Marca el binario como compatible con DEP.", BoolChoices);
        Add("dynamic_base", "ASLR (dynamic base)", PropertyEditorKind.Boolean,
            "Marca el binario como reubicable (ASLR).", BoolChoices);
        Add("CopyCore", "Copiar runtime de Clarion", PropertyEditorKind.Boolean,
            "Copia las DLL del runtime de Clarion a la carpeta de salida.", BoolChoices);
        Add("compress", "Comprimir binario", PropertyEditorKind.Boolean,
            "Comprime el ejecutable resultante.", BoolChoices);
        Add("keep_asm", "Conservar assembler", PropertyEditorKind.Boolean,
            "Deja los listados de assembler generados.", BoolChoices);
        Add("profile", "Habilitar profiling", PropertyEditorKind.Boolean,
            "Genera información para el profiler.", BoolChoices);
        Add("verbose", "Compilación verbose", PropertyEditorKind.Boolean,
            "Salida detallada del compilador.", BoolChoices);
        Add("PragmaOptions", "Opciones pragma", PropertyEditorKind.Text,
            "Opciones adicionales pasadas al compilador vía pragma.", []);
        Add("LinkerOptions", "Opciones del linker", PropertyEditorKind.Text,
            "Opciones adicionales pasadas al linker.", []);
        Add("Optimize", "Optimizar", PropertyEditorKind.Boolean,
            "Habilita optimizaciones del compilador.", BoolChoices);

        static void Add(string name, string label, PropertyEditorKind kind, string description, IReadOnlyList<string> choices) =>
            Known[name] = new PropertyInfo(name, label, kind, description, choices);
    }

    public static bool IsNeverUnify(string name) => NeverUnify.Contains(name);

    public static PropertyInfo Describe(string name) =>
        Known.TryGetValue(name, out var info) ? info : PropertyInfo.Unknown(name);

    public static bool IsKnown(string name) => Known.ContainsKey(name);

    public static IEnumerable<PropertyInfo> AllKnown => Known.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
}
