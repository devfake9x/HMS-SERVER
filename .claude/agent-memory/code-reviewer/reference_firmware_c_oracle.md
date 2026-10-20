---
name: reference-firmware-c-oracle
description: Reviewing HMS-SERVER work that touches the HMS-IDF device contract — use only the MQTT spec, never firmware source; plus read-only probing pitfalls in this repo
metadata:
  type: reference
---

HMS-SERVER integrates with the HMS-IDF firmware only through the MQTT spec `engineering-docs/api/mqtt-api.md` (owner decision 2026-10-05, design contract Q14 of plan `261005-0556-device-permissions-what-where`). When a review needs device facts, read the spec at the pinned commit with `git -C D:/WORKSPACE/PROJECT/HMS-IDF show <commit>:engineering-docs/api/mqtt-api.md`; §11 is the server permission manifest. Do not open firmware source (.c/.h, components/, services/, main/) and do not compile it as an oracle: a fact the spec does not state is a spec gap to report, not something to look up in code.

- Never use `dotnet run` against project files. Use a scratch csproj with `<Reference>` to `src/*/bin/Debug/net10.0/*.dll`, or copy the src/tests `*.cs`, `*.csproj` and `*.json` files to scratch for mutation runs. Both leave the project's bin/obj untouched. A file-based app (`dotnet run app.cs`) needs `#:property JsonSerializerIsReflectionEnabledByDefault=true` for reflection-based System.Text.Json.
- Read-only pitfall: the fixture scripts in `plans/260924-2044-hms-server-management/fixtures/` write their report JSON by default (e.g. `contract-artifacts-fixture.mjs`, argv[4]). Pass a scratch output path when running them during a review.
- Probe pitfall: STJ `JsonElement.ValueEquals(text)` returns false without unescaping when the comparand is longer than the raw token. A bad escape therefore throws only for shorter comparands, so probe with every candidate key, not just one.
- Gotcha: in Bash heredoc tool input, a doubled backslash arrives as a single one. Build backslashes in scripts with `String.fromCharCode(92)` (JS) or `(char)92` (C#). See also [[literal-unicode-escapes-in-tool-input]].
