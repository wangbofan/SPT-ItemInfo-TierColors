# ODT-ItemInfo
Massive QOL Item Info mod for SPTarkov.
English, with a community Simplified Chinese translation (see below).

# 简体中文说明（汉化版）

本仓库是 [ODT-ItemInfo 4.0](https://github.com/thuynguyentrungdang/ODT-ItemInfo-4.0)（SPT 服务端物品信息模组）的简体中文汉化分支，并已移植到 SPT 4.1 的 API。

- **适配版本：SPT 4.1.x**（已在 SPT 4.1.6 上实测加载成功，日志输出 `[ItemInfo] Item Info loaded!`）。
- 上游原版只支持 SPT 4.0.x；SPT 4.1 删除了 `AbstractModMetadata`、把 `DatabaseService` 拆成了各数据表，原版 DLL 在 4.1 上会报 "was built for a different version of SPT"。
- 如果你的 SPT 还是 4.0.x，请使用上游原版（英文）或本分支的早期 4.0 版本。

汉化方面：补充/校正了全部翻译文本，把原先硬编码英文的弹药信息等改为读翻译键，并修正了一处换行小 bug。

## 下载

不想自己编译：到 [Releases](https://github.com/wangbofan/ODT-ItemInfo-4.0-zh/releases) 下载最新的
`ODT-ItemInfo-4.0-2.0.15-SPT4.1-fix.zip`（已编译好的 DLL + 全部 config），解压后按下面步骤安装。

## 安装

1. 关闭 SPT 服务器。
2. 把 `ODT-ItemInfo-4.0` 整个文件夹（内含 `ODT-ItemInfo-4.0.dll` 与 `config` 文件夹）放到：
   ```
   <你的 SPT 目录>/user/mods/ODT-ItemInfo-4.0/
   ```
3. 启动服务器。汉化版已把 `config/config.json` 的 `UserLocale` 预设为 `ch`，开箱即中文。

## 版本要求

- SPT 4.1.x（实测 4.1.6）。DLL 直接引用 SPT 目录里的 `SPTarkov.Server.Core.dll` 等程序集编译，SPT 升到 4.2 后可能需要重新编译。
- 若服务器启动时报 "Could not load type ... AbstractModMetadata"，说明你装的是上游 4.0 版 DLL，换成本包的 DLL 即可。

### 稀有度背景色（4.1 兼容说明）

SPT 4.1 / EFT 0.16.x 的客户端把物品的 `BackgroundColor` 当作枚举 `JSType.TaxonomyColor` 解析，只认下面 12 个值（大小写不敏感）：

`default` `blue` `green` `grey` `yellow` `orange` `red` `violet` `black` `tracerYellow` `tracerGreen` `tracerRed`

所以从 2.0.15 起两个配置文件各管一件事：

- `config/tiers.json` —— **背景色**，必须是上面的枚举名；
- `config/tiers_hex.json` —— **物品名 / 简称的文字颜色**，用 `#RRGGBB` 十六进制。

旧版把 `tiers_hex.json` 的十六进制直接写进背景色，客户端请求 `/client/items` 时会抛
`Error converting value "#FFFFFF" to type 'JSType.TaxonomyColor'`，玩家卡在报错框进不了游戏。
现在背景色写错只会退回 `default` 并在服务器日志里打一条警告，不会再影响进游戏。

## 切换语言

编辑 `config/config.json` 中的 `UserLocale`：

- `ch` = 简体中文（本汉化版默认）
- `en` = 英文
- 其他可选：`ru`、`jp`、`kr`、`es`、`es-mx`、`fr`、`ge` 等

> 注意：SPT 的中文语言代码是 `ch`，不是 `zh`。写成 `zh` 会导致读取游戏本地化失败。

## 汉化范围

物品描述中由本模组追加的全部文本均已中文化：

- 价格信息（跳蚤价、物品价值、最佳收购商人）
- 以物易物 / 兑换信息（商人等级、所需材料与跳蚤总价）
- 制作信息（工作台等级、材料、每件成本）
- 材料用途（该物品可用于哪些制作）
- 任务上交信息（含"战局中找到"标记）
- 藏身处建造需求
- 护甲信息（防弹等级、有效耐久、材质、维修损耗）
- 容器效率（背包/弹挂/箱子）
- 耳机信息（环境音量、压缩增益、回响、失真）
- 高级弹药信息（伤害、穿透、初速、弹道系数、碎裂/跳弹/哑火概率、爆炸属性等）
- 稀有度名称（普通/稀有/史诗/传说/顶级/稀世/超模/自定义）

## 其他说明

- 物品名称与描述、商人名、任务名、地图/区域名均直接读取游戏本体中文语言包，会跟随游戏语言自动显示，无需在此模组里翻译。
- 物品名后的稀有度颜色、★/☆ 价值标记不受影响，与英文版完全一致。
- 若某语言缺少翻译键，模组会自动回退到英文（原版机制）。

## 与上游版本的差别

1. `config/translations.json`：重写 `ch` 语言块（约 84 条），补全 `at`、`CUSTOM2`，并新增全部弹药字段与 `Yes/No/GPU/None` 等键；`en` 同步补齐新键。
2. `ItemInfo.cs`：高级弹药信息改为使用翻译键（原先全部硬编码英文）；修复"曳光弹：否"缺少换行、以及 `Heavy blleding` 拼写错误；跳蚤禁售文字改用翻译键。
3. `Utils.cs`：最佳收购商人名改用玩家当前语言（原先固定英文）；`None`、比特币的 `GPU` 文案改用翻译键。
4. `config/config.json`：`UserLocale` 默认值改为 `ch`。
5. `ODT-ItemInfo-4.0.csproj`：`StagingDir` 改为相对路径，便于本地自行编译（不影响发布包）。
6. 移植到 SPT 4.1 API：`ModMetadata` 改为实现 `IModMetadata`、`OnLoad` 改为 `OnLoadAsync`、注入 `TemplateTable/TradersTable/HideoutTable/LocaleTable/GlobalTable` 取代已删除的 `DatabaseService`、日志改用 `ISptLogger`（4.1 位置）；`SptVersion` 标记为 `~4.1.0`。
7. 修复 SPT 4.1 上稀有度背景色导致客户端 `JSON parsing error ... TaxonomyColor` 的问题：背景色改读 `config/tiers.json` 的枚举名（新增 `Models/RarityColors.cs` 统一校验），`config/tiers_hex.json` 只负责物品名着色；非法值退回 `default` 并记录警告。

汉化文本基于原作者 long120611 的繁体/简体基础译文做了校对与修订。



# Features: 

## Rarity Recolor
This BETA feature clears and changes background color on EVERY item in the game based on MMO style rarity tier-list with colors that make actual sense. 
Tiers are based on trader level you can purchase or barter the item. 
Barters are considered +1 rarity level. 
Banned on flea market items are given highest rarity - overpowered. 
The tier list: 
* Common (grey background, bought with level 1 traders) 
* Rare (blue, level 2 trades for currency and level 1 barters) 
* Epic (purple, level 3 and level 2 barters) 
* Legendary (dim yellow, level 4 and level 3 barters) 
* Uber (bright yellow, level 4 barters only) 
* UNOBTAINIUM (bright green, super rare spawns, not 100% accurate to actual in-game spawns, especially for keys, but good enough) 
* OP (bright red, banned on flea market) 
* CUSTOM (dim red, not used by default). 

Works for 95% of items well enough to be very much usable. Can add tier name to Prices Info module. Add custom item rarities in config. 
![image](https://user-images.githubusercontent.com/33424002/215009820-772c0b0a-3659-4923-8b63-266ce0d05bd5.png)


## Mark Valueable Items
Marks most valuable items by adding symbols ★ and ☆ to item names and inventory icons based on item per-slot value (configured by thresholds in config) when sold to traders OR fleamarket AVG price.

## Bullet Stats In Name
Adds bullet stats to bullet name (damage / armor penetration). Calculates total damage for buckshot rounds. VERY usefull in raid, because bullet name is shown when check magazine action is used.
> 5.45x39mm PP gs (44/36)

# Description modules:

![image](https://user-images.githubusercontent.com/33424002/215010036-daafcc4c-760a-4750-91ad-372e3206ee97.png)


## Prices Info
Basic module that adds prices information to item description, includes avarage flea price and best trader to sell to.
> Flea price: 61703₽ | Ragman's valuation: 37386₽

## Barter Info
Adds information about how you can buy the item from traders, their levels, price or resources (barter resources short names are used and total sum is based on AVG flea prices)
> [T H I C C item case] Bought @ Therapist lv.4 < ★Defibrillator ×15 + ★LEDX ×15 + Ibuprofen ×15 + ★Toothpaste ×15 | Σ ≈ 12877545₽

## Production Info
If item can be crafted, adds information on resources and total crafting sum per item based on flea prices.
> [9x19mm AP 6.3] Crafted ×150 @ Workbench lv.2 < ☆Hawk ×2 + ☆Pst ×400 | Σ per item ≈ 1686₽

## Crafting Material Info
Shows if item is used in crafts along with other materials and profit delta based on flea prices only (this messes up calculation on some crafts that can be done insanely cheap using trader materials or items obtained from other crafts). This is a guideline for crafting profits, not a rule.
> [SSD drive] Part ×1 > Secure Flash drive ×3 @ Intelligence Center lv.2 < … + ★GPX ×1 + ☆GPhone ×1 | Δ ≈ 16234₽

## Barter Resource Info
Shows info if an item can be traded for something with traders along with other resources. Calculates total sum of all resourses (based on flea prices) and delta between buying the final item directly on flea or from trader. Positive delta = profit, negative = don't bother, buy it directly if you can.
> [Ibuprofen painkillers] Traded ×15 @ Therapist lv.4 > T H I C C item case < … + ★Defibrillator ×15 + ★LEDX ×15 + ★Toothpaste ×15 | Δ ≈ -9777545₽

## Quest Info
Adds information if the item needs to be handed in for a quest. Marks find in raid quest condition with a checkmark with an option to add this checkmark to an item name.
> [CMS surgical kit] Found (✔) ×2 > Ambulance @ Jaeger

## Hideout Info
Shows if item is needed for hideout construcion.
> [Secure Flash drive] Need ×3 > Intelligence Center lv.2

## Armor Info
Adds armor stats for armor level (usefull for Realism mod), effective durability calculation, material quality and per repair degradation.
> [BNTI Zhuk-6a body armor] Armor class: 6 | Effective durability: 94 (Max: 75 x Ceramic: 1.3) | Repair degradation: 17% - 22%

## Container Info
Adds slot effeciency calculation for rigs, backpacks and containers (number of internal slots / item size)
> [WARTECH TV-110 plate carrier rig] Slot effeciency: ×1.92 (23/12)

![image](https://user-images.githubusercontent.com/33424002/215010328-c6fe2241-6e35-4c2c-908f-c59c4b523d32.png)

## Headset Info
Adds headset actual audio stats with pseudo compression boost calculation. In theory, more compression  and lower ambient volume = better (BSG headset rarity tiering supports this theory), but it seems for me, in practice, it's not always the case in-game. Higher resonance means harsher sound and boost at filter frequency.
> [Peltor ComTac 2 headset] Ambient Volume: -5dB | Compressor: Gain 10dB × Treshold -25dB ≈ ×2.5 Boost | Resonance & Filter: 2.47@245Hz | Distortion: 28%

## Spawn Info
Shows pseudo spawn info based on BSG handbook data. Mildly useful, not representative to real spawns, can be safely disabled.
