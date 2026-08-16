# `tools/docs/` — documentation-generation helpers

This directory will hold repo-side scripts that produce inputs the
VitePress site consumes — spec cross-references, wire-format sample
regeneration, per-version compliance matrix rebuilds, and similar.

Empty on purpose. The existing generators live under
`docs/scripts/generate-api-ref.sh` and
`docs/scripts/generate-reference.sh` (invoked by `docs/`'s npm
`predev` / `prebuild` hooks) and stay there for now to keep the
docs-site self-contained. Follow-up PRs will migrate cross-cutting
generators to this directory as the release pipeline lands.
