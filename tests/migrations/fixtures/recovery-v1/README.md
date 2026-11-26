# Historical v1 recovery-compatibility fixture

`migrationEngine.js` and `protocol.js` are the exact original files from
`Magic.IndexedDb/wwwroot/migrations/` in repository
`magiccodingman/Magic.IndexedDb` at commit
`fbb55aa1f2eed1a5f3138f952ac1038bc8a5b4ad`. The original root license is
retained as [LICENSE-MIT.txt](LICENSE-MIT.txt). `SHA256SUMS` records the
SHA-256 of all three original files.

The browser compatibility tests run this actual engine against IndexedDB.
Keep the bytes immutable. This test-only fixture replaces a fetch from the
retired development history and is outside the NuGet package contents.
