#:package Mono.Cecil@0.11.6
// ABI DRIFT gate: does every assembly in the app's output still agree with the assemblies it will
// actually be loaded ALONGSIDE?
//
// 🔴 WHY IT EXISTS. SpawnDev.WebTorrent 4.2.7 shipped compiled against SpawnDev.SpawnJS 2.1.7, where
// FileSystemWritableFileStream.Seek took a ulong. SpawnJS 2.1.17 changed it to long. C# source recompiles
// against either, so the bump was source-compatible and every build of every project stayed green - but
// the SHIPPED IL still called Seek(ulong), and NuGet's nearest-wins resolution loaded 2.1.17 next to it.
// The result is a MissingMethodException at runtime, on the one code path that calls it, in the browser,
// with nothing anywhere in the build or the restore reporting a problem.
//
// Nothing else catches this. A build only proves the SOURCE agrees with the pinned version. A restore only
// proves the version RANGES can be satisfied. This is the only check that asks the question that matters:
// take the IL that will run, and the assemblies that will actually be in the folder next to it, and
// resolve every member reference between them.
//
// Every reference is checked, not just the ones some test happens to execute - which is the point, because
// a member reference only fails when its code path runs, and the OPFS writable path that broke here is one
// a shared-worker visitor takes and no dedicated-worker gate does.
//
//   dotnet run tools/check-abi-drift.cs                    the demo's Release build output
//   dotnet run tools/check-abi-drift.cs -- <dir>           any output/publish folder
//   dotnet run tools/check-abi-drift.cs -- <dir> --all     include non-SpawnDev assemblies too
//
// Exits 1 on any unresolved reference, so it is usable as a gate.
using Mono.Cecil;

// ⚠️ Keyed off the WORKING directory, not AppContext.BaseDirectory: a single-file `dotnet run` builds
// into %TEMP%\dotnet\..., so anything relative to the assembly lands nowhere near the repo. The README
// says to run these from the repo root, which is what this assumes.
var dir = args.FirstOrDefault(a => !a.StartsWith("--"))
          ?? Path.Combine(Directory.GetCurrentDirectory(),
                          "SpawnDev.AI.Demo", "bin", "Release", "net10.0");
var all = args.Contains("--all");

dir = Path.GetFullPath(dir);
if (!Directory.Exists(dir))
{
    Console.WriteLine($"[abi] no such directory: {dir}");
    Console.WriteLine("[abi] build the demo first: dotnet build SpawnDev.AI.Demo -c Release");
    return 1;
}

// The candidate set is what will sit in the folder together at runtime. Framework assemblies are excluded
// unless --all: the runtime ships them as a matched set, so drift there is not a thing we can cause.
var files = Directory.GetFiles(dir, "*.dll")
    .Where(f => all || Path.GetFileName(f).StartsWith("SpawnDev.", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

if (files.Count == 0)
{
    Console.WriteLine($"[abi] no assemblies to check in {dir}");
    return 1;
}

Console.WriteLine($"[abi] {dir}");
Console.WriteLine($"[abi] {files.Count} assemblies in the candidate set");
Console.WriteLine();

// Index every member each candidate DEFINES, so a reference can be resolved against it.
var defined = new Dictionary<string, (HashSet<string> Methods, HashSet<string> Fields, HashSet<string> Types)>(
    StringComparer.OrdinalIgnoreCase);

foreach (var f in files)
{
    AssemblyDefinition asm;
    try { asm = AssemblyDefinition.ReadAssembly(f); }
    catch { continue; }   // native or otherwise unreadable - not our concern

    var methods = new HashSet<string>(StringComparer.Ordinal);
    var fields = new HashSet<string>(StringComparer.Ordinal);
    var types = new HashSet<string>(StringComparer.Ordinal);

    void Index(TypeDefinition t)
    {
        types.Add(t.FullName);
        foreach (var m in t.Methods)
            methods.Add(Key(t.FullName, m.Name, m.ReturnType, m.Parameters.Select(p => p.ParameterType)));
        foreach (var fl in t.Fields) fields.Add(t.FullName + "::" + fl.Name);
        foreach (var n in t.NestedTypes) Index(n);
    }
    foreach (var mod in asm.Modules)
        foreach (var t in mod.Types)
            Index(t);

    defined[asm.Name.Name] = (methods, fields, types);
}

var breaks = 0;
var pairsChecked = 0;

foreach (var f in files)
{
    AssemblyDefinition consumer;
    try { consumer = AssemblyDefinition.ReadAssembly(f); }
    catch { continue; }

    // Group what this assembly is missing by the assembly that was supposed to provide it, so the report
    // names the pair that drifted rather than a flat list of symbols.
    var missingByTarget = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    var refCounts = new Dictionary<string, int>(StringComparer.Ordinal);

    // TYPE references first. A member reference only exists where a member is CALLED, so a type that was
    // renamed or removed but is merely named - a field's type, a base class, a parameter type on a method
    // this assembly never invokes - would slip past the member pass and surface as a TypeLoadException
    // instead. Same failure, same invisibility to the build; it just costs a different exception.
    foreach (var tr in consumer.MainModule.GetTypeReferences())
    {
        var t = (TypeReference)tr;
        while (t is GenericInstanceType g) t = g.ElementType;
        var tgt = t.Scope?.Name;
        if (tgt is null || !defined.TryGetValue(tgt, out var tdef)) continue;
        if (string.Equals(tgt, consumer.Name.Name, StringComparison.OrdinalIgnoreCase)) continue;

        refCounts[tgt] = refCounts.GetValueOrDefault(tgt) + 1;
        var full = OpenForm(t.FullName);
        if (!tdef.Types.Contains(full))
        {
            if (!missingByTarget.TryGetValue(tgt, out var s))
                missingByTarget[tgt] = s = new SortedSet<string>(StringComparer.Ordinal);
            s.Add($"TYPE   {full}");
        }
    }

    foreach (var mr in consumer.MainModule.GetMemberReferences())
    {
        var declRef = mr.DeclaringType;
        // A member on a generic instance (Foo`1<int>::Bar) scopes through its element type.
        while (declRef is GenericInstanceType gi) declRef = gi.ElementType;
        var target = declRef?.Scope?.Name;
        if (target is null || !defined.TryGetValue(target, out var def)) continue;
        if (string.Equals(target, consumer.Name.Name, StringComparison.OrdinalIgnoreCase)) continue;

        refCounts[target] = refCounts.GetValueOrDefault(target) + 1;
        var declType = OpenForm(declRef!.FullName);

        if (!def.Types.Contains(declType))
        {
            Add(target, $"TYPE   {declType}");
            continue;
        }

        switch (mr)
        {
            case MethodReference m
                when !def.Methods.Contains(Key(declType, m.Name, m.ReturnType, m.Parameters.Select(p => p.ParameterType))):
                Add(target, $"METHOD {declType}::{m.Name}("
                    + string.Join(", ", m.Parameters.Select(p => Render(p.ParameterType)))
                    + $") -> {Render(m.ReturnType)}");
                break;
            case FieldReference fr when !def.Fields.Contains(declType + "::" + fr.Name):
                Add(target, $"FIELD  {declType}::{fr.Name}");
                break;
        }

        void Add(string t, string s)
        {
            if (!missingByTarget.TryGetValue(t, out var set))
                missingByTarget[t] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(s);
        }
    }

    if (refCounts.Count == 0) continue;
    pairsChecked += refCounts.Count;

    var name = Path.GetFileName(f);
    if (missingByTarget.Count == 0)
    {
        Console.WriteLine($"  ok    {name}  ({refCounts.Values.Sum()} refs across {refCounts.Count} assemblies)");
        continue;
    }

    Console.WriteLine($"  BREAK {name}");
    foreach (var (target, items) in missingByTarget)
    {
        // The version it was COMPILED against versus the version that is actually here - the drift itself.
        var compiledAgainst = consumer.MainModule.AssemblyReferences
            .FirstOrDefault(r => string.Equals(r.Name, target, StringComparison.OrdinalIgnoreCase))?.Version;
        var present = files.Select(p => { try { return AssemblyDefinition.ReadAssembly(p); } catch { return null; } })
            .FirstOrDefault(a => a is not null && string.Equals(a.Name.Name, target, StringComparison.OrdinalIgnoreCase))
            ?.Name.Version;
        Console.WriteLine($"        -> {target}: built against {compiledAgainst}, {present} is present");
        foreach (var s in items) Console.WriteLine($"           {s}");
        breaks += items.Count;
    }
}

Console.WriteLine();
if (breaks == 0)
{
    Console.WriteLine($"[abi] OK - every reference across {pairsChecked} assembly pairs resolves.");
    return 0;
}

Console.WriteLine($"[abi] {breaks} UNRESOLVED REFERENCE(S). Each one is a MissingMethodException waiting for");
Console.WriteLine("[abi] its code path to run - in the browser, at runtime, with no build warning anywhere.");
Console.WriteLine("[abi] Fix = rebuild and republish the consuming package against the version that ships");
Console.WriteLine("[abi] here, then bump the pin (Rule 2: fix the library, never work around it downstream).");
return 1;

// A memberref spells generic parameters positionally (!0, !!0) while the definition spells them by name
// (T, TResult), and a generic instance carries its arguments. Both sides go through this, so the only
// differences left are real signature drift. Without it, every generic call reads as a false break.
static string Render(TypeReference t) => t switch
{
    GenericParameter gp => (gp.Type == GenericParameterType.Method ? "!!" : "!") + gp.Position,
    GenericInstanceType gi => OpenForm(gi.ElementType.FullName) + "<"
                              + string.Join(",", gi.GenericArguments.Select(Render)) + ">",
    ArrayType at => Render(at.ElementType) + "[" + new string(',', at.Rank - 1) + "]",
    ByReferenceType br => Render(br.ElementType) + "&",
    PointerType pt => Render(pt.ElementType) + "*",
    RequiredModifierType rm => Render(rm.ElementType),
    OptionalModifierType om => Render(om.ElementType),
    _ => t.FullName,
};

static string Key(string declType, string name, TypeReference ret, IEnumerable<TypeReference> ps)
    => declType + "::" + name + "(" + string.Join(",", ps.Select(Render)) + ")->" + Render(ret);

static string OpenForm(string full)
{
    var lt = full.IndexOf('<');
    return lt < 0 ? full : full.Substring(0, lt);
}
