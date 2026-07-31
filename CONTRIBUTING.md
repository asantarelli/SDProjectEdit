# Contribuir

## Compilar y probar

```bash
dotnet build
dotnet test
```

Para generar el ejecutable autocontenido:

```bash
dotnet publish src/SDProjectEdit.App/SDProjectEdit.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

Hace falta el SDK de .NET indicado en `global.json`. No hace falta tener Clarion instalado: los
tests arman solutions sintéticos en carpetas temporales.

## Estructura

```
src/SDProjectEdit.Core/     lógica, sin dependencias de UI
  Io/                       carga y escritura preservando formato
  Analysis/                 relevamiento y clasificación de divergencias
  Planning/                 plan, aplicación y verificación
  Reporting/                render de texto, compartido por CLI y GUI
src/SDProjectEdit.App/      ejecutable (CLI + ventana)
tests/                      xUnit sobre Core
```

`Core` no referencia WinForms. Si una funcionalidad nueva necesita UI, la lógica va en `Core` y la
ventana solo la maneja.

## Reglas que no se negocian

Esta herramienta reescribe archivos de proyecto de gente que trabaja. Tres cosas no se relajan:

1. **Lo que no se migra queda byte a byte igual.** Nada de reserializar el `XDocument`. Si tocás la
   escritura, sumá un test que compare el archivo antes y después línea por línea.
2. **Nada se escribe si algo falla.** Los archivos se construyen y validan completos en memoria; si
   uno solo no pasa la validación, no se escribe ninguno.
3. **Ninguna divergencia se resuelve sola.** Si una propiedad no es idéntica en el 100% de los
   proyectos, la decisión es del usuario. La herramienta informa, no adivina.

## Agregar una propiedad al catálogo

En [`PropertyCatalog.cs`](src/SDProjectEdit.Core/Model/PropertyCatalog.cs):

- Si es inherentemente por-proyecto (identifica al proyecto, o define qué linkea), va en
  `NeverUnify`. Ante la duda, va ahí: el costo de no centralizar algo es que sigue como está; el
  costo de centralizar de más es romper builds ajenos.
- Si es centralizable, sumala en el constructor estático con su tipo de editor y una descripción
  en castellano que diga **qué hace**, no cómo se llama.

Las propiedades que no están en el catálogo funcionan igual, como texto libre. El catálogo solo
mejora la experiencia de edición.

## Estilo

`.editorconfig` manda. `dotnet build /warnaserror` tiene que pasar limpio — los proyectos están con
`TreatWarningsAsErrors`.

Comentarios y mensajes al usuario en castellano; nombres de código en inglés salvo cuando se refieren
a conceptos del dominio Clarion.

Los tests se nombran describiendo el comportamiento esperado:
`Un_PropertyGroup_que_queda_vacio_se_elimina`.

## Commits

Mensaje imperativo, en castellano, explicando el porqué cuando no es obvio. Si el cambio afecta el
comportamiento observable, actualizá `CHANGELOG.md` bajo `[No publicado]`.

## Publicar una versión

1. Mover lo de `[No publicado]` a una versión nueva en `CHANGELOG.md`.
2. Commit y tag: `git tag v1.1.0 && git push --tags`.
3. El workflow de release compila, corre los tests y publica el `.exe` con sus notas.
