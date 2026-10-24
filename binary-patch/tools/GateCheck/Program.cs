using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.NET.HostModel.Bundle;

// Extract a PCL CE single-file bundle and reflectively evaluate the region gate.
// usage: GateCheck <exe> <extractDir> [--check]

if (args.Length < 2)
{
    Console.WriteLine("usage: GateCheck <exe> <extractDir> [--check]");
    return 1;
}

var exePath = Path.GetFullPath(args[0]);
var dir = Path.GetFullPath(args[1]);
var invokeCheck = args.Contains("--check");

Directory.CreateDirectory(dir);
var isDll = exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
if (!isDll)
{
    var exeBytes = File.ReadAllBytes(exePath);
    if (!Bundler.IsBundle(exePath, out var headerOffset))
    {
        Console.WriteLine("not a bundle");
        return 2;
    }
    Extract(exeBytes, headerOffset, dir);
}

static void Extract(byte[] exe, long headerOffset, string dir)
{
    foreach (var e in ParseBundle(exe, headerOffset))
    {
        if (e.Type is <= 0 or > 5) continue;
        var outPath = Path.Combine(dir, e.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var buf = new byte[e.Size];
        Array.Copy(exe, e.Offset, buf, 0, e.Size);
        File.WriteAllBytes(outPath, buf);
    }
}

var asmPath = isDll ? exePath : Path.Combine(dir, "Plain Craft Launcher 2.dll");
Console.WriteLine($"extracted  : {asmPath}");
Console.WriteLine($"timezone   : {TimeZoneInfo.Local.Id}");
Console.WriteLine($"culture    : {CultureInfo.CurrentCulture.Name} / ui {CultureInfo.CurrentUICulture.Name}");
Console.WriteLine();

var asm = Assembly.LoadFrom(asmPath);
var type = asm.GetType("PCL.RestrictedFeaturePolicy", throwOnError: true);
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

var getter = type!.GetProperty("IsRestricted", flags)!.GetMethod!;
var check = type.GetMethod("Check", flags, null, [typeof(string), typeof(bool)], null)!;
var throwIf = type.GetMethod("ThrowIfRestricted", flags, null, [typeof(string)], null)!;

Console.WriteLine($"IsRestricted getter   -> {Invoke(() => getter.Invoke(null, null))}");

if (invokeCheck)
{
    Console.WriteLine($"Check(\"harness\", false) -> {Invoke(() => check.Invoke(null, ["harness", false]))}");
    Console.WriteLine($"ThrowIfRestricted     -> {Invoke(() => { throwIf.Invoke(null, ["harness"]); return "returned (no throw)"; })}");
}

return 0;

static string Invoke(Func<object> f)
{
    try
    {
        return f()?.ToString() ?? "null";
    }
    catch (TargetInvocationException ex)
    {
        return $"threw {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}";
    }
    catch (Exception ex)
    {
        return $"error {ex.GetType().Name}: {ex.Message}";
    }
}

static List<Entry> ParseBundle(byte[] bytes, long headerOffset)
{
    var list = new List<Entry>();
    using var ms = new MemoryStream(bytes, (int)headerOffset, bytes.Length - (int)headerOffset, writable: false);
    using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
    br.ReadUInt32();
    br.ReadUInt32();
    var count = br.ReadInt32();
    Read7BitString(br);
    br.ReadInt64(); br.ReadInt64(); br.ReadInt64(); br.ReadInt64(); br.ReadUInt64();
    for (var i = 0; i < count; i++)
    {
        var offset = br.ReadInt64();
        var size = br.ReadInt64();
        br.ReadInt64();
        var type = br.ReadByte();
        list.Add(new Entry(offset, size, type, Read7BitString(br)));
    }
    return list;
}

static string Read7BitString(BinaryReader br)
{
    var len = br.Read7BitEncodedInt();
    return Encoding.UTF8.GetString(br.ReadBytes(len));
}

readonly record struct Entry(long Offset, long Size, byte Type, string Path);


