#!/usr/bin/env -S npx tsx
/**
 * Push every `.nupkg` in a directory to a NuGet feed.
 *
 * Ported from `build/MTConnect.NET.Builder/Parts/libraries/Nuget.cs`
 * (Publish command). Uses a classic NuGet API key rather than OIDC —
 * the switch to OIDC-signed publishes is a follow-up (Patrick asked
 * to defer signing entirely for phase 1).
 *
 * Usage:
 *   tsx tools/release/nuget-push.ts --input <dir> [--source <url>]
 *                                   [--api-key <key>] [--dry-run]
 *
 * When `--api-key` is omitted the script reads `NUGET_API_KEY` from
 * the environment; a missing key throws unless `--dry-run` is set.
 */

import { readdirSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { optionalEnv, parseDryRun, run } from './shell.ts';

/** Repo root — used to default `--input` to the Pack script's output. */
const repoRoot = resolve(new URL('../../', import.meta.url).pathname);

/** CLI options. */
export type Options = {
  input: string;
  source: string;
  apiKey: string | undefined;
  dryRun: boolean;
};

/** Parse argv into strongly-typed `Options`. */
export const parseOptions = (argv: string[]): Options => {
  const { dryRun, rest } = parseDryRun(argv);
  const { values } = parseArgs({
    args: rest,
    options: {
      input: { type: 'string' },
      source: { type: 'string' },
      'api-key': { type: 'string' },
    },
  });
  return {
    input: values.input ?? resolve(repoRoot, 'build', 'output', 'nupkg'),
    source: values.source ?? 'https://api.nuget.org/v3/index.json',
    apiKey: values['api-key'] ?? optionalEnv('NUGET_API_KEY'),
    dryRun,
  };
};

/** Push each .nupkg in the input directory sequentially. Symbol packages
 *  (`.snupkg`) are skipped from the explicit iteration because `dotnet
 *  nuget push` automatically pushes the matching symbol package alongside
 *  its parent .nupkg when both live in the same directory. */
export const main = async (argv: string[]): Promise<void> => {
  const opts = parseOptions(argv);

  if (!existsSync(opts.input)) {
    throw new Error(`Input directory not found: ${opts.input}`);
  }
  if (!opts.apiKey && !opts.dryRun) {
    throw new Error(
      'NuGet API key not provided. Set NUGET_API_KEY or pass --api-key. Use --dry-run to inspect commands only.',
    );
  }

  const packages = readdirSync(opts.input).filter((f) => f.endsWith('.nupkg'));
  if (packages.length === 0) {
    throw new Error(`No .nupkg files found in ${opts.input}`);
  }

  for (const pkg of packages) {
    const path = resolve(opts.input, pkg);
    const args = [
      'nuget',
      'push',
      path,
      '--source',
      opts.source,
      '--skip-duplicate',
    ];
    if (opts.apiKey) {
      args.push('--api-key', opts.apiKey);
    }
    await run('dotnet', args, { dryRun: opts.dryRun });
  }
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
    process.stderr.write(`nuget-push: ${(err as Error).message}\n`);
    process.exit(1);
  });
}
