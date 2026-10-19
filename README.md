# SPT 物品信息 + 等级配色（SPT-ItemInfo-TierColors）

一个仓库，两部分，一起装：

| 部分 | 位置 | 干什么 |
|---|---|---|
| **服务端 mod** | `server/` | ODT-ItemInfo 4.0 汉化版（已移植到 SPT 4.1 API）：修 `BackgroundColor` 枚举崩溃、按稀有度分 10 档、名字上色、价格信息、★☆ 价值标记 |
| **客户端插件** | `client/` | BepInEx 插件：物品格子背景按等级上色（10 档可调）、物品名字同色、自动检视（解决装备预设报「无法应用未检视的物品」） |

> 服务端源自 [ODT-ItemInfo-4.0](https://github.com/thuynguyentrungdang/ODT-ItemInfo-4.0) 的简体中文分支（[ODT-ItemInfo-4.0-zh](https://github.com/wangbofan/ODT-ItemInfo-4.0-zh)）。

## 下载

到 [Releases](https://github.com/wangbofan/SPT-ItemInfo-TierColors/releases) 拿整合包，关掉游戏和服务端，解压后双击 `安装-双击我.bat`：

- 自动找 E:/D:/C: 下的 `GAME`
- 装插件到 `BepInEx\plugins\ODT-TierColors`
- 迁移你以前调过的配色键名
- 更新服务端 `user\mods\ODT-ItemInfo-4.0` 的 DLL 与配置（`tiers_hex.json` 备份后覆盖，保证名字颜色和插件一致）

## 十档

| # | 档位 | 默认色 | 判据 |
|---|---|---|---|
| 01 | 普通 | `#FFFFFF` | 商人 1 级在卖；每格手册价 < 1 万 |
| 02 | 稀有 | `#4ADE80` | 商人 2 级；每格 1–2 万 |
| 03 | 史诗 | `#4D9DFF` | 商人 3 级；每格 2–3 万 |
| 04 | 传说 | `#C084FC` | 商人 4 级；每格 3–4 万 |
| 05 | 顶级 | `#FF8A00` | 4 级兑换（+1 档）；每格 4–6 万 |
| 06 | 稀世 | `#FFE066` | 武器/护甲/弹药/配件且商人无货；或每格 ≥ 6 万 |
| 07 | 禁售·商人有货 | `#FF3B3B` | 跳蚤禁售但仍能买到（197 件） |
| 08 | 超模·酸绿弹 | `#A8FF00` | 服务端手工指定（全库仅 1 件） |
| 09 | 禁售·商人无货 | `#FF2E93` | 跳蚤禁售且所有商人零货源（356 件） |
| 10 | 未知 | `#9AA3AD` | 其它枚举，兜底 |

01–06 是价格色阶（白绿蓝紫橙金），07–09 是状态标记（故意跳出色阶），10 是中性灰。

判档顺序：跳蚤禁售 → 有货 07 / 无货 09；命中服务端 `customRarity` 手工表 → 直接用该档；商人有卖 → 取最低忠诚等级（兑换 +1，1–5）；商人都不卖 → 装备类 06，否则按每格手册价兜底；任务物品 +2（`server/ItemInfo.cs:390`）。

## 客户端 F12 面板

```
00 总开关    启用配色（关掉恢复原版按类别上色，名字也回服务端色）
             自动检视全部物品（默认开）
01 等级颜色  01 普通 (COMMON) … 10 未知 (UNKNOWN)，10 个 Color
02 高级      背景不透明度（0.05–1，只管格子，名字永远实色）
```

ConfigurationManager 的 Color 控件给的是色块 + R/G/B/A 滑块 + Hex 输入框，改完立即生效。

## 安装（手动）

1. 客户端：把 `ODT-TierColors.dll` 放到 `BepInEx\plugins\ODT-TierColors\`（需要 BepInEx 5 + ConfigurationManager）。
2. 服务端：把 `ODT-ItemInfo-4.0.dll` 和 `config\` 放到 `user\mods\ODT-ItemInfo-4.0\`。

## 编译

服务端（需要 SPT 服务端目录下的引用程序集）：

```powershell
dotnet build server\ODT-ItemInfo-4.0.csproj -c Release -p:SptServerDir="E:\GAME\SPT_Runtime"
```

客户端（需要游戏目录下的 Unity / Assembly-CSharp / BepInEx 引用）：

```powershell
dotnet build client\ODT-TierColors.csproj -c Release -p:GameDir="E:\GAME"
```

## 兼容性

- 客户端插件只依赖 **EFT 客户端本体 + BepInEx 5 + ConfigurationManager**，与 SPT 版本无关。
- 服务端 DLL 的 `SPTarkov.Server.Core / DI / Common` 引用已从 4.1.6.0 降到 **4.1.0.0** —— SPT 的启动检查只拦「引用版本高于本机版本」的 mod，所以在 4.1.5 / 4.1.6 上都能加载。
- 重新编译时若你的 SPT 是 4.1.5，建议直接 `-p:SptServerDir` 指向它自己的目录，编出来的引用就是 4.1.5。

## 目录

```
server/   ODT-ItemInfo-4.0 源码（ItemInfo.cs、Utils.cs、Models/、csproj、README）
client/   ODT-TierColors 源码（Plugin.cs、NameRemap.cs、AutoExamine.cs、csproj）
docs/     预览图
```

## 许可

MIT
