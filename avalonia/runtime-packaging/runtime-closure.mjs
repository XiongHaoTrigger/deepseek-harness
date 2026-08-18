#!/usr/bin/env node
/**
 * Verify and repair the packaged dsh Web runtime dependency closure.
 *
 * Commands:
 *   verify <runtimeDir>                 — report every dependency of the closure
 *                                         that is not resolvable inside
 *                                         <runtimeDir>/node_modules and every
 *                                         symlink/junction entry under that
 *                                         directory; exit 1 when either exists.
 *   repair <runtimeDir> <appManifest>   — copy every closure dependency missing
 *                                         from <runtimeDir>/node_modules from
 *                                         the checkout owning <appManifest>
 *                                         (dereferenced, real files), materialize
 *                                         any symlink/junction the deploy left
 *                                         behind, then verify.
 *
 * A dependency is resolved when its directory exists at the top level of
 * <runtimeDir>/node_modules or as a nested node_modules entry of every package
 * that declares it. Only Node builtins (the `node:` prefix or the builtin name
 * set) are exempt. The closure is the breadth-first union of `dependencies`
 * and `peerDependencies` starting from the runtime's own package.json.
 */

import { existsSync, lstatSync, readFileSync, readdirSync, realpathSync, rmSync } from 'node:fs'
import { cpSync, mkdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, join, resolve } from 'node:path'

/** Node builtin module names, plus the prefix form, exempt from closure checks. */
const BUILTINS = new Set([
  'assert', 'async_hooks', 'buffer', 'child_process', 'cluster', 'console', 'constants', 'crypto',
  'dgram', 'diagnostics_channel', 'dns', 'domain', 'events', 'fs', 'http', 'http2', 'https', 'inspector',
  'module', 'net', 'os', 'path', 'perf_hooks', 'process', 'punycode', 'querystring', 'readline', 'repl',
  'sea', 'sqlite', 'stream', 'string_decoder', 'sys', 'test', 'timers', 'tls', 'trace_events', 'tty',
  'url', 'util', 'v8', 'vm', 'wasi', 'worker_threads', 'zlib',
])

/** Whether `name` resolves to a Node builtin rather than a package directory. */
function isBuiltin(name) {
  return name.startsWith('node:') || BUILTINS.has(name) || BUILTINS.has(name.split('/')[0])
}

/**
 * List every installed package directory below a node_modules tree: direct
 * children of each node_modules directory (scoped names included) plus nested
 * node_modules trees inside those packages. Subdirectory manifests that are not
 * installed packages (examples, benchmarks, tests) are excluded.
 */
function collectPackages(runtimeDir) {
  const packages = []
  const visitNodeModules = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      if (entry.name === '.bin' || entry.name === '.pnpm' || !entry.isDirectory()) continue
      const path = join(directory, entry.name)
      if (entry.name.startsWith('@')) {
        for (const sub of readdirSync(path, { withFileTypes: true })) {
          if (!sub.isDirectory()) continue
          const subPath = join(path, sub.name)
          if (existsSync(join(subPath, 'package.json'))) {
            packages.push(subPath)
            visitNested(subPath)
          }
        }
      } else if (existsSync(join(path, 'package.json'))) {
        packages.push(path)
        visitNested(path)
      }
    }
  }
  const visitNested = (packageDir) => {
    const nested = join(packageDir, 'node_modules')
    if (existsSync(nested)) visitNodeModules(nested)
  }
  visitNodeModules(join(runtimeDir, 'node_modules'))
  return packages
}

/** Read one manifest, tolerating a missing or unreadable file. */
function readManifest(path) {
  try {
    return JSON.parse(readFileSync(path, 'utf8'))
  } catch {
    return undefined
  }
}

/** Names a package declares it needs: dependencies plus non-optional peers. */
function declaredDependencies(manifest) {
  const names = []
  if (manifest === undefined) return names
  const dependencies = manifest.dependencies
  if (dependencies !== undefined && typeof dependencies === 'object' && dependencies !== null) {
    names.push(...Object.keys(dependencies))
  }
  const peers = manifest.peerDependencies
  const meta = manifest.peerDependenciesMeta
  if (peers !== undefined && typeof peers === 'object' && peers !== null) {
    for (const name of Object.keys(peers)) {
      if (meta?.[name]?.optional === true) continue
      names.push(name)
    }
  }
  return names
}

/**
 * Resolve one package name from an anchor file path using Node's own
 * node_modules walk, mirroring how the running runtime would find it.
 * @param anchor - path of a file inside the package that imports `name`.
 * @param name - the bare package name.
 * @returns the package directory, or undefined when no search path holds it.
 */
function packageDirFromAnchor(anchor, name) {
  for (const searchPath of createRequire(anchor).resolve.paths(name) ?? []) {
    const candidate = join(searchPath, name)
    if (existsSync(join(candidate, 'package.json'))) return candidate
  }
  return undefined
}

/** The full closure of `appManifestPath` mapped to each package's real directory. */
function repoClosure(appManifestPath) {
  const map = new Map()
  const queue = [appManifestPath]
  for (let next = queue.shift(); next !== undefined; next = queue.shift()) {
    const manifest = readManifest(next)
    for (const name of declaredDependencies(manifest)) {
      if (isBuiltin(name) || map.has(name)) continue
      const dir = packageDirFromAnchor(next, name)
      if (dir === undefined) continue
      map.set(name, realpathSync(dir))
      queue.push(join(map.get(name), 'package.json'))
    }
  }
  return map
}

/** A closure dependency missing from the runtime node_modules. */
class MissingDependency {
  constructor(name, declaredBy) {
    this.name = name
    this.declaredBy = declaredBy
  }
}

/**
 * Compute the runtime closure and list its unresolved dependencies.
 * @param runtimeDir - the packaged runtime directory.
 * @returns the missing-dependency list, each with its declaring package.
 */
function verifyClosure(runtimeDir) {
  const rootManifest = readManifest(join(runtimeDir, 'package.json'))
  const packages = collectPackages(runtimeDir)
  const byDir = new Map(packages.map(dir => [dir, readManifest(join(dir, 'package.json'))]))
  byDir.set(runtimeDir, rootManifest)
  const declaredBy = new Map()
  for (const [dir, manifest] of byDir) {
    for (const name of declaredDependencies(manifest)) {
      if (isBuiltin(name)) continue
      const owners = declaredBy.get(name) ?? []
      owners.push(dir)
      declaredBy.set(name, owners)
    }
  }
  const missing = []
  for (const [name, owners] of declaredBy) {
    for (const owner of owners) {
      const topLevel = existsSync(join(runtimeDir, 'node_modules', name, 'package.json'))
      const nested = existsSync(join(owner, 'node_modules', name, 'package.json'))
      if (!topLevel && !nested) missing.push(new MissingDependency(name, owner))
    }
  }
  return missing
}

/** Replace one symlink or junction with a dereferenced copy of its target. */
function materializeEntry(path) {
  const target = realpathSync(path)
  rmSync(path, { recursive: true, force: true })
  cpSync(target, path, { recursive: true, dereference: true })
}

/**
 * Copy one package directory into the runtime's flat node_modules. The
 * package's own `node_modules` is excluded: dependency resolution in the
 * deployed layout is flat, so nested trees would only duplicate content and
 * drag in the checkout's dev/test packages.
 */
function copyPackage(source, destination) {
  const visit = (from, to) => {
    mkdirSync(to, { recursive: true })
    for (const entry of readdirSync(from, { withFileTypes: true })) {
      if (entry.name === 'node_modules') continue
      const sourcePath = join(from, entry.name)
      const destinationPath = join(to, entry.name)
      const stat = lstatSync(sourcePath)
      if (stat.isSymbolicLink()) {
        cpSync(sourcePath, destinationPath, { recursive: true, dereference: true })
      } else if (stat.isDirectory()) {
        visit(sourcePath, destinationPath)
      } else {
        cpSync(sourcePath, destinationPath)
      }
    }
  }
  visit(source, destination)
}

/** Remove every symlink or junction below a node_modules tree, plus .bin shims. */
function materializeTree(directory) {
  const materialized = []
  const visit = (current) => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      if (entry.name === '.bin') {
        rmSync(join(current, '.bin'), { recursive: true, force: true })
        continue
      }
      const path = join(current, entry.name)
      const stat = lstatSync(path)
      if (stat.isSymbolicLink()) {
        materializeEntry(path)
        materialized.push(path)
        continue
      }
      if (stat.isDirectory()) visit(path)
    }
  }
  visit(directory)
  return materialized
}

/** Every symlink or junction below a node_modules tree. */
function findLinks(directory) {
  const links = []
  const visit = (current) => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name)
      if (lstatSync(path).isSymbolicLink()) links.push(path)
      else if (entry.isDirectory()) visit(path)
    }
  }
  visit(directory)
  return links
}

function usage() {
  return [
    'Usage:',
    '  node runtime-closure.mjs verify <runtimeDir>',
    '  node runtime-closure.mjs repair <runtimeDir> <appManifest>',
  ].join('\n')
}

const [command, runtimeArg, appArg] = process.argv.slice(2)

if (command === 'verify') {
  const runtimeDir = resolve(runtimeArg ?? '')
  const missing = verifyClosure(runtimeDir)
  const links = findLinks(join(runtimeDir, 'node_modules'))
  if (missing.length === 0 && links.length === 0) {
    console.log(`runtime-closure: ok (${runtimeDir})`)
    process.exit(0)
  }
  for (const item of missing) {
    console.error(`runtime-closure: missing ${item.name} (declared by ${item.declaredBy})`)
  }
  for (const link of links) {
    console.error(`runtime-closure: symlink or junction ${link} (target ${realpathSync(link)})`)
  }
  process.exit(1)
}

if (command === 'repair') {
  const runtimeDir = resolve(runtimeArg ?? '')
  const appManifest = resolve(appArg ?? '')
  if (!existsSync(appManifest)) {
    console.error(`runtime-closure: app manifest ${appManifest} does not exist`)
    process.exit(1)
  }
  const closure = repoClosure(appManifest)
  const missing = verifyClosure(runtimeDir)
  const repaired = []
  for (const item of missing) {
    const source = closure.get(item.name)
    if (source === undefined) {
      console.error(`runtime-closure: cannot resolve ${item.name} (declared by ${item.declaredBy}) from the checkout`)
      process.exit(1)
    }
    const destination = join(runtimeDir, 'node_modules', item.name)
    mkdirSync(dirname(destination), { recursive: true })
    copyPackage(source, destination)
    repaired.push(item.name)
    console.log(`runtime-closure: repaired ${item.name} <- ${source}`)
  }
  const materialized = materializeTree(join(runtimeDir, 'node_modules'))
  for (const path of materialized) console.log(`runtime-closure: materialized ${path}`)
  const stillMissing = verifyClosure(runtimeDir)
  if (stillMissing.length > 0) {
    for (const item of stillMissing) console.error(`runtime-closure: still missing ${item.name}`)
    process.exit(1)
  }
  console.log(`runtime-closure: repair complete (${repaired.length} repaired, ${materialized.length} materialized)`)
  process.exit(0)
}

console.error(usage())
process.exit(2)
