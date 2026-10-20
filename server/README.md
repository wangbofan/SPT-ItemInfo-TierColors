# SPT-ItemInfo-TierColors · 服务端（简中 · SPT 4.1 · 五档配色）

本仓库名 **SPT-ItemInfo-TierColors**，服务端与客户端合在一起发布。

SPT 服务端的物品信息模组：[ODT-ItemInfo 4.0](https://github.com/thuynguyentrungdang/ODT-ItemInfo-4.0) 的简体中文分支，
已移植到 **SPT 4.1** 的 API，并把稀有度从上游的十档玩法改成按**价值**分的**五档（普通 / 少见 / 稀有 / 史诗 / 传说）**，格子配色是灰 / 蓝 / 紫 / 橙 / 红。

配套的客户端插件（把格子背景和物品名字按档位上色、自动检视全部物品）在本仓库的 `client/`，
完整安装包见 [Releases](https://github.com/wangbofan/SPT-ItemInfo-TierColors/releases)。

---

## 五档是什么

| # | 档位 | 中文名 | 格子背景（枚举） | 名字颜色 | 等效价值 V |
|---|---|---|---|---|---|
| 1 | `COMMON` | 普通 | `grey`（灰，客户端默认 A = 0 不画） | `#CCCCCC` | V ≤ 10,000 |
| 2 | `RARE` | 少见 | `blue`（蓝） | `#3B82AE` | 10,000 < V ≤ 35,000 |
| 3 | `EPIC` | 稀有 | `violet`（紫） | `#875BA6` | 35,000 < V ≤ 100,000 |
| 4 | `LEGENDARY` | 史诗 | `orange`（橙） | `#CC9441` | 100,000 < V ≤ 300,000 |
| 5 | `MYTHIC` | 传说 | `tracerRed`（红） | `#CC4F4F` | V > 300,000 |

档位名（普通 / 少见 / 稀有 / 史诗 / 传说）是**品质名**，跟颜色名解耦：
第 5 档在描述里显示「传说」，不再写「红」。两套都在 `config/translations.json` 里可改。

### 价值怎么算

```
单价 U   = 跳蚤均价；没有跳蚤价时用图鉴（手册）价
总价 T   = U × 堆叠上限（StackMaxSize，至少按 1 算）
等效价值 V = T × 忠诚系数 K
```

- **K（忠诚系数）** 取「能买到这件物品的商人里，忠诚等级最低的那个」：

  | 最低忠诚等级 | K |
  |---|---|
  | 商人 1 级 | 1.00 |
  | 商人 2 级 | 1.15 |
  | 商人 3 级 | 1.30 |
  | 商人 4 级 | 1.45 |
  | 商人不卖（或只被黑名单商人卖） | 1.00 |

- 直购和以物易物**一视同仁**，只看等级；不再像上游那样给兑换额外 +1 档。
- 跳蚤禁售、任务物品不再单独占档，一律按价值照常判档。
- 弹药箱按「箱内弹种单价 × 数量」算，不按箱子本身的手册价。
- 货币、任务物品、图鉴里没有的物品不参与判档（保持游戏原色）。

### 实测分布（SPT 4.1.6 · EFT 0.16.9 · 全库 4147 件）

`1 档 1016 · 2 档 1172 · 3 档 1197 · 4 档 611 · 5 档 151`

判档是确定性的：同样的数据库跑两次结果完全一致。

---

## 2.3.2 改了什么

1. **第 5 档在描述里不再叫「红」**，改叫「传说」，五档名统一成品质名：
   `普通 / 少见 / 稀有 / 史诗 / 传说`（`config/translations.json`，九个语言块一起改）。
   以前价格行尾部写的是颜色词，红物品看起来像在报颜色而不是品质。
2. **客户端五档透明度统一**：格子背景颜色的 A 只当开关（0 不画 / 非 0 画），
   画出来有多透一律取 F12 的「背景不透明度」，不再逐档各乘各的 A。
3. **兼容性标注**：`SptVersion` 保持 `~4.1.0`，实测覆盖 4.0.0 否 / 4.1.0 / 4.1.6 / 4.1.99 是、4.2.0 否，
   正好是 SPT **4.1.x** 全系（含 4.1.6）。
4. `tiers.json` 的 `COMMON` 维持 `grey`，文档里的「白」全部改成「灰」。

---

## 安装

1. 关掉 SPT 服务器。
2. 把 `ODT-ItemInfo-4.0` 整个文件夹（DLL + `config` 目录）放到：
   ```
   <游戏目录>\SPT_Runtime\user\mods\ODT-ItemInfo-4.0
   ```
3. 启动服务器，日志里出现 `[ItemInfo] Item Info loaded!` 即成功。
4. 想在游戏里看到颜色和自动检视，还要装客户端插件 `ODT-TierColors`（见下）。

开箱即为中文：`config/config.json` 的 `UserLocale` 预设为 `ch`。

---

## 配置

| 文件 | 管什么 |
|---|---|
| `config/tiers.json` | **背景色枚举名** + 四道阈值 + 四个忠诚系数 |
| `config/tiers_hex.json` | **物品名 / 简称的十六进制颜色** |
| `config/config.json` | 各功能模块开关、`customRarity` 手工档位表、`RarityRecolorBlacklist` |
| `config/translations.json` | 各语言的界面文案 |
| `config/bsgblacklist.json` | BSG 原版跳蚤禁售表 |
| `config/keymaps.json` | 地图 / 区域名映射 |

### tiers.json

```json
{
  "COMMON": "grey",
  "RARE": "blue",
  "EPIC": "violet",
  "LEGENDARY": "orange",
  "MYTHIC": "tracerRed",
  "TIER_1_MAX": "10000",
  "TIER_2_MAX": "35000",
  "TIER_3_MAX": "100000",
  "TIER_4_MAX": "300000",
  "LOYALTY_1": "1.00",
  "LOYALTY_2": "1.15",
  "LOYALTY_3": "1.30",
  "LOYALTY_4": "1.45"
}
```

**背景色必须是枚举名，不能写 `#RRGGBB`。** 原因见下一节。

### tiers_hex.json

```json
{ "COMMON": "#CCCCCC", "RARE": "#3B82AE", "EPIC": "#875BA6",
  "LEGENDARY": "#CC9441", "MYTHIC": "#CC4F4F" }
```

这套十六进制是**物品名字**的颜色，2.2.0 起整体压暗了一档（原色 `#FFFFFF/#2694DA/#9F5ACF/#FFA726/#FF3B3B`
在深色界面上偏扎眼）。想恢复原色，把上面五个值换回去即可。

### customRarity（手工档位）

`config/config.json` → `RarityRecolor.customRarity`，格式 `"物品 tpl": 档位`，值会被收敛到 1–5，并且**覆盖**算出来的档：

```json
"customRarity": {
  "5732ee6a24597719ae0c0281": 1,
  "544a11ac4bdc2d470e8b456a": 2,
  "5857a8b324597729ab0a0e7d": 3,
  "59db794186f77448bc595262": 4
}
```

---

## 为什么背景色一定要写枚举名

SPT 4.1 / EFT 0.16.x 的客户端把物品的 `BackgroundColor` 当成枚举 `JSType.TaxonomyColor` 解析，
只认下面 12 个值（大小写不敏感）：

`default` `blue` `green` `grey` `yellow` `orange` `red` `violet` `black` `tracerYellow` `tracerGreen` `tracerRed`

服务端把该字段原样下发。旧版把 `#FFFFFF` 这类十六进制直接写进去，客户端请求 `/client/items` 时会抛：

```
JSON parsing error in response to https://192.168.1.11:6969/client/items at line 1, position 11375:
Error converting value "#FFFFFF" to type 'JSType.TaxonomyColor'.
Path 'BackgroundColor', line 1, position 11375.
```

玩家直接卡在报错框里进不了游戏。现在（2.0.15 起）由 `Models/RarityColors.cs` 统一收敛：

- `tiers.json` 只返回合法枚举名，非法值退回 `default`，并在启动时打**一次**警告；
- `tiers_hex.json` 的十六进制只用于物品名的富文本着色（`<color=#RRGGBB>名字</color>`）。

配置写错不会再让客户端崩，最多是颜色不对 + 日志里一条警告。

---

## 客户端插件（ODT-TierColors）

服务端会把 `tpl → 档位` 表通过两条路下发：

- HTTP：`GET https://<服务器>:6969/iteminfo/tiers`（同 `.json`）
- 文件：`SPT_Runtime/user/mods/ODT-ItemInfo-4.0/tiers_export.json`

客户端插件优先走 HTTP，失败再读本地文件，所以**插件必须配这个服务端 mod 一起用**。

它负责：

1. 格子背景按档位上色（F12 里 5 个颜色随便调；每档颜色自带的 A 只当开关用——A = 0 不画，非 0 就画，画出来有多透五档统一由「背景不透明度」决定）；
2. 格子里那行物品名跟背景同色系（亮度提到 0.80 以上，暗底上也看得清；可在 F12 关掉）；
3. 配件加成：整件按「自己 + 插槽里最高的配件」上色，默认**最多只升 1 档**；
4. 自动检视全部物品（装配界面 / 装备预设不再报「无法应用未检视的物品」）。

服务端关了 RarityRecolor 时，插件拿不到档位表就不改任何颜色，游戏保持原样。

---

## 切换语言

编辑 `config/config.json` 的 `UserLocale`：

- `ch` = 简体中文（本分支默认）
- `en` = 英文
- 其它：`ru`、`jp`、`kr`、`es`、`es-mx`、`fr`、`ge` 等

> SPT 的中文语言代码是 `ch`，不是 `zh`。写成 `zh` 会读不到游戏本地化。

---

## 汉化范围

物品描述里由本模组追加的文本全部中文化：

- 价格（跳蚤价、物品价值、最佳收购商人）
- 以物易物 / 兑换（商人等级、所需材料、跳蚤总价）
- 制作（工作台等级、材料、每件成本）、材料用途
- 任务上交（含「战局中找到」标记）、藏身处建造需求
- 护甲（防弹等级、有效耐久、材质、维修损耗）
- 容器效率、耳机参数（环境音量 / 压缩增益 / 回响 / 失真）
- 高级弹药信息（伤害、穿透、初速、弹道系数、碎裂 / 跳弹 / 哑火、爆炸属性）
- 档位名（普通 / 少见 / 稀有 / 史诗 / 传说）

物品名、商人名、任务名、地图名直接读游戏本体语言包，跟随游戏语言，本模组不翻译。

---

## 与上游的差别

1. **价值五档**取代上游的十档（商人等级 / 兑换 / 禁售 / 酸绿 / 无货那套判据全部废弃），阈值与系数都进了配置文件。
2. `Models/RarityColors.cs`：背景色只输出 `TaxonomyColor` 枚举名，十六进制只用于名字，非法值退回 `default` 并告警一次。
3. `Models/ModConfig.cs`：`RarityRecolor` 去掉 `alwaysMarkBannedItems` / `addTierNameToName` 等旧开关，换成 `customRarity` 手工表。
4. 高级弹药信息改用翻译键（原先硬编码英文），修掉「曳光弹：否」缺换行和 `Heavy blleding` 拼写。
5. 最佳收购商人名跟随玩家语言；`None` / 比特币的 `GPU` 文案改用翻译键。
6. 移植到 SPT 4.1 API：`ModMetadata` → `IModMetadata`、`OnLoad` → `OnLoadAsync`、注入 `TemplateTable / TradersTable / HideoutTable / LocaleTable / GlobalTable` 取代已删除的 `DatabaseService`、日志改用 `ISptLogger`、`SptVersion` 标 `~4.1.0`。
7. 新增 `tiers_export.json` 导出与 `/iteminfo/tiers` 路由，供客户端插件取档位表。
8. `config/config.json` 的 `UserLocale` 默认 `ch`。

汉化文本基于原作者 long120611 的繁 / 简基础译文做了校对与修订。

---

## 版本要求

- **SPT 4.1.**\*（`SptVersion = ~4.1.0`）**，实测 4.1.6，EFT 0.16.9。
- 兼容范围实测：4.1.0 / 4.1.6 / 4.1.99 通过，4.0.x 与 4.2.0 不匹配（4.2 改 API 后需重编）。
- DLL 直接引用 SPT 目录里的 `SPTarkov.Server.Core.dll` 编译；SPT 升到 4.2 后可能需要重编。
- 启动时报 `Could not load type ... AbstractModMetadata` → 装的是上游 4.0 版 DLL，换本包的。

## 自己编译

需要 .NET SDK 10（或 9）+ 本地 SPT 目录：

```powershell
dotnet build ODT-ItemInfo-4.0.csproj -c Release -p:SptServerDir="E:\GAME\SPT_Runtime"
```

产物：`bin/Release/ODT-ItemInfo-4.0/ODT-ItemInfo-4.0.dll`。

## 许可

MIT，见 [LICENSE](LICENSE)。上游版权归 ODT / long120611 所有。
