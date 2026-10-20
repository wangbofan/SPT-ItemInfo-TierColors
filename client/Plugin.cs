using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.HandBook;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OdtTierColors
{
    /// <summary>
    /// SPT-ItemInfo-TierColors · 五档配色（灰 蓝 紫 橙 红）。
    ///
    /// 档位不在这里算——服务端 ODT-ItemInfo 已经把每一件物品的档位算好，
    /// 通过 <c>/iteminfo/tiers</c> 把 <c>tpl -> tier</c> 整表下发（客户端没有跳蚤均价，算不出来）。
    /// 这里做三件事：
    ///   1) 开局拉一次档位表（失败就退回去读服务端写在 mod 目录下的 tiers_export.json）；
    ///   2) 给每个格子按「自己 + 身上所有插槽里最高的那个配件」上色；
    ///   3) 拆件、装件、装弹之后重算颜色。
    /// </summary>
    [BepInPlugin(Guid, PluginName, PluginVersion)]
    public sealed class TierColorPlugin : BaseUnityPlugin
    {
        public const string Guid = "justnu.odt.tiercolors";
        public const string PluginName = "SPT-ItemInfo-TierColors";
        public const string PluginVersion = "2.3.2";

        /// <summary>服务端 ItemInfo 的静态路由，返回 {version, names, colors, tiers}。</summary>
        internal const string TierRoute = "/iteminfo/tiers";

        internal static ManualLogSource LogSource = null!;
        internal static TierColorPlugin Instance = null!;

        internal static ConfigEntry<bool> EnabledEntry = null!;
        internal static ConfigEntry<bool> LevelUpOnlyEntry = null!;
        internal static ConfigEntry<float> OpacityEntry = null!;
        internal static ConfigEntry<float> BrightnessEntry = null!;
        internal static ConfigEntry<float> SaturationEntry = null!;
        internal static ConfigEntry<bool> NameColoredEntry = null!;
        internal static ConfigEntry<bool> DebugEntry = null!;

        /// <summary>档位键：1..5 对应 COMMON/RARE/EPIC/LEGENDARY/MYTHIC。</summary>
        internal static readonly string[] TierKeys = { "COMMON", "RARE", "EPIC", "LEGENDARY", "MYTHIC" };

        /// <summary>面板里的名字，只用于 F12 设置界面。</summary>
        internal static readonly string[] TierLabels = { "1 普通 · 灰", "2 少见 · 蓝", "3 稀有 · 紫", "4 史诗 · 橙", "5 传说 · 红" };

        /// <summary>2.3.0 及更早版本的键名，升级时要把这些旧键从配置文件里清掉。</summary>
        internal static readonly string[] LegacyTierLabels = { "1 普通 · 白", "2 少见 · 蓝", "3 稀有 · 紫", "4 史诗 · 橙", "5 传说 · 红" };

        /// <summary>
        /// 五档默认色。第 1 档 alpha = 0，也就是默认不画（普通物品占满仓库，全上色会很刺眼）。
        /// 第 1 档是中性灰而不是白：拉到可见时是一层淡淡的灰，比满屏白底干净。
        ///
        /// 注意：A 通道从 2.3.2 起只是个开关（0 = 不画，非 0 = 画），不再决定浓淡。
        /// 画出来有多透，五档统一听「背景不透明度」那一个滑块，这样满仓库不会一半浓一半淡。
        /// </summary>
        internal static readonly string[] DefaultHex = { "#8A8A8A00", "#2694DA", "#9F5ACF", "#FFA726", "#FF3B3B" };

        /// <summary>物品名跟着背景色变时，文字色的最低亮度，别再暗到看不清。</summary>
        private const float NameMinValue = 0.80f;

        private static readonly ConfigEntry<Color>[] ColorEntries = new ConfigEntry<Color>[5];

        /// <summary>档位 -> 最终背景色（已乘不透明度）。整条 alpha = 0 表示这一档不画，交还游戏原版配色。</summary>
        internal static readonly Color[] Palette = new Color[6];

        /// <summary>档位 -> 物品名字色（和背景同色系，但保证看得清）。</summary>
        internal static readonly Color[] NameColor = new Color[6];

        /// <summary>档位 -> 十六进制（只用于和服务端配色表对日志）。</summary>
        internal static readonly string[] HexByTier = new string[6];

        /// <summary>tpl -> 档位，来自服务端。1..5，没收录的就是 0。</summary>
        internal static readonly Dictionary<string, int> TplTier = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>服务端实际使用的名字与颜色，只用来打日志对比。</summary>
        internal static readonly Dictionary<string, string> ServerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, string> ServerColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string ServerVersion = "";

        /// <summary>本插件 dll 所在目录，读 tiers_export.json 用。</summary>
        internal static string PluginDir = "";

        /// <summary>档位表有没有成功拿到。没有就不改任何颜色，把游戏还原成原样。</summary>
        internal static bool TableReady;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;

            try
            {
                PluginDir = System.IO.Path.GetDirectoryName(Info.Location) ?? "";
            }
            catch
            {
                PluginDir = "";
            }

            MigrateFromShowFlags();
            MigrateLegacyTierLabels();

            EnabledEntry = Config.Bind("00 总开关", "启用按价值配色", true,
                "关掉就恢复游戏原本按类别的背景色。");

            AutoExamine.EnabledEntry = Config.Bind("00 总开关", "自动检视全部物品", true,
                "开启后所有物品一律当成已检视：装配界面 / 装备预设不会再报「无法应用未检视的物品」，图标也不再显示问号。每次进档自动重做，不写存档。");

            for (int i = 0; i < 5; i++)
            {
                ColorEntries[i] = Config.Bind("01 五档颜色",
                    TierLabels[i],
                    ParseHex(DefaultHex[i]),
                    "第 " + (i + 1) + " 档（" + TierKeys[i] + "）的颜色，格子背景和物品名字都跟它走。\n" +
                    "把最右边那个 A 拉到 0（或者用 Hex 填 00000000），这一档就直接不画，\n" +
                    "格子完全还原成游戏原本的样子，一点底色都不留。\n" +
                    "A 只决定画不画：只要不是 0，画出来的浓淡五档一致，统一由「02 高级 - 背景不透明度」决定。\n" +
                    "默认第 1 档「普通」就是 A = 0：满仓库绝大多数都是普通物品，全刷上色会很刺眼。");
                ColorEntries[i].SettingChanged += (_, __) => RebuildPalette();
            }

            LevelUpOnlyEntry = Config.Bind("02 高级", "配件最多升 1 档", true,
                "整件物品按「自己 + 配件里最高的那一档」上色，但最多只比本体高 1 档。\n" +
                "开着：白色枪身 + 红色瞄具 -> 蓝色（不会一步跳成红色）。\n" +
                "关掉：完全按最高档配件走，白色枪身 + 红色瞄具 -> 红色。");

            OpacityEntry = Config.Bind("02 高级", "背景不透明度", 0.75f,
                new ConfigDescription(
                    "五档共用的透明度，只管格子背景。1.00 = 最实；低于 0.4 会看不清图标。\n" +
                    "五档颜色写多少 A 都不影响浓淡，只决定这一档画不画（A = 0 不画）。",
                    new AcceptableValueRange<float>(0.15f, 1f)));

            BrightnessEntry = Config.Bind("02 高级", "背景亮度", 0.55f,
                new ConfigDescription(
                    "所有五档背景色的整体明暗。1.00 = 原色（最鲜）；0.55 = 默认，压暗但仍看得出来；0.20 = 接近黑灰，只剩一点点颜色。\n" +
                    "颜色由 Hue（色相）、Saturation（饱和度）、Value（亮度）三部分组成，调暗只改亮度，颜色种类不会变。",
                    new AcceptableValueRange<float>(0.10f, 1f)));

            SaturationEntry = Config.Bind("02 高级", "背景饱和度", 0.55f,
                new ConfigDescription(
                    "所有五档背景色的鲜艳程度。1.00 = 原色；0.55 = 默认；0.00 = 完全灰白，只剩明暗层次。\n" +
                    "想不刺眼就同时把亮度和饱和度都往下拉。",
                    new AcceptableValueRange<float>(0f, 1f)));

            NameColoredEntry = Config.Bind("02 高级", "物品名跟着变色", true,
                "格子背景上色时，格子里那行物品名也跟着变成同一色，免得暗色底上黑字看不清。\n" +
                "关掉就保持游戏原本的白字。");

            DebugEntry = Config.Bind("02 高级", "打印调试日志", false,
                "打开后每次算档都会写一行日志，排查用。平时关掉，否则日志会很大。");

            EnabledEntry.SettingChanged += (_, __) => RebuildPalette();
            OpacityEntry.SettingChanged += (_, __) => RebuildPalette();
            BrightnessEntry.SettingChanged += (_, __) => RebuildPalette();
            SaturationEntry.SettingChanged += (_, __) => RebuildPalette();
            NameColoredEntry.SettingChanged += (_, __) => RebuildPalette();

            RebuildPalette();

            var harmony = new Harmony(Guid);
            try
            {
                harmony.PatchAll(typeof(TierColorPlugin).Assembly);
                AutoExamine.Patch(harmony);
                LogSource.LogInfo("[ODT-TierColors] 2.3.2 已挂接：格子背景按 1-5 档实例级着色（透明度五档统一）。");
            }
            catch (Exception ex)
            {
                LogSource.LogError("[ODT-TierColors] 补丁失败：" + ex);
            }

            // 档位表要等服务器起来，放到协程里重试。
            StartCoroutine(LoadTierTableRoutine());
        }

        /// <summary>
        /// 2.2.0 的「显示 N 档背景」开关已经合并进各档颜色自己的 A 通道，这里把老配置搬过去再删掉。
        /// 老开关是关的（比如默认关着的第 1 档）就把该档 A 直接置 0，用户开着的话保持原来的颜色。
        /// </summary>
        private void MigrateFromShowFlags()
        {
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var showKey = new ConfigDefinition("01 五档颜色", "显示 " + (i + 1) + " 档背景");

                    ConfigEntry<bool> oldShow;
                    if (!Config.TryGetEntry(showKey, out oldShow))
                    {
                        continue;
                    }

                    var colorKey = new ConfigDefinition("01 五档颜色", LegacyTierLabels[i]);

                    ConfigEntry<Color> colorEntry;
                    if (Config.TryGetEntry(colorKey, out colorEntry) && !oldShow.Value)
                    {
                        Color c = colorEntry.Value;
                        c.a = 0f;
                        colorEntry.Value = c;
                    }

                    Config.Remove(showKey);
                }
            }
            catch (Exception ex)
            {
                LogSource.LogWarning("[ODT-TierColors] 迁移旧开关失败（不影响使用）：" + ex.Message);
            }
        }

        /// <summary>
        /// 2.3.1 把第 1 档从「白」改成「灰」，键名跟着变了。
        /// 旧键留着会让配置文件里出现两条「1 普通」，这里直接删掉，让新键按新默认值重建。
        /// </summary>
        private void MigrateLegacyTierLabels()
        {
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    if (string.Equals(LegacyTierLabels[i], TierLabels[i], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var legacyKey = new ConfigDefinition("01 五档颜色", LegacyTierLabels[i]);

                    ConfigEntry<Color> legacy;
                    if (Config.TryGetEntry(legacyKey, out legacy))
                    {
                        Config.Remove(legacyKey);
                    }
                }
            }
            catch (Exception ex)
            {
                LogSource.LogWarning("[ODT-TierColors] 清理旧档位键失败（不影响使用）：" + ex.Message);
            }
        }

        private void Update()
        {
            AutoExamine.Tick();
        }

        internal static void RebuildPalette()
        {
            // 五档共用的透明度：所有画出来的档位都用这一个值，不逐档各乘各的 A。
            float sharedAlpha = Mathf.Clamp(OpacityEntry == null ? 0.75f : OpacityEntry.Value, 0.15f, 1f);
            float brightness = Mathf.Clamp(BrightnessEntry == null ? 0.55f : BrightnessEntry.Value, 0.1f, 1f);
            float saturation = Mathf.Clamp(SaturationEntry == null ? 0.55f : SaturationEntry.Value, 0f, 1f);

            Palette[0] = Color.clear;
            NameColor[0] = Color.white;
            HexByTier[0] = "#FFFFFF";
            for (int i = 0; i < 5; i++)
            {
                Color raw = ColorEntries[i] == null ? ParseHex(DefaultHex[i]) : ColorEntries[i].Value;

                // 只动明暗和鲜艳程度，色相不变：白/蓝/紫/橙/红还是认得出来。
                Color shown = Shade(raw, brightness, saturation);

                // 这一档的开关就是它自己的 A：A = 0 就不画，格子完全还原成游戏原样。
                // A 非 0 时浓淡一律用 sharedAlpha，五档看起来才是一致的透明。
                bool drawn = raw.a > 0.001f;
                Palette[i + 1] = drawn
                    ? new Color(shown.r, shown.g, shown.b, sharedAlpha)
                    : Color.clear;

                // 名字用同色系，但提亮到看得清（背景已经按 brightness 压暗过，直接用会糊在底上）。
                NameColor[i + 1] = TextColorOf(shown);

                HexByTier[i + 1] = ToHex(raw);
            }

            // 已经画出来的格子要立刻换色
            foreach (var view in Resources.FindObjectsOfTypeAll<ItemView>())
            {
                try
                {
                    TierResolver.InvalidateSelf(view.Item);
                    view.UpdateColor();
                }
                catch
                {
                    // 场景切换时可能有半销毁的对象，忽略
                }
            }
        }

        #region 颜色工具

        /// <summary>
        /// 按 HSV 压暗 / 去饱和：只改 Value 与 Saturation，不动 Hue。
        /// 这样五档仍然是「白、蓝、紫、橙、红」，只是不那么扎眼。
        /// </summary>
        internal static Color Shade(Color c, float brightness, float saturation)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * saturation), Mathf.Clamp01(v * brightness), true);
        }

        /// <summary>
        /// 文字色：跟背景同一个色相，但把亮度顶到 NameMinValue 以上、饱和度收一点，
        /// 这样压在压暗过的背景上仍然清楚，也不会亮得上下一白一黑两条。
        /// 接近灰色的档位（第 1 档的灰）按背景明暗给一个中性浅灰，别一律甩成死白。
        /// </summary>
        internal static Color TextColorOf(Color shown)
        {
            Color.RGBToHSV(shown, out float h, out float s, out float v);

            if (s < 0.12f)
            {
                // 中性灰档位（第 1 档）跟着背景明暗走，别一律甩成死白。
                float grey = Mathf.Clamp(Mathf.Max(v, 0.72f), 0f, 1f);
                return new Color(grey, grey, grey, 1f);
            }

            float value = Mathf.Clamp(Mathf.Max(v, NameMinValue), 0f, 1f);
            float sat = Mathf.Clamp(s * 0.85f, 0f, 1f);
            return Color.HSVToRGB(h, sat, value, true);
        }

        internal static Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return Color.white;
            }

            string s = hex.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                s = s.Substring(1);
            }

            if (s.Length != 6 && s.Length != 8)
            {
                return Color.white;
            }

            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            {
                return Color.white;
            }

            if (s.Length == 6)
            {
                return new Color(
                    ((v >> 16) & 0xFF) / 255f,
                    ((v >> 8) & 0xFF) / 255f,
                    (v & 0xFF) / 255f,
                    1f);
            }

            return new Color(
                ((v >> 24) & 0xFF) / 255f,
                ((v >> 16) & 0xFF) / 255f,
                ((v >> 8) & 0xFF) / 255f,
                (v & 0xFF) / 255f);
        }

        internal static string ToHex(Color c)
        {
            return "#" + Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f).ToString("X2")
                       + Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f).ToString("X2")
                       + Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f).ToString("X2");
        }

        #endregion

        #region 档位表加载

        private static IEnumerator LoadTierTableRoutine()
        {
            const float RetrySeconds = 3f;
            const float GiveUpSeconds = 240f;

            float waited = 0f;
            int attempts = 0;

            yield return new WaitForSeconds(4f);

            while (!TableReady)
            {
                attempts++;
                if (TryLoadFromServer())
                {
                    break;
                }

                if (waited > 30f && attempts % 5 == 0 && TryLoadFromFile())
                {
                    break;
                }

                waited += RetrySeconds;
                if (waited > GiveUpSeconds)
                {
                    if (!TryLoadFromFile())
                    {
                        LogSource.LogWarning("[ODT-TierColors] 没拿到档位表（服务端路由 /iteminfo/tiers 与本地 tiers_export.json 都不可用）。" +
                                             "物品背景保持游戏原样。确认 ODT-ItemInfo 4.0 已装好并且服务端 mod 正常运行。");
                    }

                    yield break;
                }

                yield return new WaitForSeconds(RetrySeconds);
            }

            TableReady = true;
            LogSource.LogInfo("[ODT-TierColors] 档位表已就绪：版本 " + ServerVersion + "，收录 " + TplTier.Count + " 件物品。");
            RefreshAllViews(true);
        }

        private static void RefreshAllViews(bool log)
        {
            int count = 0;
            foreach (var view in Resources.FindObjectsOfTypeAll<ItemView>())
            {
                if (view == null)
                {
                    continue;
                }

                try
                {
                    view.UpdateColor();
                    count++;
                }
                catch
                {
                    // 半销毁的对象，忽略
                }
            }

            if (log)
            {
                LogSource.LogInfo("[ODT-TierColors] 已刷新 " + count + " 个格子。");
            }
        }

        private static bool TryLoadFromServer()
        {
            try
            {
                string json = SPT.Common.Http.RequestHandler.GetJson(TierRoute);
                return ApplyTable(json, "服务端路由");
            }
            catch (Exception ex)
            {
                if (DebugEntry != null && DebugEntry.Value)
                {
                    LogSource.LogWarning("[ODT-TierColors] 拉取 " + TierRoute + " 失败：" + ex.Message);
                }

                return false;
            }
        }

        /// <summary>服务端拉不到时，退回去读它写在 mod 目录下的导出文件。</summary>
        private static bool TryLoadFromFile()
        {
            var candidates = new List<string>();

            // E:\GAME\BepInEx\plugins\ODT-TierColors  ->  E:\GAME\SPT_Runtime\...
            if (!string.IsNullOrEmpty(PluginDir))
            {
                candidates.Add(System.IO.Path.Combine(PluginDir, "tiers_export.json"));

                // 4.0 时代的服务端目录叫 SPT，4.1 起统一叫 SPT_Runtime，两个都试。
                foreach (string serverDir in new[] { "SPT_Runtime", "SPT" })
                {
                    candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(
                        PluginDir, "..", "..", "..", serverDir, "user", "mods", "ODT-ItemInfo-4.0", "tiers_export.json")));
                }

                // 插件直接放在游戏根目录下的兜底：<PluginDir>....
                candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    PluginDir, "..", "..", "user", "mods", "ODT-ItemInfo-4.0", "tiers_export.json")));
            }

            foreach (string path in candidates)
            {
                try
                {
                    if (!System.IO.File.Exists(path))
                    {
                        continue;
                    }

                    string json = System.IO.File.ReadAllText(path, Encoding.UTF8);
                    if (ApplyTable(json, path))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogSource.LogWarning("[ODT-TierColors] 读取 " + path + " 失败：" + ex.Message);
                }
            }

            return false;
        }

        private static bool ApplyTable(string json, string source)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                LogSource.LogWarning("[ODT-TierColors] 档位表不是合法 JSON（" + source + "）：" + ex.Message);
                return false;
            }

            JObject tiers = FindProperty(root, "tiers") as JObject;
            if (tiers == null || tiers.Count == 0)
            {
                return false;
            }

            TplTier.Clear();
            foreach (var kvp in tiers)
            {
                if (kvp.Value == null)
                {
                    continue;
                }

                int tier;
                try
                {
                    tier = kvp.Value.Value<int>();
                }
                catch
                {
                    continue;
                }

                if (tier >= 1 && tier <= 5)
                {
                    TplTier[kvp.Key] = tier;
                }
            }

            if (TplTier.Count == 0)
            {
                return false;
            }

            ServerNames.Clear();
            ServerColors.Clear();
            var names = FindProperty(root, "names") as JObject;
            if (names != null)
            {
                foreach (var kvp in names)
                {
                    ServerNames[kvp.Key] = kvp.Value == null ? "" : kvp.Value.ToString();
                }
            }

            var colors = FindProperty(root, "colors") as JObject;
            if (colors != null)
            {
                foreach (var kvp in colors)
                {
                    ServerColors[kvp.Key] = kvp.Value == null ? "" : kvp.Value.ToString();
                }
            }

            var version = FindProperty(root, "version");
            ServerVersion = version == null ? "" : version.ToString();

            TierResolver.ClearCache();
            LogSource.LogInfo("[ODT-TierColors] 档位表来自 " + source + "，版本 " + ServerVersion + "，共 " + TplTier.Count + " 件。");

            CompareServerColors();
            return true;
        }

        /// <summary>
        /// 服务端下发的名字色和本地背景色本来就不是一回事（服务端那套是物品名的富文本颜色），
        /// 只有开了「打印调试日志」才逐档打印，免得每次开局刷一堆没用的日志。
        /// </summary>
        private static void CompareServerColors()
        {
            if (DebugEntry == null || !DebugEntry.Value)
            {
                return;
            }

            for (int i = 0; i < 5; i++)
            {
                string serverHex;
                if (!ServerColors.TryGetValue(TierKeys[i], out serverHex) || string.IsNullOrEmpty(serverHex))
                {
                    continue;
                }

                if (!string.Equals(serverHex.Trim(), HexByTier[i + 1], StringComparison.OrdinalIgnoreCase))
                {
                    LogSource.LogInfo("[ODT-TierColors] " + TierKeys[i] + " 服务端配色 " + serverHex +
                                      "，本地自定义为 " + HexByTier[i + 1] + "（以本地设置优先）。");
                }
            }
        }

        private static JToken FindProperty(JObject root, string name)
        {
            foreach (var prop in root.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value;
                }
            }

            return null;
        }

        #endregion

        #region 档位解析

        /// <summary>
        /// 一件物品显示成哪一档：先看它自己，再看它身上所有插槽里的配件，取最高的那个。
        /// 插槽 = Slot；弹匣里的子弹、背包里的东西走 Grid，不算进来。
        /// </summary>
        internal static class TierResolver
        {
            private sealed class Box
            {
                public int Tier;
                public bool Dirty = true;
            }

            private static System.Runtime.CompilerServices.ConditionalWeakTable<Item, Box> Cache =
                new System.Runtime.CompilerServices.ConditionalWeakTable<Item, Box>();

            private static Box BoxOf(Item item)
            {
                Box box;
                if (!Cache.TryGetValue(item, out box))
                {
                    box = new Box();
                    Cache.Add(item, box);
                }

                return box;
            }

            /// <summary>只作废自己（不改父链），档位显示开关变更时用，避开物品正在移动时的地址异常。</summary>
            internal static void InvalidateSelf(Item item)
            {
                if (item == null)
                {
                    return;
                }

                Box box;
                if (Cache.TryGetValue(item, out box))
                {
                    box.Dirty = true;
                }
            }

            internal static void ClearCache()
            {
                // net472 的 ConditionalWeakTable 没有 Clear()，直接换一张新表
                Cache = new System.Runtime.CompilerServices.ConditionalWeakTable<Item, Box>();
            }

            internal static void Invalidate(Item item)
            {
                if (item == null)
                {
                    return;
                }

                Box box;
                if (Cache.TryGetValue(item, out box))
                {
                    box.Dirty = true;
                }

                // 配件变了，整条父链一起作废：装/拆配件时 GameObjects 会给父物品也发刷新事件，
                // 这里主动作废，避免同一帧里「先算旧值再算新值」。
                try
                {
                    if (item.CurrentAddress != null)
                    {
                        foreach (var parent in item.CurrentAddress.GetAllParentItems(true))
                        {
                            Box box2;
                            if (parent != null && Cache.TryGetValue(parent, out box2))
                            {
                                box2.Dirty = true;
                            }
                        }
                    }
                }
                catch
                {
                    // 物品正在被销毁/移动时地址可能暂时无效，忽略
                }
            }

            internal static int Resolve(Item item)
            {
                if (!TableReady || item == null || EnabledEntry == null || !EnabledEntry.Value)
                {
                    return 0;
                }

                Box box = BoxOf(item);
                if (!box.Dirty)
                {
                    return box.Tier;
                }

                int tier = Compute(item);
                box.Tier = tier;
                box.Dirty = false;

                if (DebugEntry != null && DebugEntry.Value)
                {
                    LogSource.LogInfo("[ODT-TierColors] " + item.Name + " [" + item.TemplateId + "] -> " + tier + " 档");
                }

                return tier;
            }

            private static int Compute(Item item)
            {
                int self = OwnTier(item);
                if (self == 0)
                {
                    // 这件物品本身没有档位（任务物品 / 货币 / 手册无价），保持游戏原色
                    return 0;
                }

                int best = self;
                Collect(item, ref best, 0);

                bool cap = LevelUpOnlyEntry != null && LevelUpOnlyEntry.Value;
                if (cap && best > self + 1)
                {
                    best = self + 1;
                }

                return best;
            }

            private static void Collect(Item item, ref int best, int depth)
            {
                if (depth > 8 || item == null)
                {
                    return;
                }

                var compound = item as CompoundItem;
                if (compound == null)
                {
                    return;
                }

                IEnumerable<IContainer> containers;
                try
                {
                    containers = compound.Containers;
                }
                catch
                {
                    return;
                }

                if (containers == null)
                {
                    return;
                }

                foreach (var container in containers)
                {
                    // 只走插槽。弹匣里的子弹、背包里的东西在 Grid 里，不算「配件」。
                    if (!(container is EFT.InventoryLogic.Slot) || container.Items == null)
                    {
                        continue;
                    }

                    foreach (var child in container.Items)
                    {
                        if (child == null)
                        {
                            continue;
                        }

                        int childTier = Resolve(child);
                        if (childTier > best)
                        {
                            best = childTier;
                        }

                        Collect(child, ref best, depth + 1);
                    }
                }
            }

            private static int OwnTier(Item item)
            {
                int tier;
                if (item == null)
                {
                    return 0;
                }

                string tpl = null;
                try
                {
                    tpl = item.TemplateId;
                }
                catch
                {
                    return 0;
                }

                if (string.IsNullOrEmpty(tpl))
                {
                    return 0;
                }

                return TplTier.TryGetValue(tpl, out tier) ? tier : 0;
            }
        }

        #endregion

        #region Harmony 补丁

        /// <summary>
        /// 所有格子最终都走这里上色：普通状态、鼠标悬停、子类（SelectableSlotItemView 等）全部覆盖。
        /// </summary>
        [HarmonyPatch(typeof(ItemView), nameof(ItemView.UpdateColor))]
        internal static class ItemViewUpdateColorPatch
        {
            /// <summary>格子里那行物品名（GridItemView 的 Caption）。别的 ItemView 子类没有，取到 null 就跳过。</summary>
            private static readonly System.Reflection.FieldInfo CaptionField =
                AccessTools.Field(typeof(GridItemView), "Caption");

            private static readonly System.Reflection.FieldInfo BottomPanelField =
                AccessTools.Field(typeof(ItemView), "BottomPanel");

            private static readonly System.Reflection.FieldInfo InscriptionField =
                AccessTools.Field(typeof(EFT.UI.DragAndDrop.ItemViewBottomPanel), "ItemInscription");

            /// <summary>名字改成跟背景同一档的颜色。背景已经上过色才调这个。</summary>
            internal static void ApplyNameColor(ItemView view, int tier)
            {
                if (view == null || tier < 1 || tier > 5)
                {
                    return;
                }

                if (NameColoredEntry == null || !NameColoredEntry.Value)
                {
                    RestoreNameColor(view);
                    return;
                }

                Color c = NameColor[tier];
                c.a = 1f;

                object bottomPanel = BottomPanelField == null ? null : BottomPanelField.GetValue(view);
                RememberOriginals(view, bottomPanel);

                SetTextColor(CaptionField, view, c);
                SetTextColor(InscriptionField, bottomPanel, c);
            }

            /// <summary>第一次改名字前的原文颜色，还原时用。</summary>
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ItemView, Color[]> Originals =
                new System.Runtime.CompilerServices.ConditionalWeakTable<ItemView, Color[]>();

            /// <summary>这一档不上色（A = 0）时，把名字交还游戏原本的颜色。</summary>
            internal static void RestoreNameColor(ItemView view)
            {
                if (view == null)
                {
                    return;
                }

                Color[] original;
                if (!Originals.TryGetValue(view, out original))
                {
                    return;
                }

                SetTextColor(CaptionField, view, original[0]);
                SetTextColor(InscriptionField, BottomPanelField == null ? null : BottomPanelField.GetValue(view), original[1]);
            }

            /// <summary>记下第一次改之前两条文字的颜色，之后还原就用它，不用猜游戏原本是什么颜色。</summary>
            private static void RememberOriginals(ItemView view, object bottomPanel)
            {
                Color[] store;
                if (Originals.TryGetValue(view, out store))
                {
                    return;
                }

                store = new[] { new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f) };

                try
                {
                    if (CaptionField != null && CaptionField.GetValue(view) is TMPro.TMP_Text caption && caption != null)
                    {
                        store[0] = caption.color;
                    }
                }
                catch
                {
                    // 拿不到就用白色兜底
                }

                try
                {
                    if (InscriptionField != null && bottomPanel != null &&
                        InscriptionField.GetValue(bottomPanel) is TMPro.TMP_Text inscription && inscription != null)
                    {
                        store[1] = inscription.color;
                    }
                }
                catch
                {
                    // 同上
                }

                Originals.Add(view, store);
            }

            private static void SetTextColor(System.Reflection.FieldInfo field, object owner, Color color)
            {
                if (field == null || owner == null)
                {
                    return;
                }

                try
                {
                    if (field.GetValue(owner) is TMPro.TMP_Text text && text != null)
                    {
                        text.color = color;
                    }
                }
                catch
                {
                    // 面板正在销毁时取不到，忽略
                }
            }

            private static readonly System.Reflection.FieldInfo HighlightedField =
                AccessTools.Field(typeof(ItemView), "Highlighted");

            private static readonly System.Reflection.FieldInfo HighlightedGloballyField =
                AccessTools.Field(typeof(ItemView), "HighlightedGlobally");

            private static bool ReadFlag(System.Reflection.FieldInfo field, ItemView view)
            {
                if (field == null)
                {
                    return false;
                }

                try
                {
                    return field.GetValue(view) is bool b && b;
                }
                catch
                {
                    return false;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(ItemView __instance)
            {
                try
                {
                    if (__instance == null || __instance.ColorPanel == null)
                    {
                        return;
                    }

                    int tier = TierResolver.Resolve(__instance.Item);
                    if (tier <= 0)
                    {
                        RestoreNameColor(__instance);
                        return;
                    }

                    Color baseColor = Palette[tier];

                    // 这一档不画背景：背景和名字都放过，让游戏用它自己的原版配色。
                    if (baseColor.a <= 0.01f)
                    {
                        RestoreNameColor(__instance);
                        return;
                    }

                    bool highlighted = ReadFlag(HighlightedField, __instance) || ReadFlag(HighlightedGloballyField, __instance);

                    // 高亮只提亮 RGB，A 不动：透明度五档统一，悬停也不该突然变实。
                    Color final = highlighted
                        ? new Color(
                            Mathf.Lerp(baseColor.r, 1f, 0.45f),
                            Mathf.Lerp(baseColor.g, 1f, 0.45f),
                            Mathf.Lerp(baseColor.b, 1f, 0.45f),
                            baseColor.a)
                        : baseColor;

                    __instance.ColorPanel.color = final;

                    // 名字跟着背景色走：暗底上黑字根本看不见，同一个色系但提亮过。
                    ApplyNameColor(__instance, tier);

                    // 记下「这个色块属于哪个 ItemView」，装弹闪烁要用
                    ItemViewAnimationPatch.Remember(__instance.ColorPanel, __instance);
                }
                catch (Exception ex)
                {
                    if (DebugEntry != null && DebugEntry.Value)
                    {
                        LogSource.LogWarning("[ODT-TierColors] 上色失败：" + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// 装弹 / 使用物品时会有闪烁动画，它最后会把色块刷回模板色。
        /// 这里在动画初始化时把「默认色」换成档位色，闪烁结束就落回正确颜色。
        /// </summary>
        [HarmonyPatch(typeof(ItemViewAnimation), nameof(ItemViewAnimation.Init))]
        internal static class ItemViewAnimationPatch
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, ItemViewHolder> Owners =
                new System.Runtime.CompilerServices.ConditionalWeakTable<Image, ItemViewHolder>();

            private sealed class ItemViewHolder
            {
                public WeakReference<ItemView> View;
            }

            internal static void Remember(Image panel, ItemView view)
            {
                if (panel == null || view == null)
                {
                    return;
                }

                ItemViewHolder holder;
                if (!Owners.TryGetValue(panel, out holder))
                {
                    holder = new ItemViewHolder();
                    Owners.Add(panel, holder);
                }

                holder.View = new WeakReference<ItemView>(view);
            }

            private static ItemView FindOwner(Image panel)
            {
                ItemViewHolder holder;
                if (panel != null && Owners.TryGetValue(panel, out holder) && holder.View != null)
                {
                    ItemView view;
                    if (holder.View.TryGetTarget(out view) && view != null)
                    {
                        return view;
                    }
                }

                // 兜底：扫一遍现有格子，按色块引用反查
                if (panel == null)
                {
                    return null;
                }

                foreach (var candidate in Resources.FindObjectsOfTypeAll<ItemView>())
                {
                    if (candidate != null && ReferenceEquals(candidate.ColorPanel, panel))
                    {
                        Remember(panel, candidate);
                        return candidate;
                    }
                }

                return null;
            }

            [HarmonyPrefix]
            private static void Prefix(ItemViewAnimation __instance, ref Color backgroundColor)
            {
                try
                {
                    if (__instance == null || __instance._colorPanel == null)
                    {
                        return;
                    }

                    ItemView view = FindOwner(__instance._colorPanel);
                    if (view == null)
                    {
                        return;
                    }

                    int tier = TierResolver.Resolve(view.Item);
                    if (tier > 0 && Palette[tier].a > 0.01f)
                    {
                        backgroundColor = Palette[tier];
                    }
                }
                catch
                {
                    // 动画兜底不能影响主流程
                }
            }
        }

        /// <summary>
        /// 拆件、装件、换弹之后 GameObjects 只调 UpdateInfo，不会重新上色。这里补一次。
        /// </summary>
        [HarmonyPatch(typeof(GridItemView), "OnRefreshItem")]
        internal static class ItemViewOnRefreshItemPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ItemView __instance)
            {
                try
                {
                    if (__instance == null)
                    {
                        return;
                    }

                    TierResolver.Invalidate(__instance.Item);
                    __instance.UpdateColor();
                }
                catch
                {
                    // 刷新失败不影响游戏
                }
            }
        }

        #endregion
    }
}
