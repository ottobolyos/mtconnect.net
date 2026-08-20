# Mutation testing — Stryker.NET

Stryker.NET is the mutation-testing framework adopted as the Ultrareview coverage-quality gate in PR #233 (per user D3 directive, 2026-08-20). Mutation testing complements line / branch coverage by mutating the code under test and asserting that at least one test fails per mutation — surviving mutants are gaps the coverage report cannot see.

## Configuration

`stryker-config.json` at the repo root pins the entry-point project + test project:

```json
{
  "stryker-config": {
    "project": "MTConnect.NET-Common.csproj",
    "solution": "MTConnect.NET.sln",
    "test-projects": ["tests/MTConnect.NET-Common-Tests/MTConnect.NET-Common-Tests.csproj"],
    "target-framework": "net8.0",
    "reporters": ["progress", "cleartext", "html", "json"],
    "thresholds": { "high": 100, "low": 100, "break": 100 },
    "concurrency": 4,
    "mutation-level": "Complete",
    "mutate": [
      "!**/*.g.cs",
      "!libraries/MTConnect.NET-Common/Assets/**/*.g.cs",
      "!libraries/MTConnect.NET-Common/Devices/**/*.g.cs",
      "!libraries/MTConnect.NET-Common/Observations/**/*.g.cs"
    ]
  }
}
```

Key choices:

- **Target: `MTConnect.NET-Common`** — the largest hand-authored surface. Subsequent adoptions extend the roster (`MTConnect.NET-Generator-Tests`, `MTConnect.NET-XML`, `MTConnect.NET-JSON-cppagent`) once the Common project reaches zero surviving mutants.
- **Reporters** — `progress` + `cleartext` for the terminal replay, `html` for browsable maintainer report, `json` for CI ingestion. No `dashboard` reporter (no external API surface).
- **Thresholds: 100 / 100 / 100** — 100 % mutation score is the Ultrareview gate per CONVENTIONS §1.0d-trigies-septdecies. `break` at 100 means any surviving mutant fails the run.
- **Mutate excludes: every `*.g.cs`** — generator output is not hand-authored code; mutating it produces meaningless results. Fidelity of the generator emission is guarded by `tests/MTConnect.NET-Generator-Tests/` byte-identity + delta cross-verify tests.
- **Concurrency: 4** — matches the bluefin CPU budget without oversubscribing.

## Running locally

Install the tool once per machine:

```bash
dotnet tool install -g dotnet-stryker
```

Then, from the repo root:

```bash
dotnet stryker
```

A typical run takes 30 – 60 minutes on the `MTConnect.NET-Common` surface (four-way concurrency, ~250 hand-authored source files). Results land under `StrykerOutput/<timestamp>/` — open `reports/mutation-report.html` for the browsable report and `mutation-report.json` for automation.

## Running in CI

The Stryker gate is not wired into `dotnet.yml` yet; the config lands standalone in PR #233 with the runner integration deferred to a follow-up PR per user directive. When wired, the workflow shape is `dotnet stryker --config-file stryker-config.json --reporter json` on a nightly cron + label-triggered on-demand, uploading `StrykerOutput/**/*` as an artefact and failing the job on `--break-at 100` (i.e. any surviving mutant).

## Handling surviving mutants

Every surviving mutant has exactly two acceptable dispositions:

1. **Killed by a new test.** Add a test that would fail if the mutation were shipped, land it in the same PR that introduced the surface. This is the default disposition — 99 % of surviving mutants deserve a matching test.
2. **Explicit exclusion with rationale.** Add the mutant to `stryker-config.json`'s `mutate.excluded-mutations` list (or use a `// Stryker disable next-line <mutator>` pragma at the source site) with a comment explaining why the mutation is spec-equivalent / performance-equivalent / defensively-unreachable. Rare — needs code-level rationale.

There is no "leave it for later" disposition. Zero surviving mutants (or fully-justified exclusions) is the merge gate.

## References

- Stryker.NET: <https://stryker-mutator.io/docs/stryker-net/introduction/>
- Configuration options: <https://stryker-mutator.io/docs/stryker-net/configuration/>
- CONVENTIONS §1.0d-trigies-septdecies (Ultrareview coverage-quality gate)
- PR #233 adoption commit — `chore(tests): adopt Stryker.NET mutation-testing framework`
