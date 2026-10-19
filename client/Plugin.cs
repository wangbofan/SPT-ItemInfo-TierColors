using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JsonType;
using TMPro;
using UnityEngine;

namespace OdtTierColors
{
    /// <summary>
    /// 一个颜色管两处：物品格子背景色（TaxonomyColor -> Color）和物品名字的颜色
    /// （服务端写进语言表的 &lt;color=#RRGGBB&gt;）。F12 里改一个色，两处一起变。
    /// 纯客户端行为，不动服务器数据、不发网络请求。
    /// </summary>
    [BepInPlugin(Guid, PluginName, PluginVersion)]
    public sealed class TierColorPlugin : BaseUnityPlugin
    {
        public const string Guid = "justnu.odt.tiercolors";
        public const string PluginName = "ODT 物品等级配色";
        public const string PluginVersion = "1.6.0";

        internal static ManualLogSource LogSource = null!;
        internal static ConfigEntry<bool> EnabledEntry = null!;
        internal static ConfigEntry<float> OpacityEntry = null!;

        internal static readonly Dictionary<TaxonomyColor, ConfigEntry<Color>> ColorEntries = new Dictionary<TaxonomyColor, ConfigEntry<Color>>();
        internal static readonly Dictionary<TaxonomyColor, Color> Palette = new Dictionary<TaxonomyColor, Color>();
        internal static readonly Dictionary<string, string> CurrentHexByTier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 十档，面板顺序就是 1 到 10：枚举值 / 面板名 / 默认色 / 一句话判据。
        /// 1-6 是价格阶梯（白绿蓝紫橙金），7-9 是特殊状态，10 是兜底。
        /// </summary>
        private static readonly TierDef[] Tiers =
        {
            new TierDef("01", "COMMON",      "普通",            TaxonomyColor.@default,     "#FFFFFF", "商人 1 级就在卖；没人卖时每格手册价 < 1 万"),
            new TierDef("02", "RARE",        "稀有",            TaxonomyColor.blue,         "#4ADE80", "商人 2 级才卖；没人卖时每格 1 - 2 万"),
            new TierDef("03", "EPIC",        "史诗",            TaxonomyColor.violet,       "#4D9DFF", "商人 3 级才卖；没人卖时每格 2 - 3 万"),
            new TierDef("04", "LEGENDARY",   "传说",            TaxonomyColor.yellow,       "#C084FC", "商人 4 级才卖；没人卖时每格 3 - 4 万"),
            new TierDef("05", "UBER",        "顶级",            TaxonomyColor.tracerYellow, "#FF8A00", "4 级商人兑换（兑换再 +1 档）；没人卖时每格 4 - 6 万"),
            new TierDef("06", "UNOBTAINIUM", "稀世",            TaxonomyColor.tracerGreen,  "#FFE066", "武器护甲弹药配件且商人完全无货；或每格 >= 6 万"),
            new TierDef("07", "OVERPOWERED", "禁售 · 商人有货", TaxonomyColor.tracerRed,    "#FF3B3B", "跳蚤禁售，但商人还在卖。当前 197 件。老版本 ODT 管它叫「超模」"),
            new TierDef("08", "CUSTOM",      "超模 · 酸绿弹",   TaxonomyColor.red,          "#A8FF00", "服务端手工指定，全库只有 26x75 酸绿照明弹 1 件（不是算出来的）"),
            new TierDef("09", "CUSTOM2",     "禁售 · 商人无货", TaxonomyColor.orange,       "#FF2E93", "跳蚤禁售，而且所有商人一件都不卖，买不到也卖不掉。当前 356 件"),
            new TierDef("10", "UNKNOWN",     "未知",            TaxonomyColor.green,        "#9AA3AD", "其它枚举值，正常遇不到"),
        };

        private sealed class TierDef
        {
            public TierDef(string order, string key, string label, TaxonomyColor value, string defaultHex, string rule)
            {
                Order = order;
                Key = key;
                Label = label;
                Value = value;
                DefaultHex = defaultHex;
                Rule = rule;
            }

            public string Order { get; }
            public string Key { get; }
            public string Label { get; }
            public TaxonomyColor Value { get; }
            public string DefaultHex { get; }
            public string Rule { get; }
        }

        private void Awake()
        {
            LogSource = Logger;

            EnabledEntry = Config.Bind("00 总开关", "启用配色", true,
                "关掉就恢复游戏原本的按类别上色，物品名字也恢复服务端原本的颜色。");

            AutoExamine.EnabledEntry = Config.Bind("00 总开关", "自动检视全部物品", true,
                "开启后，所有物品一律当成「已检视」：装配界面 / 装备预设不会再报「无法应用未检视的物品」，图标也不再显示问号。每次进档自动重做，不写存档。");


            foreach (var t in Tiers)
            {
                var entry = Config.Bind("01 等级颜色",
                    string.Format("{0} {1} ({2})", t.Order, t.Label, t.Key),
                    ParseHex(t.DefaultHex),
                    t.Rule + " ｜ 这个颜色同时用于格子背景和物品名字。默认 " + t.DefaultHex);
                ColorEntries[t.Value] = entry;
                entry.SettingChanged += (_, __) => Rebuild();
            }

            OpacityEntry = Config.Bind("02 高级", "背景不透明度", 1f,
                new ConfigDescription("只管格子背景，1.00 = 实色最鲜艳；低于 0.35 会发灰。物品名字永远是实色。",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            EnabledEntry.SettingChanged += (_, __) => Rebuild();
            OpacityEntry.SettingChanged += (_, __) => Rebuild();

            Rebuild();
            DumpVanilla();

            try
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(TierColorPlugin).Assembly);
                PatchNames(harmony);
                AutoExamine.Patch(harmony);
                LogSource.LogInfo("[ODT-TierColors] 已挂接：格子背景色 + 物品名字颜色 + 自动检视。");
            }
            catch (Exception ex)
            {
                LogSource.LogError("[ODT-TierColors] 补丁失败：" + ex);
            }
        }

        private void Update()
        {
            AutoExamine.Tick();
        }

        private static void Rebuild()
        {
            Palette.Clear();
            CurrentHexByTier.Clear();
            float opacity = Mathf.Clamp(OpacityEntry.Value, 0.05f, 1f);

            foreach (var t in Tiers)
            {
                ConfigEntry<Color> entry;
                if (!ColorEntries.TryGetValue(t.Value, out entry))
                {
                    continue;
                }

                var c = entry.Value;
                Palette[t.Value] = new Color(c.r, c.g, c.b, opacity);
                CurrentHexByTier[t.Key] = ToHex(c);
            }
        }

        /// <summary>物品名字走的是 TMP 文本，给所有 set_text / SetText(string, ...) 挂前缀改颜色。</summary>
        private static void PatchNames(Harmony harmony)
        {
            var targets = new List<MethodInfo>();
            var seen = new HashSet<MethodInfo>();

            var prop = typeof(TMP_Text).GetProperty("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var setter = prop == null ? null : prop.GetSetMethod(true);
            if (setter != null && seen.Add(setter))
            {
                targets.Add(setter);
            }

            foreach (var m in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "SetText")
                {
                    continue;
                }
                var ps = m.GetParameters();
                if (ps.Length > 0 && ps[0].ParameterType == typeof(string) && seen.Add(m))
                {
                    targets.Add(m);
                }
            }

            var prefix = new HarmonyMethod(typeof(NamePatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static));
            int ok = 0;
            foreach (var m in targets)
            {
                try
                {
                    harmony.Patch(m, prefix: prefix);
                    ok++;
                }
                catch (Exception ex)
                {
                    LogSource.LogWarning("[ODT-TierColors] 文本方法挂接失败 " + m.Name + "：" + ex.Message);
                }
            }
            LogSource.LogInfo("[ODT-TierColors] 物品名字换色已挂接 " + ok + "/" + targets.Count + " 个文本方法。");
        }

        private static void DumpVanilla()
        {
            try
            {
                var field = typeof(TaxonomyColorExtension).GetField("ColorMapping",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var map = field == null ? null : field.GetValue(null) as Dictionary<TaxonomyColor, Color>;
                if (map == null)
                {
                    return;
                }
                var sb = new System.Text.StringBuilder("[ODT-TierColors] 游戏原本枚举色：");
                foreach (var kv in map)
                {
                    sb.Append(' ').Append(kv.Key).Append('=').Append(ToHex(kv.Value)).Append(';');
                }
                LogSource.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                LogSource.LogDebug("[ODT-TierColors] 读取原本枚举色失败：" + ex.Message);
            }
        }

        internal static Color ParseHex(string hex)
        {
            Color parsed;
            return ColorUtility.TryParseHtmlString(hex, out parsed) ? parsed : Color.white;
        }

        internal static string ToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", Mathf.RoundToInt(c.r * 255f), Mathf.RoundToInt(c.g * 255f), Mathf.RoundToInt(c.b * 255f));
        }
    }

    /// <summary>物品名字：把服务端写进文本的等级源色换成玩家当前选的颜色。</summary>
    internal static class NamePatch
    {
        private static void Prefix(ref string __0)
        {
            if (string.IsNullOrEmpty(__0) || !TierColorPlugin.EnabledEntry.Value)
            {
                return;
            }
            __0 = NameRemap.Apply(__0, TierColorPlugin.CurrentHexByTier);
        }
    }

    [HarmonyPatch(typeof(TaxonomyColorExtension), "ToColor")]
    internal static class ToColorPatch
    {
        private static bool Prepare()
        {
            bool ok = AccessTools.Method(typeof(TaxonomyColorExtension), "ToColor", new[] { typeof(TaxonomyColor) }) != null;
            if (!ok)
            {
                TierColorPlugin.LogSource.LogError("[ODT-TierColors] 找不到 TaxonomyColorExtension.ToColor，格子背景色未生效。");
            }
            return ok;
        }

        private static void Postfix(TaxonomyColor __0, ref Color __result)
        {
            if (!TierColorPlugin.EnabledEntry.Value)
            {
                return;
            }
            Color custom;
            if (TierColorPlugin.Palette.TryGetValue(__0, out custom))
            {
                __result = custom;
            }
        }
    }
}
