using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Diagnose IL body location and readiness of a managed assembly.
// usage: Diag <assembly.dll> [typeFullName] [methodName...]

var path = args[0];
var typeFilter = args.Length > 1 ? args[1] : "PCL.RestrictedFeaturePolicy";
var methodFilter = args.Skip(2).ToArray();

var bytes = File.ReadAllBytes(path);
Console.WriteLine($"file      : {path}");
Console.WriteLine($"size      : {bytes.Length:N0}");
Console.WriteLine($"sha256    : {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");

using var ms = new MemoryStream(bytes, writable: false);
using var pe = new PEReader(ms);
var h = pe.PEHeaders;
var cor = h.CorHeader!;
Console.WriteLine($"corFlags  : 0x{(int)cor.Flags:X8} ({cor.Flags})");
Console.WriteLine($"ManagedNativeHeader (R2R): rva=0x{cor.ManagedNativeHeaderDirectory.RelativeVirtualAddress:X} size={cor.ManagedNativeHeaderDirectory.Size}");
Console.WriteLine($"isR2R     : {cor.ManagedNativeHeaderDirectory.Size != 0}");
Console.WriteLine();
Console.WriteLine("sections:");
foreach (var s in h.SectionHeaders)
    Console.WriteLine($"  {s.Name,-10} va=0x{s.VirtualAddress:X6} vsize=0x{s.VirtualSize:X6} raw=0x{s.PointerToRawData:X6} rawsize=0x{s.SizeOfRawData:X6}");
Console.WriteLine();

var md = pe.GetMetadataReader();
foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var full = $"{md.GetString(td.Namespace)}.{md.GetString(td.Name)}";
    if (full != typeFilter) continue;
    Console.WriteLine($"type {full}");
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var name = md.GetString(m.Name);
        if (methodFilter.Length > 0 && !methodFilter.Contains(name)) continue;
        var rva = m.RelativeVirtualAddress;
        Console.WriteLine($"  {name,-22} rva=0x{rva:X6}");

        // (1) independent API
        try
        {
            var body = pe.GetMethodBody(rva);
            var il = body.GetILBytes();
            Console.WriteLine($"      PEReader.GetMethodBody: size={body.Size} maxStack={body.MaxStack} locals={body.LocalSignature.IsNil == false} IL={Convert.ToHexString(il.ToArray())}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      PEReader.GetMethodBody FAILED: {ex.GetType().Name}: {ex.Message}");
        }

        // (2) manual section mapping
        var si = h.GetContainingSectionIndex(rva);
        var sec = h.SectionHeaders[si];
        var off = sec.PointerToRawData + (rva - sec.VirtualAddress);
        var b0 = bytes[off];
        int hs, cs;
        string kind;
        if ((b0 & 3) == 2) { hs = 1; cs = b0 >> 2; kind = "tiny"; }
        else { var fl = BitConverter.ToUInt16(bytes, off); hs = ((fl >> 12) & 0xF) * 4; cs = BitConverter.ToInt32(bytes, off + 4); kind = $"fat(flags=0x{fl:X4})"; }
        var manual = Convert.ToHexString(bytes, off + hs, cs);
        Console.WriteLine($"      manual mapping        : sec={si} off=0x{off:X6} {kind} headerSize={hs} codeSize={cs} IL={manual}");
    }
}
return 0;
