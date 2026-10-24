# 官方单文件二进制补丁（解锁中国大陆下载）

在**不重新编译、不重新打包**的前提下，直接对官方发布的单文件 exe 做原地二进制补丁，
解除「中国大陆地区禁用 Minecraft 下载 / 补全 / 版本安装 / 整合包安装」的区域封锁。

产物文件大小与官方**完全相同**，bundle 布局不变，其余 92 个内嵌模块字节级一致——
官方内置密钥（正版登录、CurseForge、遥测）全部保留。这是源码重编译做不到的：
GitHub Actions Secrets 只写不可读，重建会丢失这些凭据。

> 源码级方案见上级目录 [`patches/`](../patches)：直接 revert 上游 #3673 的提交。
> 两种方案二选一即可，本目录适用于**只能拿到官方 exe、无法重编译**的场景。

## 适用版本与校验值

| 项目 | 值 |
|---|---|
| 官方输入 | `PCL2_CE_Release_x64_4.exe` |
| 输入 SHA256 | `168BD8369D5ED4E5478942677562913330EF1D30A5193870AF910BD18198050C` |
| 官方版本 | FileVersion `2.15.1.0`，ProductVersion `1.0.0+1a765312`（含 #3673 封锁） |
| 补丁产物 SHA256 | `A5C849BE4558B2BCBDAEDEAC3DEE958500B8C0829BA8815525FFAF4A00476B66` |
| 产物大小 | `23,486,024` 字节（与官方一致） |
| 变更量 | 95 字节（3 个方法体，共重写 110 字节） |

补丁按**方法名**定位而非固定偏移，因此同一模式在相邻版本通常仍可用；不匹配时工具报错退出，
不会产出损坏文件。

## 原理

官方包是 .NET 单文件 bundle（framework-dependent，未压缩）：

```
bundle v6.0 | 93 个文件 | header @ 23,480,540 | flags = 0（无压缩）
Plain Craft Launcher 2.dll  offset = 364,544  size = 5,154,816
```

因为 bundle 未压缩、且补丁不改变任何文件大小，可以直接改写内嵌程序集字节，
无需解包重打包，header 与所有偏移保持有效。

流程（`tools/BundlePatch`）：

1. `Microsoft.NET.HostModel.Bundle.Bundler.IsBundle` 判定单文件包，手工解析 v6.0 header 得到文件表。
2. 取出 `Plain Craft Launcher 2.dll`，用 `System.Reflection.Metadata` 找到类型 `PCL.RestrictedFeaturePolicy`。
3. 对每个目标方法：metadata RVA → 节表 `PointerToRawData + (rva - VirtualAddress)` 换算文件偏移，
   读方法体头（tiny `(b&3)==2` / fat `(b&3)==3`，含 EH 则拒绝），得到 IL 起点与长度。
4. 用**等长常量桩**覆盖 IL。

| 方法 | 原语义 | 补丁后 IL | 效果 |
|---|---|---|---|
| `get_IsRestricted` | 判定区域并返回是否受限 | `ldc.i4.0; nop…; ret` | `IsRestricted` 恒为 `false` |
| `Check(string,bool)` | 受限则弹窗拦截并返回 `false` | `ldc.i4.1; nop…; ret` | 所有被拦截功能直接放行 |
| `ThrowIfRestricted(string)` | 受限则抛异常 | `nop…; ret` | 永不抛出 |

这三个方法覆盖上游 #3673 引入的**全部**闸门调用点（下载、补全、重置、安装、整合包、
启动前补全），因此无需逐个修改调用方。

### 踩过的坑：IL 末尾必须是终止指令

第一版补丁写成 `ret` + nop 填充，静态字节完全正确，但 JIT 抛
`InvalidProgramException: Common Language Runtime detected an invalid program`。

用最小复现实验（自建程序集 + 同样手法）定位到原因：**.NET 不接受方法体末尾存在没有终止符的死代码块**。
`ret; nop; nop` 非法，`nop; nop; ret` 合法。因此常量桩的 nop 填充必须放在最后的 `ret` **之前**。

## 用法

```bat
rem 首次运行会自动编译补丁器（需要 .NET SDK 10）
binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" "PCL2_CE_Release_x64_4_patched.exe"

rem 只看将被修改的偏移与原始 IL，不写文件
binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" out.exe --dry-run
```

工具会打印输入/输出 SHA256、三个方法的原始 IL 与改写偏移，并在写完后**重新读回产物校验**，
最后输出 `VERIFY: PASS`。

### 验证工具

```bat
rem 动态验证：解包并把闸门方法逐个反射调用，对比官方包与补丁包的行为
tools\GateCheck\bin\Release\net10.0-windows\GateCheck.exe <exe> <tmp-dir> --check

rem 诊断：打印 bundle/PE 结构、是否 ReadyToRun、每个方法的 RVA / 文件偏移 / IL
tools\Diag\bin\Release\net10.0\Diag.exe <inner.dll> PCL.RestrictedFeaturePolicy
```

## 验证结论（本仓库提交时的实测）

1. **静态 IL**：补丁后重解包，三个方法体分别等于 `1600…2A` / `1700…2A` / `0000…2A`。
2. **动态行为**（本机 `China Standard Time` + `zh-CN`）：

   | 调用 | 官方包 | 补丁包 |
   |---|---|---|
   | `RestrictedFeaturePolicy.IsRestricted` | **True**（封锁生效） | **False** |
   | `Check("harness", false)` | 受限 | **True** |
   | `ThrowIfRestricted("harness")` | 受限 | **正常返回，不抛异常** |

3. **真实启动**：空目录运行产物，窗口正常拉起，日志显示服务加载、更新检查、遥测上报正常
   （说明官方密钥可用）。
4. **完整性**：逐文件比对 93 个内嵌模块，仅 `Plain Craft Launcher 2.dll` 不同；
   `PCL.Core.dll`、`deps.json`、`runtimeconfig.json` 等 92 个文件哈希一致；exe 大小不变。
5. **可复现性**：同一输入重复打补丁，产物 SHA256 完全一致。

## 限制与注意事项

1. **版本强绑定**：补丁按方法名定位，后续版本通常仍适用；若上游重写闸门结构或改用压缩 bundle
   （`EnableCompressionInSingleFile`），工具会报错退出，不会产出损坏文件。
2. **自动更新会覆盖**：启动器自更新会换回官方文件、补丁失效；更新后需重新打补丁。
3. **签名**：官方包本身未做 Authenticode 签名（`NotSigned`），修改不涉及签名失效。
4. **不要分发产物**：补丁保留了官方内置的 OAuth / CurseForge 凭据，仅限本机使用。
5. 目标机需要 **.NET 10 Desktop Runtime**（官方包同为框架依赖型）。
6. 回滚 = 直接使用官方原文件。

## 目录

```
binary-patch/
├─ patch.cmd                  # 一键打补丁（首次自动编译）
├─ README.md
└─ tools/
   ├─ BundlePatch/            # 补丁器：bundle 解析 + IL 定位与改写 + 回读校验
   ├─ GateCheck/              # 动态验证：解包并反射调用闸门方法
   └─ Diag/                   # 诊断：bundle/PE 结构、R2R、方法 RVA 与 IL
```
