# RinkuLib docs (DocFX)

This folder is the project's DocFX documentation site. It is self-contained and does not affect the library build.

## Build locally

```bash
dotnet tool update -g docfx        # once
cd docs
docfx metadata                     # generate the API reference from XML comments
docfx build                        # build the static site into _site/
docfx serve _site                  # preview at http://localhost:8080
```

## Layout

- `articles/`. Conceptual docs, one folder per area, each a set of small pages + `toc.yml`.
- `api/`. Generated API reference (not committed. Produced by `docfx metadata`).
- `docfx.json`. Site config. Add the analyzers / PowerTools `.csproj` files under `metadata > src` to include them in the API reference.
- `index.md`, `toc.yml`. Site landing and top navigation.

All conceptual pages are written. The `api/` reference is generated on demand by `docfx metadata` (see above) and is not committed.
