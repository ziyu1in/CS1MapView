# Private acceptance fixture

The full acceptance suite requires `Springdale-20260927-124147.cslmap.gz`, with
SHA-256:

`C9EBCB5BA1371BAE962FB536649AFD482F91BCC523B4B7471B91E01F3921F718`

Preserve this export unchanged. Supply it separately using:

```powershell
./build.ps1 -SmokeTest -MapFile 'C:/path/to/Springdale-20260927-124147.cslmap.gz'
```

A private local copy may instead be placed in this directory for
`./build.ps1 -SmokeTest`. City exports are ignored by Git and are unnecessary
for ordinary application builds and operation.
