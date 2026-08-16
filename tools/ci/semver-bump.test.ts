#!/usr/bin/env -S npx tsx
/**
 * Unit tests for the pure functions in `semver-bump.ts`. Run with:
 *
 *   tsx tools/ci/semver-bump.test.ts
 *
 * Exits 0 when every assertion passes, non-zero otherwise. Not wired
 * into a formal test runner (Jest/Vitest) because the module has zero
 * runtime deps in these tests and adding a runner just for one file
 * paid nothing back.
 */

import { strict as assert } from 'node:assert';
import {
  aggregateBump,
  applyBump,
  bumpKindFor,
  isDevTag,
  isStableCutMarker,
} from './semver-bump.ts';

/** Simple test-runner shim — each `test(name, fn)` runs immediately
 *  and prints pass/fail. A failure throws and terminates the script,
 *  so the exit code is 1 on the first miss. */
let passed = 0;
const test = (name: string, fn: () => void): void => {
  fn();
  passed += 1;
  process.stdout.write(`  ok  ${name}\n`);
};

// ─── bumpKindFor ────────────────────────────────────────────────
test('bumpKindFor: feat → minor', () => {
  assert.equal(bumpKindFor('feat: add xyz', ''), 'minor');
  assert.equal(bumpKindFor('feat(agent): add xyz', ''), 'minor');
});

test('bumpKindFor: fix → patch', () => {
  assert.equal(bumpKindFor('fix: correct off-by-one', ''), 'patch');
  assert.equal(bumpKindFor('fix(common): correct', ''), 'patch');
});

test('bumpKindFor: bang → major', () => {
  assert.equal(bumpKindFor('feat!: drop v6 API', ''), 'major');
  assert.equal(bumpKindFor('fix(agent)!: rename field', ''), 'major');
});

test('bumpKindFor: BREAKING CHANGE footer → major', () => {
  assert.equal(
    bumpKindFor('feat: add xyz', 'BREAKING CHANGE: renamed field'),
    'major',
  );
  assert.equal(
    bumpKindFor('feat: add xyz', 'BREAKING-CHANGE: renamed field'),
    'major',
  );
});

test('bumpKindFor: chore/docs/build → patch', () => {
  assert.equal(bumpKindFor('chore: bump deps', ''), 'patch');
  assert.equal(bumpKindFor('docs(agent): describe xyz', ''), 'patch');
  assert.equal(bumpKindFor('build(ci): tweak workflow', ''), 'patch');
});

test('bumpKindFor: non-conventional → none', () => {
  assert.equal(bumpKindFor('WIP', ''), 'none');
  assert.equal(bumpKindFor('add stuff', ''), 'none');
  assert.equal(bumpKindFor('Merge branch xyz', ''), 'none');
});

// ─── aggregateBump ──────────────────────────────────────────────
test('aggregateBump: major beats everything', () => {
  assert.equal(aggregateBump(['patch', 'major', 'minor']), 'major');
});

test('aggregateBump: minor beats patch and none', () => {
  assert.equal(aggregateBump(['patch', 'minor', 'none']), 'minor');
});

test('aggregateBump: patch beats none only', () => {
  assert.equal(aggregateBump(['none', 'patch', 'none']), 'patch');
});

test('aggregateBump: empty → none', () => {
  assert.equal(aggregateBump([]), 'none');
});

// ─── applyBump ──────────────────────────────────────────────────
test('applyBump: major → X+1.0.0', () => {
  assert.equal(applyBump('v6.6.0', 'major'), '7.0.0');
  assert.equal(applyBump('6.6.0', 'major'), '7.0.0');
});

test('applyBump: minor → X.Y+1.0', () => {
  assert.equal(applyBump('v6.6.0', 'minor'), '6.7.0');
});

test('applyBump: patch → X.Y.Z+1', () => {
  assert.equal(applyBump('v6.6.0', 'patch'), '6.6.1');
});

test('applyBump: none → patch bump (avoids version collision)', () => {
  assert.equal(applyBump('v6.6.0', 'none'), '6.6.1');
});

test('applyBump: rejects invalid base', () => {
  assert.throws(() => applyBump('nope', 'minor'), /not a valid semver/);
});

// ─── isStableCutMarker ──────────────────────────────────────────
test('isStableCutMarker: bare marker', () => {
  assert.equal(isStableCutMarker('chore(release): publish new stable'), true);
});

test('isStableCutMarker: with squash-merge PR suffix', () => {
  assert.equal(
    isStableCutMarker('chore(release): publish new stable (#123)'),
    true,
  );
});

test('isStableCutMarker: other chore(release) messages rejected', () => {
  assert.equal(isStableCutMarker('chore(release): tweak wording'), false);
  assert.equal(isStableCutMarker('chore: publish new stable'), false);
});

// ─── isDevTag ───────────────────────────────────────────────────
test('isDevTag: accepts vX.Y.Z-dev.N', () => {
  assert.equal(isDevTag('v6.6.0-dev.42'), true);
  assert.equal(isDevTag('v7.0.0-dev.1'), true);
});

test('isDevTag: rejects stable + rc tags', () => {
  assert.equal(isDevTag('v6.6.0'), false);
  assert.equal(isDevTag('v6.6.0-rc.1'), false);
  assert.equal(isDevTag('v6.6.0-dev.1.2'), false);
});

process.stdout.write(`\n${passed} assertions passed.\n`);
