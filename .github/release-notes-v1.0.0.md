## PCLCE Unlock v1.0.0

解除 **PCL 社区版**（PCL-CE）在中国大陆地区对 **Minecraft 下载 / 文件补全 / 版本安装 / 整合包安装** 的封锁。

本 Release **不附带任何二进制附件**：补丁后的官方包内仍含官方 OAuth / CurseForge 凭据，
不应分发。请在本地对官方文件打补丁。

---

## 方案 A · 官方单文件原地打补丁（推荐）

1. 准备官方包 `PCL2_CE_Release_x64_4.exe`（FileVersion `2.15.1.0`，ProductVersion `1.0.0+1a765312`），
   其 SHA256 应为 `168BD8369D5ED4E5478942677562913330EF1D30A5193870AF910BD18198050C`。

2. 获取工具：

   ```bat
   git clone https://github.com/StillMi/PCLCE-Unlock.git
   cd PCLCE-Unlock
   ```

3. 打补丁（首次运行会自动编译补丁器，需要 .NET SDK 10）：

   ```bat
   binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" "PCL2_CE_Release_x64_4_patched.exe"
   ```

4. 看到 `VERIFY: PASS` 即成功。产物应与官方**同尺寸** `23,486,024` 字节，
   SHA256 为 `A5C849BE4558B2BCBDAEDEAC3DEE958500B8C0829BA8815525FFAF4A00476B66`。

5. 只想先确认会改哪些位置（不写文件）：

   ```bat
   binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" out.exe --dry-run
   ```

## 方案 B · 源码级回退（重新编译）

需要一个干净的 PCL-CE checkout（`dev` 分支 `915ac297`）：

```powershell
powershell -ExecutionPolicy Bypass -File .\patches\apply-patch.ps1 -Build
```

产物在 `artifacts\pcl-ce-unlocked\`。注意：重新编译会丢失官方内置密钥，
微软正版登录与 CurseForge 资源将不可用。

## 验证

| 检查 | 结果 |
|---|---|
| 静态 IL | 三方法体回读为 `1600…2A` / `1700…2A` / `0000…2A` |
| 动态行为（`China Standard Time` + `zh-CN`） | 官方包 `IsRestricted = True`；补丁包 `False`、`Check(...) = True`、`ThrowIfRestricted` 正常返回 |
| 真实启动 | 空目录运行产物，窗口正常、服务加载 / 更新检查 / 遥测正常 |
| 完整性 | 93 个内嵌文件仅 `Plain Craft Launcher 2.dll` 变化，exe 尺寸不变 |
| 可复现性 | 同一输入重复打补丁，产物 SHA256 一致 |

## 注意事项

1. 本 Release **不提供 exe**；补丁产物含官方凭据，请勿分发。
2. 补丁按方法名定位，相邻版本通常仍可用；若上游改用压缩 bundle，工具会报错退出。
3. 启动器自更新会覆盖补丁，更新后需重新打补丁。
4. 与上游无隶属关系，仅供学习研究；PCL-CE 本体遵循 Apache-2.0。

---

## English

### Option A · Patch the official single-file exe in place (recommended)

1. Prepare the official `PCL2_CE_Release_x64_4.exe` (FileVersion `2.15.1.0`,
   ProductVersion `1.0.0+1a765312`); its SHA256 must be
   `168BD8369D5ED4E5478942677562913330EF1D30A5193870AF910BD18198050C`.

2. Get the tools:

   ```bat
   git clone https://github.com/StillMi/PCLCE-Unlock.git
   cd PCLCE-Unlock
   ```

3. Patch (the patcher compiles itself on first run; .NET SDK 10 required):

   ```bat
   binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" "PCL2_CE_Release_x64_4_patched.exe"
   ```

4. `VERIFY: PASS` means success. The output keeps the official size (`23,486,024` bytes) and
   should hash to `A5C849BE4558B2BCBDAEDEAC3DEE958500B8C0829BA8815525FFAF4A00476B66`.

5. To inspect what would change without writing anything:

   ```bat
   binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" out.exe --dry-run
   ```

### Option B · Source-level revert (rebuild)

Use a clean PCL-CE checkout (`dev` @ `915ac297`):

```powershell
powershell -ExecutionPolicy Bypass -File .\patches\apply-patch.ps1 -Build
```

The build lands in `artifacts\pcl-ce-unlocked\`. Note that a rebuild loses the credentials baked
into the official build, so Microsoft account login and CurseForge resources stop working.

### Verification

| Check | Result |
|---|---|
| Static IL | Bodies read back as `1600…2A` / `1700…2A` / `0000…2A` |
| Dynamic behavior (`China Standard Time` + `zh-CN`) | Official: `IsRestricted = True`; patched: `False`, `Check(...) = True`, `ThrowIfRestricted` returns normally |
| Real launch | Patched exe starts in an empty folder; service load / update check / telemetry fine |
| Integrity | Only `Plain Craft Launcher 2.dll` differs out of 93 embedded files; exe size unchanged |
| Reproducibility | Patching the same input twice yields the same SHA256 |

### Notes

1. **No exe is attached.** A patched package carries official credentials — do not redistribute it.
2. Methods are located by name, so adjacent versions usually still work; a compressed bundle makes
   the tool fail loudly instead of writing a broken file.
3. The launcher's self-update overwrites the patch; re-apply after updating.
4. Not affiliated with upstream; provided for study and research. PCL-CE itself is Apache-2.0.
