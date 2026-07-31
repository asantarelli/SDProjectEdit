using System.Text;

namespace SDProjectEdit.Core.Tests;

/// <summary>
/// Arma un solution Clarion sintético en una carpeta temporal, con el formato físico exacto
/// que genera el IDE: UTF-8 con BOM, CRLF, sin declaración XML y sin salto de línea final.
/// </summary>
public sealed class SolutionFixture : IDisposable
{
    public SolutionFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "sdprojectedit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CommonPropsPath => Path.Combine(Root, "Common.props");

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* carpeta temporal */ }
    }

    /// <summary>Escribe un .cwproj con el formato físico de Clarion y devuelve su path.</summary>
    public string WriteProject(string name, params string[] bodyLines)
    {
        var path = Path.Combine(Root, name + ".cwproj");
        var text = string.Join("\r\n", bodyLines);
        var bytes = new UTF8Encoding(false).GetBytes(text);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write([0xEF, 0xBB, 0xBF]);
        stream.Write(bytes);
        return path;
    }

    /// <summary>
    /// Un .cwproj típico. <paramref name="releaseExtra"/> se inserta dentro del PropertyGroup
    /// de Release para poder simular divergencias entre proyectos.
    /// </summary>
    public string WriteTypicalProject(
        string name,
        string releaseVid = "off",
        IEnumerable<string>? releaseExtra = null,
        IEnumerable<string>? generalExtra = null)
    {
        var lines = new List<string>
        {
            "<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
            "  <PropertyGroup>",
            $"    <ProjectGuid>{{{Guid.NewGuid().ToString().ToUpperInvariant()}}}</ProjectGuid>",
            "    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>",
            "    <Platform Condition=\" '$(Platform)' == '' \">Win32</Platform>",
            "    <OutputType>Library</OutputType>",
            $"    <AssemblyName>{name}</AssemblyName>",
            $"    <OutputName>{name}</OutputName>",
            // Con entidades a propósito: si algo reserializa el XML, esto se rompe y el test lo detecta.
            "    <DefineConstants>_ABCDllMode_=&gt;1%3b_ABCLinkMode_=&gt;0</DefineConstants>",
            "    <Model>Dll</Model>",
        };
        lines.AddRange(generalExtra ?? []);
        lines.AddRange([
            "  </PropertyGroup>",
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Debug' \">",
            "    <DebugSymbols>True</DebugSymbols>",
            "    <DebugType>Full</DebugType>",
            "    <vid>full</vid>",
            "    <check_stack>True</check_stack>",
            "  </PropertyGroup>",
            "  <PropertyGroup Condition=\" '$(Configuration)' == 'Release' \">",
            "    <DebugSymbols>False</DebugSymbols>",
            "    <DebugType>None</DebugType>",
            $"    <vid>{releaseVid}</vid>",
            "    <check_stack>False</check_stack>",
            "    <OutputPath>.\\</OutputPath>",
        ]);
        lines.AddRange(releaseExtra ?? []);
        lines.AddRange([
            "  </PropertyGroup>",
            "  <ItemGroup>",
            $"    <Compile Include=\"{name}.clw\">",
            "      <Generated>true</Generated>",
            "    </Compile>",
            "  </ItemGroup>",
            "  <Import Project=\"$(ClarionBinPath)\\SoftVelocity.Build.Clarion.targets\" />",
            "</Project>",
        ]);

        return WriteProject(name, [.. lines]);
    }

    public string ReadText(string fileName)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, fileName));
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
    }

    public byte[] ReadBytes(string fileName) => File.ReadAllBytes(Path.Combine(Root, fileName));
}
