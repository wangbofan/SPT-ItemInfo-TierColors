# SPT-ItemInfo-TierColors

一个仓库，两部分，一起装：

| 部分 | 位置 | 干什么 |
|---|---|---|
| **服务端 mod** | `server/` | ODT-ItemInfo 4.0 汉化版（已移植到 SPT 4.1 API）：修 `BackgroundColor` 枚举崩溃、`/iteminfo/tiers` 下发档位表、物品名上色、价格信息 |
| **客户端插件** | `client/` | BepInEx 插件：物品格子背景按**价值五档**上色（灰 / 蓝 / 紫 / 橙 / 红）、物品名跟着同色系变色、自动检视全部物品 |

> 服务端源自 [ODT-ItemInfo-4.0](https://github.com/thuynguyentrungdang/ODT-ItemInfo-4.0) 的简体中文分支。

## 下载

到 [Releases](https://github.com/wangbofan/SPT-ItemInfo-TierColors/releases) 拿整合包，关掉游戏和服务端，解压后双击 `安装-双击我.bat`：

- 自动找 E:/D:/C:/F:/G: 下的 `GAME`
- 装插件到 `BepInEx\plugins\ODT-TierColors`
- 迁移你以前调过的配色键名
- 更新服务端 `SPT_Runtime\user\mods\ODT-ItemInfo-4.0` 的 DLL，缺的 config 补齐，**不覆盖你调过的配置**

## 五档

| # | 档位 | 品质名 | 格子背景（枚举） | 名字颜色 | 等效价值 V |
|---|---|---|---|---|---|
| 1 | `COMMON` | 普通 | `grey`（灰，客户端默认 A = 0 不画） | `#CCCCCC` | V ≤ 10,000 |
| 2 | `RARE` | 少见 | `blue`（蓝） | `#3B82AE` | 10,000 < V ≤ 35,000 |
| 3 | `EPIC` | 稀有 | `violet`（紫） | `#875BA6` | 35,000 < V ≤ 100,000 |
| 4 | `LEGENDARY` | 史诗 | `orange`（橙） | `#CC9441` | 100,000 < V ≤ 300,000 |
| 5 | `MYTHIC` | 传说 | `tracerRed`（红） | `#CC4F4F` | V > 300,000 |

档位名（普通 / 少见 / 稀有 / 史诗 / 传说）是**品质名**，跟格子颜色解耦：第 5 档在物品描述里写「传说」，不写「红」。

### 价值怎么算

```
单价 U   = 跳蚤均价；没有跳蚤价时用图鉴（手册）价
总价 T   = U × 堆叠上限（StackMaxSize，至少按 1 算）
等效价值 V = T × 忠诚系数 K
```

K 取「能买到这件物品的商人里，忠诚等级最低的那个」：1 级 1.00 / 2 级 1.15 / 3 级 1.30 / 4 级 1.45 / 商人不卖 1.00。

- 直购和以物易物一视同仁，只看等级；不像上游给兑换额外 +1 档。
- 弹药箱按「箱内弹种单价 × 数量」算，不按箱子本身的图鉴价。
- 货币、任务物品、图鉴里没有的物品不参与判档（保持游戏原色）。

### 实测分布（SPT 4.1.6 · EFT 0.16.9 · 全库 4147 件）

`1 档 1016 · 2 档 1172 · 3 档 1197 · 4 档 611 · 5 档 151`

## 客户端 F12 面板

```
00 总开关     启用按价值配色 / 自动检视全部物品（默认开）
01 五档颜色   1 普通·灰  2 少见·蓝  3 稀有·紫  4 史诗·橙  5 传说·红
              A 只决定画不画：A = 0 不画，A > 0 画（2.3.2 起）
02 高级       配件最多升 1 档 / 背景亮度 / 背景饱和度 / 背景不透明度 / 物品名跟着变色 / 打印调试日志
```

**五档透明度统一**：画出来有多透，一律用「背景不透明度」那一个值，不再逐档各乘各的 A。
所以仓库里蓝紫橙红叠在一起，看起来是同一层玻璃，只是颜色不同。

## 2.3.2 改了什么

1. **第 5 档在描述里不再叫「红」**，改叫「传说」，五档名统一成品质名：
   `普通 / 少见 / 稀有 / 史诗 / 传说`（`config/translations.json`，九个语言块一起改）。
2. **客户端五档透明度统一**：格子背景颜色的 A 只当开关，浓淡一律取 F12 的「背景不透明度」。
   悬停高亮也只提亮 RGB，A 不动。
3. **明确兼容 SPT 4.1.\***：`SptVersion = ~4.1.0`，实测 4.1.0 / 4.1.6 / 4.1.99 通过，4.0.x 与 4.2.0 不匹配。
4. 整个模组统一叫 **SPT-ItemInfo-TierColors**（客户端 F12 面板显示名也是这个）。

更早的变化见 `server/README.md` 与 `client/README.md`。

## 安装（手动）

1. 客户端：`ODT-TierColors.dll` → `BepInEx\plugins\ODT-TierColors\`（需要 BepInEx 5 + ConfigurationManager）
2. 服务端：`ODT-ItemInfo-4.0.dll` 和 `config\` → `SPT_Runtime\user\mods\ODT-ItemInfo-4.0\`
3. 启动服务端，日志出现 `[ItemInfo] Item Info loaded!` 就成了。

## 编译

服务端：

```powershell
dotnet build server\ODT-ItemInfo-4.0.csproj -c Release -p:SptServerDir="E:\GAME\SPT_Runtime"
```

客户端：

```powershell
dotnet build client\ODT-TierColors.csproj -c Release -p:GameDir="E:\GAME"
```

## 兼容性

- 客户端插件只依赖 **EFT 客户端本体 + BepInEx 5 + ConfigurationManager**，与 SPT 版本无关。
- 服务端 `SptVersion = ~4.1.0`，覆盖 SPT **4.1.x** 全系。
- 客户端拿不到档位表时不改任何颜色，界面保持游戏原样，不会搞花。

## 目录

```
server/   ODT-ItemInfo-4.0 源码（ItemInfo.cs、Utils.cs、Models/、config/、csproj、README）
client/   ODT-TierColors 源码（Plugin.cs、AutoExamine.cs、csproj、README）
docs/     预览图
```

## 许可

MIT。上游 `ODT-ItemInfo-4.0` 版权归 ODT / long120611 所有。
