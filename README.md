# PCLCE Unlock

解除 **PCL 社区版**（Plain Craft Launcher Community Edition，PCL-CE）在中国大陆地区
对 **Minecraft 下载 / 文件补全 / 版本安装 / 整合包安装** 的封锁。

上游提交 `8cd9d374`（`fix(install): 中国大陆地区禁用 Minecraft 安装功能 (#3673)`）依据
Windows 区域名、UI 语言、用户区域设置与时区判定「中国大陆 / 无法判定」，随后在 14 个调用点
拦截下载与安装；后续 `1a765312`（#3676）又为该判定加入了「设备设置区域」读取。

本仓库提供两种互补方案，按你的条件二选一：

| 方案 | 适用场景 | 入口 | 代价 |
|---|---|---|---|
| **官方单文件二进制补丁** | 只能拿到官方 exe；需要保留官方内置密钥（微软正版登录、CurseForge） | [`binary-patch/`](binary-patch/) | 需 .NET SDK 10 构建补丁器 |
| **源码级回退补丁** | 愿意自行编译，且不需要官方密钥 | [`patches/`](patches/) | 需 .NET SDK 10 完整构建 PCL |

> 为什么会有两个方案：官方 CI 在 `Release` 构建里**编译期烧入** `MS_CLIENT_ID`、
> `CURSEFORGE_API_KEY`、`SENTRY_DSN` 等（GitHub Actions Secrets 只写不可读），
> 重新编译会丢掉这些凭据，微软正版登录与 CurseForge 资源会失效。二进制补丁则完全保留它们。

## 快速开始

### A. 官方单文件 exe 原地打补丁（推荐）

```bat
rem 首次运行自动编译补丁器
binary-patch\patch.cmd "PCL2_CE_Release_x64_4.exe" "PCL2_CE_Release_x64_4_patched.exe"
```

产物与官方**同尺寸**（23,486,024 字节）、bundle 布局不变，93 个内嵌文件中只有
`Plain Craft Launcher 2.dll` 被改写（整文件仅 95 字节不同），官方密钥全部保留。

### B. 源码级回退（重新编译）

```powershell
powershell -ExecutionPolicy Bypass -File .\patches\apply-patch.ps1 -Build
```

## 原理速览

- **源码补丁**：整体回退 #3673，删除 `RegionPolicy`、`RestrictedFeaturePolicy` 及
  14 处调用点与 2 条本地化文案（14 文件、528 行删除、2 行恢复）。
- **二进制补丁**：官方包是 .NET 单文件 bundle（v6.0、93 文件、**未压缩**、框架依赖型）。
  由于未压缩且补丁不改变文件大小，可直接改写内嵌程序集的 IL，无需解包重打包，
  所有偏移保持有效。改写 `PCL.RestrictedFeaturePolicy` 的三个方法：

  | 方法 | 补丁后 IL | 效果 |
  |---|---|---|
  | `get_IsRestricted` | `ldc.i4.0; nop…; ret` | `IsRestricted` 恒为 `false` |
  | `Check(string,bool)` | `ldc.i4.1; nop…; ret` | 所有闸门放行 |
  | `ThrowIfRestricted(string)` | `nop…; ret` | 永不抛出 |

  三个方法覆盖 #3673 引入的全部闸门调用点，因此无需逐个修改调用方。

> 踩坑记录：nop 填充必须放在最后一条 `ret` **之前**。`ret; nop; nop` 会被 .NET 判为
> `InvalidProgramException`（方法体末尾不允许出现无终止符的死代码块）。详见
> [`binary-patch/README.md`](binary-patch/README.md)。

## 验证记录

| 项目 | 结果 |
|---|---|
| 静态 IL | 补丁后重解包，三方法体分别为 `1600…2A` / `1700…2A` / `0000…2A` |
| 动态行为（本机 `China Standard Time` + `zh-CN`） | 官方包 `IsRestricted = True`；补丁包 `IsRestricted = False`、`Check(...) = True`、`ThrowIfRestricted` 正常返回 |
| 真实启动 | 空目录运行产物，窗口正常拉起，服务加载 / 更新检查 / 遥测上报正常（说明官方密钥可用） |
| 完整性 | 93 个内嵌文件逐一比对，仅 `Plain Craft Launcher 2.dll` 变化；exe 大小不变 |
| 可复现性 | 同一输入重复打补丁，产物 SHA256 一致（`A5C849BE…76B66`） |

## 注意事项

1. **本仓库不包含任何编译产物或 exe**：补丁后的官方包保留了官方内置的 OAuth /
   CurseForge 凭据，**请勿分发该产物**，仅限本机使用。
2. **版本强绑定但可移植**：二进制补丁按方法名定位而非硬编码偏移，相邻版本通常仍适用；
   若上游重写闸门结构或改用压缩 bundle，工具会报错退出，不会产出损坏文件。
3. **自动更新会覆盖补丁**：启动器自更新会换回官方文件，更新后需重新打补丁。
4. **环境要求**：构建补丁器需 .NET SDK 10；运行 PCL-CE 需 .NET 10 Desktop Runtime
   （官方包同为框架依赖型）。官方包未做 Authenticode 签名。
5. **与上游的关系**：本项目非官方项目，仅供学习与研究。PCL-CE 本体版权归上游所有。

## 目录

```
.
├─ README.md
├─ LICENSE                       # Apache-2.0（与上游一致）
├─ patches/                      # 源码级方案
│  ├─ 0001-unlock-downloads-mainland-china.patch
│  ├─ apply-patch.ps1            # 幂等应用 + 可选构建
│  └─ README.md
└─ binary-patch/                 # 官方单文件二进制方案
   ├─ patch.cmd                  # 一键打补丁（首次自动编译）
   ├─ README.md
   └─ tools/
      ├─ BundlePatch/            # bundle 解析 + IL 定位改写 + 回读校验
      ├─ GateCheck/              # 动态验证：解包并反射调用闸门方法
      └─ Diag/                   # 诊断：bundle/PE 结构、R2R、方法 RVA 与 IL
```

## 许可

Apache License 2.0，与上游 PCL-CE 一致，见 [LICENSE](LICENSE)。
