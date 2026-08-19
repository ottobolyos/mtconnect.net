#!/usr/bin/env -S npx tsx
/**
 * Create a GitHub pre-release for a `<version>-dev.<N>` cut and attach
 * the SBOMs + .nupkg files that the earlier pipeline steps produced.
 *
 * Uses the `gh` CLI rather than `@octokit/rest` for the release
 * creation itself — `gh release create` handles the multipart asset
 * upload semantics without any of the retry / MIME plumbing an
 * Octokit-side implementation would need. Octokit remains available
 * to `tools/` scripts that need the fine-grained REST surface (see
 * `tools/package.json`), just not this one.
 *
 * Usage:
 *   tsx tools/release/gh-release-create.ts --version <ver>
 *                                          [--repo <owner/name>]
 *                                          [--assets <dir> ...]
 *                                          [--docker-image <ref>]
 *                                          [--dry-run]
 *
 * The release is always created as `--prerelease` (Phase 1 automates
 * only the dev pre-release cadence; stable releases stay under the
 * existing MTConnect.NET.Builder flow until a follow-up wires them
 * in). Release notes list every attached asset plus the docker image
 * reference so consumers have a single machine-readable manifest of
 * the cut.
 */

import { readdirSync, existsSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { parseDryRun, run } from './shell.ts';

/** Repo root — used for default asset directories. */
const repoRoot = resolve(new URL('../../', import.meta.url).pathname);

/** CLI options. */
export type Options = {
  version: string;
  repo: string;
  assetDirs: string[];
  dockerImage: string | undefined;
  dryRun: boolean;
};

/** Parse argv into strongly-typed `Options`. Multiple `--assets` flags
 *  accumulate into a list; missing dirs are skipped with a warning
 *  (a workflow may pass both `nupkg/` and `sbom/` even when only one
 *  step ran). */
export const parseOptions = (argv: string[]): Options => {
  const { dryRun, rest } = parseDryRun(argv);
  const { values } = parseArgs({
    args: rest,
    options: {
      version: { type: 'string' },
      repo: { type: 'string' },
      assets: { type: 'string', multiple: true },
      'docker-image': { type: 'string' },
    },
  });
  if (!values.version) {
    throw new Error('--version is required');
  }
  const assetDirs = values.assets ?? [
    resolve(repoRoot, 'build', 'output', 'nupkg'),
    resolve(repoRoot, 'build', 'output', 'sbom'),
  ];
  return {
    version: values.version,
    repo: values.repo ?? 'TrakHound/MTConnect.NET',
    assetDirs,
    dockerImage: values['docker-image'],
    dryRun,
  };
};

/** Enumerate assets across the requested directories, returning
 *  absolute paths. Recurses one level so `sbom/*.spdx.json` and
 *  `nupkg/*.nupkg` are both picked up without special-casing. */
export const collectAssets = (dirs: string[]): string[] => {
  const files: string[] = [];
  for (const dir of dirs) {
    if (!existsSync(dir)) {
      process.stderr.write(`[gh-release-create] skipping missing dir: ${dir}\n`);
      continue;
    }
    for (const name of readdirSync(dir)) {
      const path = resolve(dir, name);
      if (name.startsWith('.')) continue;
      files.push(path);
    }
  }
  return files;
};

/** Build the release-notes body — a short header plus a
 *  human-readable manifest of every attached asset and the docker
 *  image reference. Kept plain-markdown so the GitHub release page
 *  renders it without extension conversions. */
export const renderReleaseNotes = (
  version: string,
  assets: string[],
  dockerImage: string | undefined,
): string => {
  const lines: string[] = [];
  lines.push(`# MTConnect.NET ${version}`);
  lines.push('');
  lines.push(
    'Automated dev pre-release cut by the CI release pipeline. Not intended for production use — the stable-release cadence still runs through the manual `MTConnect.NET.Builder` flow.',
  );
  lines.push('');
  lines.push('## Assets');
  lines.push('');
  if (assets.length === 0) {
    lines.push('_No assets attached._');
  } else {
    for (const a of assets) {
      lines.push(`- \`${a.split('/').pop()}\``);
    }
  }
  lines.push('');
  if (dockerImage) {
    lines.push('## Docker image');
    lines.push('');
    lines.push('```');
    lines.push(`docker pull ${dockerImage}`);
    lines.push('```');
    lines.push('');
  }
  return lines.join('\n');
};

/** Entry point — write the notes file, then invoke `gh release
 *  create`. */
export const main = async (argv: string[]): Promise<void> => {
  const opts = parseOptions(argv);

  const assets = collectAssets(opts.assetDirs);
  const notes = renderReleaseNotes(opts.version, assets, opts.dockerImage);

  const notesDir = resolve(repoRoot, 'build', 'output', 'release-notes');
  const notesFile = resolve(notesDir, `${opts.version}.md`);
  if (!opts.dryRun) {
    mkdirSync(notesDir, { recursive: true });
    writeFileSync(notesFile, notes, 'utf8');
  }

  const args = [
    'release',
    'create',
    `v${opts.version}`,
    '--repo',
    opts.repo,
    '--title',
    `MTConnect.NET ${opts.version}`,
    '--notes-file',
    notesFile,
    '--prerelease',
    ...assets,
  ];
  await run('gh', args, { dryRun: opts.dryRun, cwd: repoRoot });
};

const invokedDirectly = (() => {
  const entry = process.argv[1];
  if (!entry) return false;
  try {
    return new URL(`file://${entry}`).href === import.meta.url;
  } catch {
    return false;
  }
})();

if (invokedDirectly) {
  main(process.argv.slice(2)).catch((err) => {
    process.stderr.write(`gh-release-create: ${(err as Error).message}\n`);
    process.exit(1);
  });
}
