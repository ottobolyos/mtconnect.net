using MTConnect.SysML;
using MTConnect.SysML.CSharp;
using MTConnect.SysML.Json_cppagent;
using MTConnect.SysML.Xml;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// SysML importer entry point. Runs on Linux / macOS / Windows / CI.
//
// Usage:
//     dotnet run --project build/MTConnect.NET-SysML-Import \
//         -- --xmi <path-to-MTConnectSysMLModel.xml> \
//            --output <repo-root> \
//            [--previous-xmi <path-to-prior-XMI>] \
//            [--compat-version-label <label>] \
//            [--json-dump <path>]
//
// Flags:
//   --xmi <path>            SysML XMI file to consume. Required.
//   --output <path>         Repository root. Each subgenerator writes into its
//                           own libraries/<LibraryName>/ subtree under this
//                           root. Required.
//   --previous-xmi <path>   Opt-in delta-driven mode. When supplied, the
//                           generator renders both XMIs to scratch directories,
//                           diffs the emitted trees at the file level, and
//                           writes only the .g.cs files whose content changed
//                           between the two — plus a per-library
//                           Compat/<label>.g.cs file that concentrates every
//                           unchanged .g.cs file into a single re-emit surface
//                           (per plan D4). Files present in --previous-xmi's
//                           tree but absent from --xmi's tree (REMOVED types)
//                           are dropped. Files present in --xmi's tree but
//                           absent from --previous-xmi's tree (ADDED types) are
//                           written normally.
//   --compat-version-label <label>
//                           Label used for the Compat/<label>.g.cs file name in
//                           delta mode. Defaults to "Previous". Ignored unless
//                           --previous-xmi is supplied.
//   --json-dump <path>      Optional. Writes the parsed MTConnectModel as JSON
//                           for debugging.
//
// See build/MTConnect.NET-SysML-Import/README.md for the full usage guide,
// the "Adding a new MTConnect Standard version" runbook, the determinism
// guarantee (regen against a pinned XMI tag must produce zero diff), and the
// delta-mode design notes (plan D4 — partial-class re-emit, not
// [TypeForwardedTo]).

string? xmiPath = null;
string? previousXmiPath = null;
string? compatVersionLabel = "Previous";
string? outputRoot = null;
string? jsonDumpPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--xmi":
            xmiPath = RequireValue(args, ref i, "--xmi");
            break;
        case "--previous-xmi":
            previousXmiPath = RequireValue(args, ref i, "--previous-xmi");
            break;
        case "--compat-version-label":
            compatVersionLabel = RequireValue(args, ref i, "--compat-version-label");
            break;
        case "--output":
            outputRoot = RequireValue(args, ref i, "--output");
            break;
        case "--json-dump":
            jsonDumpPath = RequireValue(args, ref i, "--json-dump");
            break;
        case "-h":
        case "--help":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintHelp();
            return 2;
    }
}

if (string.IsNullOrEmpty(xmiPath))
{
    Console.Error.WriteLine("error: --xmi <path> is required.");
    PrintHelp();
    return 2;
}

if (!File.Exists(xmiPath))
{
    Console.Error.WriteLine($"error: XMI file not found: {xmiPath}");
    return 1;
}

if (previousXmiPath is not null && !File.Exists(previousXmiPath))
{
    Console.Error.WriteLine($"error: --previous-xmi file not found: {previousXmiPath}");
    return 1;
}

// --compat-version-label flows through Path.Combine(compatDir, $"{label}.g.cs")
// and must produce a safe, contained filename on every host. Reject anything
// that would escape the Compat/ directory (path-separator sequences), select
// a legal but surprising target (drive letters, NUL bytes, ASCII controls),
// or produce a hidden dotfile. The default value "Previous" always passes.
if (!IsSafeCompatLabel(compatVersionLabel))
{
    Console.Error.WriteLine(
        $"error: --compat-version-label value '{compatVersionLabel}' is not a safe filename. " +
        "Allowed shape: 1 to 64 characters, ASCII letters / digits / '_' / '-' / '.', no leading dot.");
    return 2;
}

if (string.IsNullOrEmpty(outputRoot))
{
    Console.Error.WriteLine("error: --output <path> is required.");
    PrintHelp();
    return 2;
}

if (!Directory.Exists(outputRoot))
{
    Console.Error.WriteLine($"error: Output root not found: {outputRoot}");
    return 1;
}

Console.WriteLine($"XMI:    {xmiPath}");
if (previousXmiPath is not null)
{
    Console.WriteLine($"Prev:   {previousXmiPath}");
    Console.WriteLine($"Label:  {compatVersionLabel}");
}
Console.WriteLine($"Output: {outputRoot}");
if (jsonDumpPath is not null)
    Console.WriteLine($"JSON:   {jsonDumpPath}");

if (jsonDumpPath is not null)
{
    var mtconnectModelForDump = MTConnectModel.Parse(xmiPath);
    if (mtconnectModelForDump == null)
    {
        Console.Error.WriteLine($"error: Failed to parse XMI: {xmiPath}");
        return 1;
    }
    RenderJsonFile(mtconnectModelForDump, jsonDumpPath);
}

if (previousXmiPath is null)
{
    // Full-tree mode (default). Preserves the pre-Phase-4 behaviour bit-for-bit.
    var mtconnectModel = MTConnectModel.Parse(xmiPath);
    if (mtconnectModel == null)
    {
        // Fail fast on a null model. The renderers below internally null-check
        // and silently no-op, producing zero output and exit 0. Surface the parse
        // failure here so the operator gets a proper non-zero exit + stderr.
        Console.Error.WriteLine($"error: Failed to parse XMI: {xmiPath}");
        return 1;
    }
    Console.WriteLine($"Model parsed: type={mtconnectModel.GetType().Name}");

    Console.WriteLine("Rendering C# common classes...");
    RenderCommonClasses(mtconnectModel, outputRoot);
    Console.WriteLine("Rendering JSON-cppagent formatters...");
    RenderJsonComponents(mtconnectModel, outputRoot);
    Console.WriteLine("Rendering XML formatters...");
    RenderXmlComponents(mtconnectModel, outputRoot);
    Console.WriteLine("Done.");
    return 0;
}

// Delta mode. Render both XMIs into isolated scratch directories, then diff at
// the file level and emit only the changed/added files into outputRoot,
// concentrating unchanged files into Compat/<label>.g.cs per library.
return RenderDelta(xmiPath, previousXmiPath, outputRoot, compatVersionLabel!);


static string RequireValue(string[] argv, ref int index, string flag)
{
    index++;
    if (index >= argv.Length)
        throw new ArgumentException($"Flag '{flag}' requires a value.");
    return argv[index];
}

// Validates that --compat-version-label produces a safe on-disk filename inside
// the per-library Compat/ directory. Rejects: null / empty / whitespace, any
// path separator or drive letter, ASCII control chars, leading dots (hidden
// files), and lengths outside 1..64 chars. The default "Previous" always
// passes; typical spec-labels like "v2_6" / "v2.5-rc3" / "PriorSpec" pass;
// hostile inputs like "../../etc/passwd" or "Compat/../secret" reject at
// argument-parse time.
static bool IsSafeCompatLabel(string? label)
{
    if (string.IsNullOrWhiteSpace(label)) return false;
    if (label.Length > 64) return false;
    return Regex.IsMatch(label, @"^[A-Za-z0-9_\-][A-Za-z0-9_\-.]*$");
}

static void PrintHelp()
{
    Console.WriteLine("""
        MTConnect.NET SysML Importer

        Usage:
          dotnet run --project build/MTConnect.NET-SysML-Import -- \
            --xmi <path-to-MTConnectSysMLModel.xml> \
            --output <repo-root> \
            [--previous-xmi <path-to-prior-XMI>] \
            [--compat-version-label <label>] \
            [--json-dump <path>]

        See build/MTConnect.NET-SysML-Import/README.md for the full guide.
        """);
}

static void RenderJsonFile(MTConnectModel model, string path)
{
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // --json-dump is operator-trusted (no path-traversal guard); echo the
    // resolved absolute path so the operator can verify exactly where the
    // dump landed when running with a relative path or a sibling-clone
    // launchSettings profile.
    var resolved = Path.GetFullPath(path);
    Console.WriteLine($"JSON dump: writing to {resolved}");

    var dir = Path.GetDirectoryName(resolved);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(model, options: jsonOptions);
    File.WriteAllText(resolved, json);
}

static void RenderCommonClasses(MTConnectModel model, string outputRoot)
{
    var outputPath = Path.Combine(outputRoot, "libraries", "MTConnect.NET-Common");
    if (!Directory.Exists(outputPath))
        throw new DirectoryNotFoundException($"MTConnect.NET-Common not found under output root: {outputPath}");

    CSharpTemplateRenderer.Render(model, outputPath);
}

static void RenderJsonComponents(MTConnectModel model, string outputRoot)
{
    var outputPath = Path.Combine(outputRoot, "libraries", "MTConnect.NET-JSON-cppagent");
    if (!Directory.Exists(outputPath))
        throw new DirectoryNotFoundException($"MTConnect.NET-JSON-cppagent not found under output root: {outputPath}");

    JsonCppAgentTemplateRenderer.Render(model, outputPath);
}

static void RenderXmlComponents(MTConnectModel model, string outputRoot)
{
    var outputPath = Path.Combine(outputRoot, "libraries", "MTConnect.NET-XML");
    if (!Directory.Exists(outputPath))
        throw new DirectoryNotFoundException($"MTConnect.NET-XML not found under output root: {outputPath}");

    XmlTemplateRenderer.Render(model, outputPath);
}

// Delta-driven regen. Renders both --xmi and --previous-xmi to isolated
// scratch trees, then emits the diff:
//
//   - Files present only in the NEW tree (ADDED) → written to outputRoot.
//   - Files present in both trees with different bytes (CHANGED) → written
//     to outputRoot (NEW tree's version).
//   - Files present only in the PREV tree (REMOVED) → DELETED from outputRoot
//     so the type stops shipping (the spec dropped it).
//   - Files present in both trees with identical bytes (UNCHANGED) →
//     concentrated into Compat/<label>.g.cs per library, per plan D4. The
//     individual .g.cs at outputRoot is DELETED so the Compat file is the
//     sole namespace host (avoids CS0101 duplicate-type errors when the
//     delta runs against a repo already carrying the committed .g.cs tree,
//     which is the realistic use case).
//
// The Compat file concatenates the unchanged files' bodies verbatim (each
// still carries its own `namespace X { ... }` block, so multi-namespace
// concentration is legal C#). Byte-identity of unchanged files is preserved,
// satisfying the Phase 4.1 invariant that Compat/<PrevVersion>.g.cs is
// `git diff`-clean against the deterministically-emitted full-tree output.
static int RenderDelta(string newXmiPath, string prevXmiPath, string outputRoot, string compatLabel)
{
    var scratchRoot = Path.Combine(Path.GetTempPath(), $"mtc-sysml-delta-{Guid.NewGuid():N}");
    var prevScratch = Path.Combine(scratchRoot, "prev");
    var newScratch = Path.Combine(scratchRoot, "new");

    try
    {
        Console.WriteLine($"Delta scratch: {scratchRoot}");
        Console.WriteLine("Rendering previous-XMI full tree to scratch...");
        RenderFullTreeToScratch(prevXmiPath, prevScratch);
        Console.WriteLine("Rendering new-XMI full tree to scratch...");
        RenderFullTreeToScratch(newXmiPath, newScratch);

        Console.WriteLine("Diffing scratch trees and emitting delta...");
        var stats = EmitDelta(prevScratch, newScratch, outputRoot, compatLabel);

        Console.WriteLine(
            $"Delta emission: added={stats.Added}, changed={stats.Changed}, " +
            $"unchanged-concentrated={stats.UnchangedConcentrated}, removed-skipped={stats.RemovedSkipped}, " +
            $"compat-files-written={stats.CompatFilesWritten}");
        Console.WriteLine("Done.");
        return 0;
    }
    finally
    {
        if (Directory.Exists(scratchRoot))
        {
            try { Directory.Delete(scratchRoot, recursive: true); }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"warning: could not clean scratch {scratchRoot}: {ex.Message}");
            }
        }
    }
}

// Runs the full-tree renderer pipeline against `xmiPath`, writing every
// `.g.cs` artefact into <scratchRoot>/libraries/<library>/... Mirrors the
// full-tree branch above so the emitted bytes are byte-identical to what
// the operator would get from `--xmi <xmiPath> --output <scratchRoot>` with
// no `--previous-xmi`.
static void RenderFullTreeToScratch(string xmiPath, string scratchRoot)
{
    Directory.CreateDirectory(scratchRoot);
    Directory.CreateDirectory(Path.Combine(scratchRoot, "libraries", "MTConnect.NET-Common"));
    Directory.CreateDirectory(Path.Combine(scratchRoot, "libraries", "MTConnect.NET-JSON-cppagent"));
    Directory.CreateDirectory(Path.Combine(scratchRoot, "libraries", "MTConnect.NET-XML"));

    var model = MTConnectModel.Parse(xmiPath);
    if (model == null)
        throw new InvalidOperationException($"Failed to parse XMI: {xmiPath}");

    RenderCommonClasses(model, scratchRoot);
    RenderJsonComponents(model, scratchRoot);
    RenderXmlComponents(model, scratchRoot);
}

// File-level diff between prev and new scratch trees, emitting the delta into
// outputRoot. Returns per-category counts for the console summary.
static DeltaStats EmitDelta(string prevScratch, string newScratch, string outputRoot, string compatLabel)
{
    var stats = new DeltaStats();

    // Iterate per library so each library gets its own Compat/<label>.g.cs.
    string[] libraries = { "MTConnect.NET-Common", "MTConnect.NET-JSON-cppagent", "MTConnect.NET-XML" };
    foreach (var library in libraries)
    {
        var prevLibrary = Path.Combine(prevScratch, "libraries", library);
        var newLibrary = Path.Combine(newScratch, "libraries", library);
        var outputLibrary = Path.Combine(outputRoot, "libraries", library);

        if (!Directory.Exists(outputLibrary))
            throw new DirectoryNotFoundException($"{library} not found under output root: {outputLibrary}");

        var prevFiles = EnumerateGeneratedFiles(prevLibrary);
        var newFiles = EnumerateGeneratedFiles(newLibrary);

        var compatBody = new StringBuilder();
        var compatFileCount = 0;

        foreach (var relativePath in newFiles.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var newContent = newFiles[relativePath];
            if (prevFiles.TryGetValue(relativePath, out var prevContent))
            {
                if (ByteEquals(prevContent, newContent))
                {
                    // UNCHANGED — concentrate into Compat file.
                    if (compatFileCount == 0)
                    {
                        compatBody.AppendLine("// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.");
                        compatBody.AppendLine("// TrakHound Inc. licenses this file to you under the MIT license.");
                        compatBody.AppendLine();
                        compatBody.AppendLine("// Compat re-emit: concentrates every .g.cs file whose content did NOT change");
                        compatBody.AppendLine("// between --previous-xmi and --xmi. Each block carries its own namespace,");
                        compatBody.AppendLine("// so multi-namespace concentration is legal C#. Byte-identical to the");
                        compatBody.AppendLine("// full-tree emission for every included type (plan D4).");
                        compatBody.AppendLine();
                    }
                    compatBody.AppendLine($"// --- from {relativePath.Replace('\\', '/')} ---");
                    compatBody.Append(System.Text.Encoding.UTF8.GetString(newContent));
                    if (newContent.Length == 0 || newContent[^1] != (byte)'\n')
                        compatBody.AppendLine();
                    compatBody.AppendLine();
                    compatFileCount++;
                    stats.UnchangedConcentrated++;

                    // Concentration replaces the individual file. If the operator ran
                    // the delta against an outputRoot that already carried the
                    // committed .g.cs tree (the realistic use case — the repo root),
                    // leaving the individual file in place would produce CS0101
                    // duplicate-type errors when the Compat file re-emits the same
                    // namespaces. Delete pre-existing individual .g.cs so Compat is
                    // the single source for UNCHANGED types.
                    DeleteIfExists(Path.Combine(outputLibrary, relativePath));
                }
                else
                {
                    // CHANGED — emit new-tree version at its normal location.
                    WriteFile(Path.Combine(outputLibrary, relativePath), newContent);
                    stats.Changed++;
                }
            }
            else
            {
                // ADDED — emit new-tree version at its normal location.
                WriteFile(Path.Combine(outputLibrary, relativePath), newContent);
                stats.Added++;
            }
        }

        // REMOVED types (present in prev tree, absent from new tree). Delete the
        // stale individual file from outputRoot so it stops shipping in the
        // library; the type is intentionally gone from the new spec version.
        foreach (var relativePath in prevFiles.Keys)
        {
            if (!newFiles.ContainsKey(relativePath))
            {
                DeleteIfExists(Path.Combine(outputLibrary, relativePath));
                stats.RemovedSkipped++;
            }
        }

        if (compatFileCount > 0)
        {
            var compatDir = Path.Combine(outputLibrary, "Compat");
            Directory.CreateDirectory(compatDir);
            var compatPath = Path.Combine(compatDir, $"{compatLabel}.g.cs");
            File.WriteAllText(compatPath, compatBody.ToString());
            stats.CompatFilesWritten++;
        }
    }

    return stats;
}

// Enumerates every .g.cs file under `root` and returns a dictionary keyed by
// the forward-slash-normalised path relative to `root`, with the raw file
// bytes as value. Ordinal-key comparer keeps cross-platform behaviour
// consistent (Linux CI vs. Windows local).
static Dictionary<string, byte[]> EnumerateGeneratedFiles(string root)
{
    var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    if (!Directory.Exists(root))
        return result;

    foreach (var path in Directory.EnumerateFiles(root, "*.g.cs", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        result[relative] = File.ReadAllBytes(path);
    }
    return result;
}

static bool ByteEquals(byte[] a, byte[] b)
    => ((ReadOnlySpan<byte>)a).SequenceEqual(b);

static void WriteFile(string path, byte[] contents)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);
    File.WriteAllBytes(path, contents);
}

// Idempotent delete. Missing files no-op; existing files delete without following
// symlinks (System.IO.File.Delete on .NET removes the link, not the target).
static void DeleteIfExists(string path)
{
    if (File.Exists(path))
        File.Delete(path);
}

// Per-run counters emitted at the tail of a delta invocation. Every category
// is reported so the operator can spot a regression at a glance (a spec bump
// that renames a type surfaces as {changed:1, removed-skipped:1, added:1};
// whereas a spec bump that only adds enum arms surfaces as {changed:1,
// unchanged-concentrated:N-1, removed-skipped:0, added:0}).
internal sealed class DeltaStats
{
    public int Added { get; set; }
    public int Changed { get; set; }
    public int UnchangedConcentrated { get; set; }
    public int RemovedSkipped { get; set; }
    public int CompatFilesWritten { get; set; }
}
