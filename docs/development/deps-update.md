# Weekly deps update

The `deps-update` workflow (`.github/workflows/deps-update.yml`) fires
every Saturday at 02:00 UTC and opens one PR that bumps every
dependency in four ecosystems.

## Ecosystems covered

- GitHub Actions plugin versions in `.github/workflows/*.yml` (via
  Renovate's `github-actions` manager).
- Docker base images in every `Dockerfile` (via Renovate's `docker`
  manager).
- npm packages under `docs/` (via `npm-check-updates`).
- NuGet packages across every `.csproj` (via `dotnet-outdated-tool`).

## Supply-chain quarantine

Every candidate release is filtered by a minimum-age check — no
version younger than seven days is accepted. The invariant catches
the standard OSS "poisoned publish yanked within a week" response
window. Increase the window by editing `env.MIN_AGE_DAYS` at the top
of the workflow.

## Auto-merge

The workflow enables auto-merge on the resulting PR (`gh pr merge
--auto --squash`). CI must go green for the merge to happen; a red
CI keeps the PR open for triage.

## Single-PR invariant

If a prior deps PR from the branch `chore/deps-weekly-update` is
still open when the workflow re-fires, it is closed as superseded
before the new branch is pushed. Only one deps PR is ever open at
once — the newest bumps supersede the older ones by construction.

## Manual re-run

`gh workflow run deps-update.yml` triggers a run on-demand. The
`workflow_dispatch` handler is present for exactly this case (a hotfix
that needs a fresh dep bump outside the Saturday cadence).
