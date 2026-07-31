<h1 align="center">SDProjectEdit</h1>

<p align="center">
  <strong>Editor de proyectos multi-DLL de Clarion</strong><br>
  Centraliza la configuración de build de todos los <code>.cwproj</code> de un solution
  en un único <code>Common.props</code>, sin romper nada por el camino.
</p>

<p align="center">
  <a href="https://github.com/asantarelli/SDProjectEdit/actions/workflows/build.yml"><img alt="build" src="https://github.com/asantarelli/SDProjectEdit/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/asantarelli/SDProjectEdit/releases/latest"><img alt="release" src="https://img.shields.io/github/v/release/asantarelli/SDProjectEdit?label=descargar"></a>
  <a href="LICENSE"><img alt="licencia" src="https://img.shields.io/badge/licencia-MIT-blue.svg"></a>
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-x64-0078D6">
</p>

---

- [El problema](#el-problema)
- [Instalación](#instalación)
- [Arranque rápido](#arranque-rápido)
- [Cómo funciona la herencia](#cómo-funciona-la-herencia)
- [Clasificación de propiedades](#clasificación-de-propiedades)
- [Comandos](#comandos)
- [Seguridad de la edición](#seguridad-de-la-edición)
- [Selección de proyectos](#selección-de-proyectos)
- [Compilar desde el código](#compilar-desde-el-código)

## El problema

Cada `.cwproj` de un multi-DLL repite a mano las mismas propiedades de Debug/Release:
`DebugSymbols`, `DebugType`, `vid`, `check_stack`, `check_index`, `warnings`, `OutputPath`,
`GenerateMap`, `line_numbers`, `stack_size`. Cambiar un setting para todo el solution obliga a
editar N archivos.

Peor: con el tiempo aparece **drift**. Proyectos que quedaron con valores distintos sin que nadie
lo decidiera. En un solution real de 15 proyectos, esta herramienta encontró un proyecto compilando
Release con info de debug y cinco propiedades presentes en solo 3 de los 15 — ninguna de las dos
cosas era intencional.

Por eso el orden es: primero **detectar** el drift y hacértelo decidir, después centralizar.

## Instalación

Bajá `SDProjectEdit.exe` de [la última release](https://github.com/asantarelli/SDProjectEdit/releases/latest).
Es un solo archivo autocontenido — **no necesita .NET instalado**. Ponelo donde quieras.

## Arranque rápido

```bash
SDProjectEdit.exe X:\MiSolution
```

Abre la ventana con ese solution ya analizado. Sin argumentos, abre la ventana y elegís la carpeta.

Si preferís la línea de comandos, el flujo completo es este:

```bash
# 1. Ver qué hay y qué diverge. No escribe nada.
SDProjectEdit.exe analizar X:\MiSolution

# 2. Ver exactamente qué se va a tocar, con los cambios de comportamiento. Tampoco escribe nada.
SDProjectEdit.exe plan X:\MiSolution --unify-keep-overrides Release:vid=off

# 3. Aplicar. Hace backup y verifica al terminar.
SDProjectEdit.exe aplicar X:\MiSolution --unify-keep-overrides Release:vid=off --yes

# 4. Ya migrado: cambiar un valor para todo el solution.
SDProjectEdit.exe set X:\MiSolution Release:GenerateMap=True
```

> [!IMPORTANT]
> Después de aplicar, corré un **Rebuild Solution** completo en el IDE (no Compile incremental)
> para confirmar que MSBuild resuelve bien el `Import`.

## Cómo funciona la herencia

`Common.props` son los **defaults**. Lo que quede declarado en un `.cwproj` **los pisa**, y sirve
para dejar un override intencional y visible en un proyecto puntual.

Eso funciona porque el `Import` se inserta **después del PropertyGroup general** y **antes de los
PropertyGroup condicionales**. La segunda parte es obvia; la primera no, y es la razón de no
ponerlo justo después de `<Project>`, que sería lo natural.

El PropertyGroup general es el que trae:

```xml
<Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
```

Si el `Import` va antes de esa línea, MSBuild evalúa `Common.props` con `$(Configuration)` vacío,
**ninguna** de las dos condiciones da true, y el proyecto compila con los defaults del compilador.
Sin error, sin warning. Verificado con `dotnet msbuild -getProperty`:

| Ubicación del `Import` | `msbuild x.cwproj -p:Configuration=Debug` | `msbuild x.cwproj` |
|---|---|---|
| Después de `<Project>` | `vid=full`, `check_stack=True` | **`vid=`, `check_stack=`** ← se pierde todo |
| Después del PropertyGroup general | `vid=full`, `check_stack=True` | `vid=full`, `check_stack=True` |

El IDE de Clarion siempre pasa `Configuration`, así que ahí las dos ubicaciones andan; la diferencia
aparece invocando MSBuild a mano o desde un script de build. Si aun así querés la otra ubicación:
`--import-at project`.

## Clasificación de propiedades

| Estado | Significado | Decisión por defecto |
|---|---|---|
| **Uniforme** | Presente en el 100% de los proyectos con el mismo valor | Se centraliza |
| **Divergente** | Presente en todos, con valores distintos | Se deja como está |
| **Parcial** | Falta en uno o más proyectos | Se deja como está |
| **Parcial + divergente** | Las dos cosas a la vez | Se deja como está |
| **Por-proyecto** | Lista fija, nunca se centraliza | Intocable |

**Nada divergente se unifica solo.** Para cada divergencia tenés tres salidas:

| Opción | Qué hace |
|---|---|
| `--unify Ambito:Prop=Valor` | Mismo valor para todos. **Cambia comportamiento** en los que difieren — el plan te lista cuáles y de qué valor a cuál |
| `--unify-keep-overrides Ambito:Prop=Valor` | El valor va a `Common.props`; los proyectos con otro valor lo conservan en su `.cwproj`. Cero cambio de comportamiento |
| `--leave Ambito:Prop` | Queda por-proyecto, como estaba |

> [!WARNING]
> Ojo con **Parcial**. Si una propiedad falta en 12 de 15 proyectos, centralizarla **se la agrega**
> a esos 12. MSBuild no sabe expresar "sin definir", así que ahí `--unify` siempre implica un cambio
> de comportamiento real. El plan lo marca como `(no definida — default del compilador) -> Valor`.

### Nunca se centralizan

`ProjectGuid` · `ProjectName` · `ProjectTypeGuids` · `AssemblyName` · `OutputName` ·
`RootNamespace` · `ApplicationIcon` · `DefineConstants` · `Model` · `OutputType` · `CWOutputType` ·
`TargetName` · `Configuration` · `Platform` · `RedirectionFile` · `SolutionDir` · `ProjectView` ·
`AppGenAppFile` · `DictionaryFile`

`DefineConstants` es la crítica: define qué librerías van DLL vs LIB en cada proyecto. Tampoco se
toca ningún `ItemGroup` (`Compile`, `Library`, `FileDriver`, `ProjectReference`, `None`).

## Comandos

```
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
```

El comando `set` avisa si algún proyecto declara localmente la propiedad que estás cambiando —
porque ese proyecto **no** se va a ver afectado:

```
  Release/vid                        off  ->  on
    aviso: 1 proyecto(s) lo declaran localmente y NO se ven afectados: SDGI0
```

## Seguridad de la edición

Esta herramienta reescribe archivos de proyecto en los que trabaja gente. Las garantías:

- **Edición por líneas, no round-trip de XML.** Reserializar el `XDocument` reescribiría el escapado
  de entidades — `&gt;` dentro de `DefineConstants` volvería como `>` — generando diffs enormes en
  archivos que ni siquiera se querían tocar. Editando líneas puntuales, **todo lo demás queda byte a
  byte idéntico**. Se preservan BOM, CRLF, ausencia de declaración XML y ausencia de salto final.
- **Una propiedad sólo se borra si ocupa exactamente una línea y no trae `Condition` propia.** Si no,
  se reporta como "no editable automáticamente" y no se toca.
- **Un `PropertyGroup` con `Condition` que no se entiende se ignora por completo**, y se avisa.
- **Nada se escribe si algo falla.** Todo se construye y valida en memoria primero: cada archivo
  resultante se reparsea como XML y se confirma que las propiedades removidas no estén y los
  overrides sí. Si un solo archivo no pasa, **no se escribe ninguno**.
- **Backup automático** en `<raíz>\.sdprojectedit\backup\<timestamp>\`.
- **Idempotente.** Re-ejecutar `aplicar` no duplica el `Import` ni pierde lo que ya estaba en
  `Common.props`.

Todo esto está cubierto por la suite de tests (`dotnet test`), que arma solutions sintéticos con el
formato físico exacto que genera Clarion.

## Selección de proyectos

Si hay un `.sln` en la raíz, se trabaja sobre los `.cwproj` que ese `.sln` referencia. Un `.cwproj`
que esté en disco pero fuera del `.sln` se reporta como aviso y queda excluido; `--all` lo incluye.
`--recursive` busca también en subcarpetas.

## Compilar desde el código

```bash
dotnet build
dotnet test
dotnet publish src/SDProjectEdit.App/SDProjectEdit.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

No hace falta tener Clarion instalado: los tests arman solutions sintéticos en carpetas temporales.

```
src/SDProjectEdit.Core/     lógica, sin dependencias de UI
  Io/                       carga y escritura preservando formato
  Analysis/                 relevamiento y clasificación de divergencias
  Planning/                 plan, aplicación y verificación
  Reporting/                render de texto, compartido por CLI y GUI
src/SDProjectEdit.App/      ejecutable
  Cli/                      modo línea de comandos
  Ui/                       ventana WinForms
tests/                      xUnit sobre Core
```

Si la ventana falla al arrancar, el detalle queda en `%LOCALAPPDATA%\SDProjectEdit\crash.log`.

---

[Contribuir](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Licencia MIT](LICENSE)
