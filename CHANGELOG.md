# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).
Versionado según [SemVer](https://semver.org/lang/es/).

## [No publicado]

## [1.0.0] — 2026-07-31

Primera versión.

### Agregado

- **Relevamiento** de todos los `.cwproj` de un solution, con tabla comparativa de propiedades
  contra proyectos.
- **Detección de divergencias** en cuatro categorías: uniforme, divergente, parcial y
  parcial + divergente. Nada divergente se unifica sin decisión explícita.
- **Migración a `Common.props`** con tres estrategias por propiedad: dejar por-proyecto, unificar,
  o unificar conservando overrides.
- **Reporte de cambios de comportamiento reales** antes de escribir: por proyecto y propiedad,
  valor anterior contra valor resultante.
- **Verificación** post-migración: `Common.props` válido, `Import` en el 100% de los `.cwproj`,
  orden de evaluación correcto, y ausencia de residuos inesperados.
- **Comando `set`** para editar `Common.props` después de migrar, avisando qué proyectos tienen
  un override local que anula el cambio.
- **Ventana WinForms** con grilla de decisiones, detalle por proyecto, editor de `Common.props`
  y las pestañas de relevamiento, plan y verificación.
- **CLI** con `analizar`, `plan`, `aplicar`, `verificar` y `set`, y exit codes
  (`0` ok, `1` error, `2` verificación fallida, `3` uso incorrecto).
- Backup automático en `.sdprojectedit\backup\<timestamp>\` antes de escribir.
- Log de fallos de arranque de la ventana en `%LOCALAPPDATA%\SDProjectEdit\crash.log`.

### Decisiones de diseño

- **El `Import` va después del PropertyGroup general**, no justo después de `<Project>`. Ese primer
  grupo es el que fija el default de `$(Configuration)`; importar antes deja las condiciones de
  `Common.props` sin evaluar cuando MSBuild se invoca sin pasar `Configuration`, y el proyecto
  compila con los defaults del compilador en silencio. Verificado con `dotnet msbuild -getProperty`.
  Se puede forzar la ubicación anterior con `--import-at project`.
- **Edición por líneas, no reserialización del XML.** Un round-trip por `XmlWriter` reescribe el
  escapado de entidades (`&gt;` dentro de `DefineConstants` volvería como `>`), ensuciando archivos
  que no se querían tocar. Se preservan BOM, CRLF, ausencia de declaración XML y ausencia de salto
  de línea final.
- **Nada se escribe si algo falla.** Todos los archivos resultantes se construyen y validan en
  memoria — reparseo del XML y confirmación de que las propiedades removidas no están y los
  overrides sí — antes de tocar el disco.

[No publicado]: https://github.com/asantarelli/SDProjectEdit/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/asantarelli/SDProjectEdit/releases/tag/v1.0.0
