namespace SDProjectEdit.Core.Model;

/// <summary>
/// Ámbito de una propiedad dentro de un .cwproj: el PropertyGroup general (sin Condition)
/// o uno condicionado por una Configuration (Debug / Release / la que sea).
/// </summary>
public readonly struct PropertyScope : IEquatable<PropertyScope>
{
    private PropertyScope(string? configuration) => Configuration = configuration;

    /// <summary>Nombre de la Configuration, o null si es el PropertyGroup general.</summary>
    public string? Configuration { get; }

    public static PropertyScope General => default;

    public static PropertyScope For(string configuration) => new(configuration);

    public bool IsGeneral => Configuration is null;

    public string Display => Configuration ?? "(general)";

    public bool Equals(PropertyScope other) =>
        string.Equals(Configuration, other.Configuration, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PropertyScope other && Equals(other);

    public override int GetHashCode() =>
        Configuration is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Configuration);

    public override string ToString() => Display;

    public static bool operator ==(PropertyScope a, PropertyScope b) => a.Equals(b);

    public static bool operator !=(PropertyScope a, PropertyScope b) => !a.Equals(b);
}

/// <summary>Identifica una propiedad concreta: ámbito + nombre. El nombre se compara sin distinguir mayúsculas.</summary>
public readonly struct PropertyKey : IEquatable<PropertyKey>, IComparable<PropertyKey>
{
    public PropertyKey(PropertyScope scope, string name)
    {
        Scope = scope;
        Name = name;
    }

    public PropertyScope Scope { get; }

    public string Name { get; }

    public bool Equals(PropertyKey other) =>
        Scope.Equals(other.Scope) && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PropertyKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Scope.GetHashCode(), StringComparer.OrdinalIgnoreCase.GetHashCode(Name));

    public int CompareTo(PropertyKey other)
    {
        // General primero, después las configurations en orden alfabético.
        var a = Scope.Configuration ?? "";
        var b = other.Scope.Configuration ?? "";
        var c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => $"{Scope.Display}/{Name}";
}
