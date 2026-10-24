# PCLCE Unlock

English | [中文](README.md)

Unlocks **Minecraft download / file repair / version install / modpack install** in
**PCL Community Edition** (Plain Craft Launcher Community Edition, PCL-CE) for mainland China.

Upstream commit `8cd9d374` (`fix(install): mainland China disables Minecraft installation (#3673)`)
classifies a machine as "mainland China / undetermined" from the Windows geo name, UI language,
user region and time zone, then blocks download and installation at 14 call sites. The follow-up
`1a765312` (#3676) additionally reads the "device region" setting for that classification.

This repository provides two complementary approaches — pick the one that fits your situation:

| Approach | When to use it | Entry point | Cost |
|---|---|---|---|
| **Official single-file binary patch** | You only have the official exe, and you need the credentials baked into it (Microsoft account login, CurseForge) | [`binary-patch/`](binary-patch/) | Needs .NET SDK 10 to build the patcher |
| **Source-level revert patch** | You are fine with rebuilding PCL yourself and do not need the official credentials | [`patches/`](patches/) | Needs .NET SDK 10 for a full PCL build |

> Why two approaches: the official CI build **bakes in** `MS_CLIENT_ID`, `CURSEFORGE_API_KEY`,
> `SENTRY_DSN` and friends at compile time (GitHub Actions Secrets are write-only, so they cannot
> be reproduced). A rebuild loses them, which breaks Microsoft account login and CurseForge
> resources. The binary patch keeps every one of them intact.

## Quick start

### A. Patch the official single-file exe in place (recommended)

```bat
rem The patcher compiles itself on first use
binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" "PCL2_CE_Release_x64_4_patched.exe"
```

The output has the **same size** as the official build (23,486,024 bytes) and the same bundle
layout. Of the 93 embedded files only `Plain Craft Launcher 2.dll` is modified — 95 bytes in the
whole exe — and all official credentials are preserved.

### B. Source-level revert (rebuild)

```powershell
powershell -ExecutionPolicy Bypass -File .\patches\apply-patch.ps1 -Build
```

## How it works

- **Source patch**: reverts #3673 as a whole, deleting `RegionPolicy`, `RestrictedFeaturePolicy`,
  14 call sites and 2 localization strings (14 files, 528 deletions, 2 insertions).
- **Binary patch**: the official package is a .NET single-file bundle (v6.0, 93 files,
  **uncompressed**, framework-dependent). Because it is uncompressed and the patch never changes a
  file size, the IL of the embedded assembly can be rewritten in place — no unpack/repack, and
  every offset stays valid. Three methods of `PCL.RestrictedFeaturePolicy` are stubbed:

  | Method | Patched IL | Effect |
  |---|---|---|
  | `get_IsRestricted` | `ldc.i4.0; nop…; ret` | `IsRestricted` is always `false` |
  | `Check(string,bool)` | `ldc.i4.1; nop…; ret` | every gate lets the call through |
  | `ThrowIfRestricted(string)` | `nop…; ret` | never throws |

  These three cover every gate introduced by #3673, so no call site needs to be touched.

> Gotcha worth knowing: the nop padding must come **before** the final `ret`. `ret; nop; nop` makes
> the .NET JIT throw `InvalidProgramException` — a method body may not end with an unterminated dead
> block. See [`binary-patch/README.md`](binary-patch/README.md) for the minimal repro that pinned
> this down.

## Verification

| Check | Result |
|---|---|
| Static IL | After patching, the three bodies read back as `1600…2A` / `1700…2A` / `0000…2A` |
| Dynamic behavior (host: `China Standard Time` + `zh-CN`) | Official: `IsRestricted = True`; patched: `IsRestricted = False`, `Check(...) = True`, `ThrowIfRestricted` returns normally |
| Real launch | Patched exe starts in an empty folder, window responsive, service load / update check / telemetry all fine (proves the official credentials work) |
| Integrity | All 93 embedded files compared: only `Plain Craft Launcher 2.dll` differs; exe size unchanged |
| Reproducibility | Patching the same input twice yields the same SHA256 (`A5C849BE…76B66`) |

## Notes

1. **This repository contains no build artifacts or exe files.** A patched official package still
   carries the official OAuth / CurseForge credentials — **do not redistribute it**, keep it local.
2. **Version-bound but portable**: the binary patch locates methods by name rather than hard-coded
   offsets, so adjacent versions usually still work. If upstream restructures the gate or switches
   to a compressed bundle, the tool exits with an error instead of writing a broken file.
3. **Auto-update overwrites the patch**: the launcher's self-update replaces the exe with the
   official one; re-apply the patch after updating.
4. **Requirements**: .NET SDK 10 to build the patcher, .NET 10 Desktop Runtime to run PCL-CE
   (the official package is framework-dependent too). The official package is not Authenticode-signed.
5. **Relation to upstream**: this is not an official project; it is provided for study and research
   only. PCL-CE itself remains copyright of its upstream authors.

## Layout

```
.
├─ README.md                     # Chinese
├─ README.en.md                  # English (this file)
├─ LICENSE                       # Apache-2.0 (same as upstream)
├─ patches/                      # source-level approach
│  ├─ 0001-unlock-downloads-mainland-china.patch
│  ├─ apply-patch.ps1            # idempotent apply + optional build
│  └─ README.md
└─ binary-patch/                 # official single-file approach
   ├─ patch.cmd                  # one-shot patch (self-building)
   ├─ README.md
   └─ tools/
      ├─ BundlePatch/            # bundle parsing + IL rewrite + read-back verification
      ├─ GateCheck/              # dynamic verification: unpack and invoke the gate methods
      └─ Diag/                   # diagnostics: bundle/PE layout, R2R, method RVA and IL
```

## License

Apache License 2.0, matching upstream PCL-CE. See [LICENSE](LICENSE).
