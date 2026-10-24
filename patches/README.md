# PCL CE 中国大陆下载解锁补丁

解决 **PCL 社区版（PCL-CE）在中国大陆地区禁用 Minecraft 下载 / 补全 / 版本安装 / 整合包安装** 的问题。

> 本目录是**源码级**方案（重新编译）。若不希望重编译——官方构建内含 `MS_CLIENT_ID`、
> `CURSEFORGE_API_KEY` 等只在 CI 注入的密钥，重编译会丢失——可改用
> [`../binary-patch/`](../binary-patch/)：对官方单文件 exe 做原地二进制补丁，大小与布局不变。

## 原理

上游提交 `8cd9d374`（`fix(install): 中国大陆地区禁用 Minecraft 安装功能 (#3673)`）引入了区域封锁：

- `PCL.Core/App/Region/RegionPolicy.cs`：通过 Windows 区域名（`GetUserDefaultGeoName`）、
  UI 语言、用户区域设置、时区综合判定是否为「中国大陆 / 无法判定」；
- `Plain Craft Launcher 2/Modules/Minecraft/RestrictedFeaturePolicy.cs`：判定为受限时弹窗并阻断；
- 14 个调用点覆盖：Minecraft 客户端下载、版本安装、文件补全、重置、整合包安装、启动前补全等。

本补丁即**完整撤销该提交**，让上述功能对所有地区无差别开放：

| 变更 | 说明 |
|---|---|
| 删除 `RegionPolicy.cs` | 250 行区域判定逻辑 |
| 删除 `RegionPolicyTest.cs` | 92 行对应单元测试 |
| 删除 `RestrictedFeaturePolicy.cs` | 33 行拦截器 |
| 移除 2 条本地化文案 | `Region.Feature.Restricted` / `Region.Feature.Unknown` |
| 还原 10 处调用点 | 下载 / 安装 / 补全 / 重置 / 整合包 / 启动补全 |

合计 14 个文件、528 行删除、2 行恢复。

## 用法

### 1. 直接应用补丁

```powershell
# 在仓库根目录（本 patches 目录的上一级）
powershell -ExecutionPolicy Bypass -File .\patches\apply-patch.ps1 -Build
```

脚本做的事：

1. 幂等检测：若已无区域封锁代码，直接跳过；
2. `git apply --3way` 应用补丁，失败时回退 `--reject` 并给出提示；
3. 校验：确认 `RestrictedFeaturePolicy.cs` 已删除且无残留引用；
4. `-Build` 时调用 `dotnet publish` 产出单文件 exe 到 `artifacts\pcl-ce-unlocked\`。

### 2. 手动应用

```powershell
git apply --3way patches\0001-unlock-downloads-mainland-china.patch
dotnet publish "Plain Craft Launcher 2\Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -o .\artifacts\pcl-ce-unlocked --no-self-contained
```

### 3. 反向验证 / 回滚

```powershell
git apply -R patches\0001-unlock-downloads-mainland-china.patch
```

## 已验证

- 补丁在干净 checkout（`915ac297`）上 `git apply --3way` 成功，产出的 git tree 哈希
  与应用后的工作区**完全一致**（`63d732f0…`）；
- `Release|x64` 单文件发布：0 错误，产物 `Plain Craft Launcher 2.exe` 22.4 MB；
- 冒烟启动：窗口正常拉起，无崩溃；
- 全仓 `git grep` 确认 `RestrictedFeaturePolicy` / `RegionPolicy` / `Region.*` 文案零残留。

## 注意事项

1. **目标版本**：补丁基于 `dev` 分支提交 `915ac297`（2026-10-23）。上游后续改动同一批文件时可能产生
   `.rej` 冲突，需手动合并；新版本若重构了区域判定，建议改为「最小改动」而非整提交回退。
2. **连带影响**：后续提交 `1a765312`（`fix(install): 读取"设备设置区域" (#3676)`）只修改了
   `RegionPolicy.cs` 与 `RegionPolicyTest.cs`，这两个文件随本补丁删除，因此 #3676 的
   「读取注册表 DeviceRegion」功能一并消失。这是整提交回退的必然结果。
3. **与密钥无关**：下载 Minecraft 原版 / Forge / Fabric / NeoForge 等不依赖
   `MS_CLIENT_ID`、`CURSEFORGE_API_KEY` 等构建期密钥；缺失只影响微软正版登录与 CurseForge 资源。
4. **运行时依赖**：`--no-self-contained` 产物需要目标机安装 .NET 10 Desktop Runtime。
5. **发布/分发**：本补丁产物为自编译版本，未签名；正式分发请遵循 PCL-CE 的上游许可与社区规范。
