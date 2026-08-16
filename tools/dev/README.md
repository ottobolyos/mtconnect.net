# `tools/dev/` — local development-loop helpers

This directory will hold repo-side scripts that speed up the local
inner loop — spin up a demo agent against a fake adapter, tail agent
logs while a change is being iterated, regenerate one narrow slice of
the docs without paying the full `npm run regen` wall-clock, and
similar.

Empty on purpose. The first helper will be added in a follow-up PR
once the release pipeline in `tools/release/` is stable and the
inner-loop pain points are cleaner to prioritise.

For the currently-shipped inner-loop scripts (`tools/dotnet.sh`,
`tools/test.sh`) see the sibling docs under `docs/cli/dotnet-sh` and
`docs/cli/test-sh` — those pre-date this reorganisation and stay at
`tools/` root so their existing CI + doc references are undisturbed.
