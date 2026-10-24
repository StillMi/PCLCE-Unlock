using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.NET.HostModel.Bundle;

// Binary patch for the official PCL CE single-file bundle.
//
// Neutralizes PCL.RestrictedFeaturePolicy - the mainland-China region gate that blocks
// Minecraft download / install / repair / modpack install - by replacing three method
// bodies with constant-return stubs, in place, without repacking the bundle.
//
// The bundle is not compressed (flags=0) and entry sizes are unchanged, so the output
// file has exactly the same size and layout as the input.
//
// Patched methods (code size preserved):
//   get_IsRestricted   -> ldc.i4.0 ; nop... ; ret      (IsRestricted == false)
//   Check(string,bool) -> ldc.i4.1 ; nop... ; ret      (every gated feature allowed)
//   ThrowIfRestricted  -> nop...   ; ret               (never throws)
//
// IMPORTANT: the last IL byte must be a terminating instruction. .NET rejects a method
// body whose final block has no terminator, so nop padding goes *before* the final ret.
//
// usage: BundlePatch <input.exe> <output.exe> [--dry-run]

const string TargetAssembly = "Plain Craft Launcher 2.dll";
const string TargetNamespace = "PCL";
const string TargetType = "RestrictedFeaturePolicy";
const byte OpRet = 0x2A;
const byte OpNop = 0x00;

if (args.Length < 2)
{
    Console.WriteLine("usage: BundlePatch <input.exe> <output.exe> [--dry-run]");
    return 1;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var dryRun = args.Contains("--dry-run");

var exe = File.ReadAllBytes(inputPath);
Console.WriteLine($"input      : {inputPath}");
Console.WriteLine($"size       : {exe.Length:N0} bytes");
Console.WriteLine($"sha256(in) : {Convert.ToHexString(SHA256.HashData(exe))}");
Console.WriteLine();

if (!Bundler.IsBundle(inputPath, out var headerOffset))
{
    Console.WriteLine("ERROR: input is not a .NET single-file bundle");
    return 2;
}

var entries = ParseBundle(exe, headerOffset);
var matches = entries.Where(e => string.Equals(e.Path, TargetAssembly, StringComparison.OrdinalIgnoreCase)).ToList();
if (matches.Count != 1) throw new InvalidOperationException($"expected one bundle entry '{TargetAssembly}', found {matches.Count}");
var target = matches[0];

Console.WriteLine($"bundle     : version {target.BundleVersion}, {entries.Count} files, header @ {headerOffset:N0}");
Console.WriteLine($"assembly   : {target.Path} offset={target.Offset:N0} size={target.Size:N0}");
Console.WriteLine();

var assembly = new byte[target.Size];
Array.Copy(exe, target.Offset, assembly, 0, target.Size);

// method -> IL prologue (pushed constant); padding is nop, terminator is ret
var patches = new List<(string Method, byte[] Prologue)>
{
    ("get_IsRestricted", [0x16]),   // ldc.i4.0
    ("Check", [0x17]),              // ldc.i4.1
    ("ThrowIfRestricted", [])       // nothing to push
};

var resolved = ResolveMethods(assembly, patches.Select(p => p.Method).ToArray());
Console.WriteLine("--- original IL ---");
var absolutePatches = new List<(long ExeOffset, int CodeSize, byte[] NewIL, string Method)>();

foreach (var (methodName, prologue) in patches)
{
    var loc = resolved[methodName];
    var il = new byte[loc.CodeSize];
    Array.Copy(assembly, loc.CodeOffset, il, 0, loc.CodeSize);
    Console.WriteLine($"{methodName,-20} rva=0x{loc.Rva:X6} fileOff=0x{loc.CodeOffset:X6} codeSize={loc.CodeSize,-4} IL={Convert.ToHexString(il)}");

    if (loc.CodeSize < prologue.Length + 1)
        throw new InvalidOperationException($"code size {loc.CodeSize} too small for patch of {methodName}");
    if (il[^1] is not (OpRet or 0x7A))
        throw new InvalidOperationException($"unexpected method body (no ret/throw terminator) for {methodName}: {Convert.ToHexString(il)}");
    if (loc.HasExceptionHandlers)
        throw new InvalidOperationException($"method {methodName} has exception handling clauses; refusing to patch IL");

    var patched = BuildStub(loc.CodeSize, prologue);
    absolutePatches.Add((target.Offset + loc.CodeOffset, loc.CodeSize, patched, methodName));
}

Console.WriteLine();
if (dryRun)
{
    Console.WriteLine("dry run: no file written");
    return 0;
}

var output = (byte[])exe.Clone();
foreach (var (exeOffset, codeSize, newIL, method) in absolutePatches)
{
    Array.Copy(newIL, 0, output, exeOffset, codeSize);
    Console.WriteLine($"patched    : {method} @ exe offset 0x{exeOffset:X6} ({codeSize} bytes)");
}

File.WriteAllBytes(outputPath, output);
Console.WriteLine();
Console.WriteLine($"output     : {outputPath}");
Console.WriteLine($"size       : {output.Length:N0} bytes (unchanged: {output.Length == exe.Length})");
Console.WriteLine($"sha256(out): {Convert.ToHexString(SHA256.HashData(output))}");

// ---- verify by reloading the written bundle -------------------------------
Console.WriteLine();
Console.WriteLine("--- verify (re-read output) ---");
if (!Bundler.IsBundle(outputPath, out var h2)) throw new InvalidOperationException("output is not a bundle");
var outBytes = File.ReadAllBytes(outputPath);
var entries2 = ParseBundle(outBytes, h2);
if (entries2.Count != entries.Count) throw new InvalidOperationException("bundle entry count changed");
var t2 = entries2.First(e => string.Equals(e.Path, TargetAssembly, StringComparison.OrdinalIgnoreCase));
if (t2.Offset != target.Offset || t2.Size != target.Size) throw new InvalidOperationException("assembly entry moved");

var asm2 = new byte[t2.Size];
Array.Copy(outBytes, t2.Offset, asm2, 0, t2.Size);
var resolved2 = ResolveMethods(asm2, patches.Select(p => p.Method).ToArray());
var allOk = true;
foreach (var (method, prologue) in patches)
{
    var loc = resolved2[method];
    var actual = new byte[loc.CodeSize];
    Array.Copy(asm2, loc.CodeOffset, actual, 0, loc.CodeSize);
    var expected = BuildStub(loc.CodeSize, prologue);
    var ok = actual.SequenceEqual(expected);
    allOk &= ok;
    Console.WriteLine($"{method,-20} {(ok ? "OK  " : "FAIL")} IL={Convert.ToHexString(actual)}");
}
Console.WriteLine(allOk ? "VERIFY: PASS" : "VERIFY: FAIL");
return allOk ? 0 : 3;

// --------------------------------------------------------------------------
// Stub layout: prologue + nop padding + final ret (terminator required).
static byte[] BuildStub(int codeSize, byte[] prologue)
{
    var il = new byte[codeSize];
    Array.Copy(prologue, il, prologue.Length);
    for (var i = prologue.Length; i < codeSize - 1; i++) il[i] = OpNop;
    il[codeSize - 1] = OpRet;
    return il;
}

static Dictionary<string, MethodLoc> ResolveMethods(byte[] assembly, string[] methodNames)
{
    using var ms = new MemoryStream(assembly, writable: false);
    using var pe = new PEReader(ms);
    var md = pe.GetMetadataReader();

    TypeDefinitionHandle? typeHandle = null;
    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        if (md.GetString(td.Namespace) == TargetNamespace && md.GetString(td.Name) == TargetType)
        {
            typeHandle = th;
            break;
        }
    }
    if (typeHandle is null) throw new InvalidOperationException($"type {TargetNamespace}.{TargetType} not found");

    var result = new Dictionary<string, MethodLoc>(StringComparer.Ordinal);
    var td2 = md.GetTypeDefinition(typeHandle.Value);
    foreach (var mh in td2.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var name = md.GetString(m.Name);
        if (!methodNames.Contains(name)) continue;

        var rva = m.RelativeVirtualAddress;
        if (rva == 0) throw new InvalidOperationException($"method {name} has no body");
        var sectionIndex = pe.PEHeaders.GetContainingSectionIndex(rva);
        if (sectionIndex < 0) throw new InvalidOperationException($"method {name}: rva 0x{rva:X} not in any section");
        var section = pe.PEHeaders.SectionHeaders[sectionIndex];
        var bodyOffset = section.PointerToRawData + (rva - section.VirtualAddress);

        var b0 = assembly[bodyOffset];
        int headerSize, codeSize;
        var hasEh = false;
        if ((b0 & 0x3) == 0x2)
        {
            headerSize = 1;
            codeSize = b0 >> 2;
        }
        else if ((b0 & 0x3) == 0x3)
        {
            var flags = BitConverter.ToUInt16(assembly, bodyOffset);
            headerSize = ((flags >> 12) & 0xF) * 4;
            codeSize = BitConverter.ToInt32(assembly, bodyOffset + 4);
            hasEh = (flags & 0x8) != 0;
        }
        else throw new InvalidOperationException($"method {name}: bad IL header 0x{b0:X2}");

        result[name] = new MethodLoc(rva, bodyOffset + headerSize, codeSize, hasEh);
    }

    foreach (var n in methodNames)
        if (!result.ContainsKey(n)) throw new InvalidOperationException($"method {TargetNamespace}.{TargetType}.{n} not found");
    return result;
}

static List<BundleEntry> ParseBundle(byte[] bytes, long headerOffset)
{
    var list = new List<BundleEntry>();
    using var ms = new MemoryStream(bytes, (int)headerOffset, bytes.Length - (int)headerOffset, writable: false);
    using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
    var major = br.ReadUInt32();
    var minor = br.ReadUInt32();
    var count = br.ReadInt32();
    var version = $"{major}.{minor}";
    Read7BitString(br);
    br.ReadInt64(); br.ReadInt64(); br.ReadInt64(); br.ReadInt64(); br.ReadUInt64();
    for (var i = 0; i < count; i++)
    {
        var offset = br.ReadInt64();
        var size = br.ReadInt64();
        br.ReadInt64(); // compressedSize
        var type = br.ReadByte();
        list.Add(new BundleEntry(offset, size, type, Read7BitString(br), version));
    }
    return list;
}

static string Read7BitString(BinaryReader br)
{
    var len = br.Read7BitEncodedInt();
    return Encoding.UTF8.GetString(br.ReadBytes(len));
}

readonly record struct MethodLoc(int Rva, int CodeOffset, int CodeSize, bool HasExceptionHandlers);
readonly record struct BundleEntry(long Offset, long Size, byte Type, string Path, string BundleVersion);
