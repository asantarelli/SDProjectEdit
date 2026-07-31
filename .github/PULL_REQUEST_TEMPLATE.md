## Qué cambia

<!-- Una o dos frases. Si cierra un issue: Closes #N -->

## Por qué

<!-- El problema que resuelve, no la implementación -->

## Checklist

- [ ] `dotnet test` pasa
- [ ] `dotnet build /warnaserror` pasa
- [ ] Si toca la escritura de `.cwproj`: hay un test que verifica que lo no migrado queda byte a byte igual
- [ ] Si agrega una propiedad al catálogo: está clasificada bien (centralizable vs. por-proyecto)
- [ ] Si cambia el comportamiento observable: el README quedó actualizado

## Cómo lo probaste

<!-- Idealmente contra un solution multi-DLL real. Decí cuántos proyectos y si corriste un
     Rebuild Solution completo después. -->
