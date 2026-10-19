using System;
using System.Collections.Generic;

namespace ItemInfo.Models;

/// <summary>
/// EFT 0.16.x / SPT 4.1 中，物品背景色 <c>TemplateItemProperties.BackgroundColor</c> 在客户端是
/// <c>JSType.TaxonomyColor</c> 枚举。服务端把这个字段原样序列化给客户端，所以它只能是枚举名；
/// 写 "#FFFFFF" 这类十六进制值，客户端解析 /client/items 时就会抛
/// <c>Error converting value "#FFFFFF" to type 'JSType.TaxonomyColor'</c>，玩家直接卡在报错框进不了游戏。
///
/// 本类把稀有度颜色收敛到客户端一定认识的形式：
///   * config/tiers.json     —— 背景色（枚举名，客户端只接受下面这 12 个值）
///   * config/tiers_hex.json —— 物品名 / 简称的富文本颜色（十六进制，Unity 富文本支持）
/// 配置写错只会退化成 default 并打印一次警告，不会再让客户端解析失败。
/// </summary>
public static class RarityColors
{
    /// <summary>
    /// 客户端 <c>JsonType.TaxonomyColor</c> 的全部成员（大小写不敏感，这里采用游戏内的标准写法）。
    /// </summary>
    public static readonly string[] BackgroundColors =
    {
        "default", "blue", "green", "grey", "yellow", "orange",
        "red", "violet", "black", "tracerYellow", "tracerGreen", "tracerRed"
    };

    /// <summary>
    /// 会写进 BackgroundColor 的稀有度键（用于启动时预检）。
    /// </summary>
    public static readonly string[] TierKeys =
    {
        "COMMON", "RARE", "EPIC", "LEGENDARY", "UBER",
        "UNOBTAINIUM", "OVERPOWERED", "CUSTOM", "CUSTOM2", "UNKNOWN"
    };

    /// <summary>
    /// 枚举名 -> 富文本十六进制。只有 tiers_hex.json 里写的是枚举名（而不是 #RRGGBB）时才用得到，
    /// 颜色是近似值，仅影响物品名着色；背景色本身由游戏按枚举取色。
    /// </summary>
    private static readonly Dictionary<string, string> HexByColor = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = "#FFFFFF",
        ["blue"] = "#4A90D9",
        ["green"] = "#4CD07D",
        ["grey"] = "#8C8C8C",
        ["yellow"] = "#E8D24A",
        ["orange"] = "#E8913C",
        ["red"] = "#D94A4A",
        ["violet"] = "#9F5ACF",
        ["black"] = "#3C3C3C",
        ["tracerYellow"] = "#FFE633",
        ["tracerGreen"] = "#66FF66",
        ["tracerRed"] = "#FF6666"
    };

    private static readonly HashSet<string> WarnedKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>由模组启动时注入的警告输出（ISptLogger.Warning）；未注入时静默。</summary>
    public static Action<string>? Warn { get; set; }

    /// <summary>
    /// 背景色：优先用 tiers.json 里的枚举名；其次兼容旧配置——tiers_hex.json 里直接写枚举名。
    /// 十六进制一律不返回，从根上避免客户端解析 /client/items 失败。
    /// </summary>
    public static string Background(IReadOnlyDictionary<string, string>? tiers,
                                    IReadOnlyDictionary<string, string>? tiersHex,
                                    string tierKey)
    {
        if (tiers is not null && tiers.TryGetValue(tierKey, out string? valueFromTiers) &&
            TryGetColorName(valueFromTiers, out string? colorName))
        {
            return colorName!;
        }

        if (tiersHex is not null && tiersHex.TryGetValue(tierKey, out string? valueFromHex) &&
            TryGetColorName(valueFromHex, out colorName))
        {
            return colorName!;
        }

        string? rawValue = null;
        if (tiers is not null && tiers.TryGetValue(tierKey, out string? rawTiers))
            rawValue = rawTiers;
        else if (tiersHex is not null && tiersHex.TryGetValue(tierKey, out string? rawHex))
            rawValue = rawHex;

        ReportInvalid(tierKey, rawValue);
        return "default";
    }

    /// <summary>
    /// 物品名 / 简称的富文本颜色：优先 tiers_hex.json；那里如果写的是枚举名，转成近似十六进制。
    /// </summary>
    public static string Text(IReadOnlyDictionary<string, string>? tiersHex,
                              IReadOnlyDictionary<string, string>? tiers,
                              string tierKey)
    {
        if (tiersHex is not null && tiersHex.TryGetValue(tierKey, out string? valueFromHex))
        {
            if (TryGetHex(valueFromHex, out string? hex))
                return hex!;

            if (TryGetColorName(valueFromHex, out string? colorName))
                return HexByColor[colorName!];
        }

        if (tiers is not null && tiers.TryGetValue(tierKey, out string? valueFromTiers) &&
            TryGetColorName(valueFromTiers, out string? fallbackName))
        {
            return HexByColor[fallbackName!];
        }

        return "#FFFFFF";
    }

    /// <summary>
    /// 启动时调用一次：把所有配置里的非法背景色提前暴露到日志里，而不是等玩家进游戏时报错。
    /// </summary>
    public static void Validate(IReadOnlyDictionary<string, string>? tiers,
                                IReadOnlyDictionary<string, string>? tiersHex)
    {
        foreach (string tierKey in TierKeys)
            _ = Background(tiers, tiersHex, tierKey);
    }

    private static bool TryGetColorName(string? value, out string? colorName)
    {
        colorName = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string candidateValue = value.Trim();
        foreach (string candidate in BackgroundColors)
        {
            if (string.Equals(candidate, candidateValue, StringComparison.OrdinalIgnoreCase))
            {
                colorName = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetHex(string? value, out string? hex)
    {
        hex = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string candidate = value.Trim();
        if (candidate.StartsWith('#'))
            candidate = candidate[1..];

        if (candidate.Length != 6 && candidate.Length != 8)
            return false;

        foreach (char character in candidate)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        hex = "#" + candidate[..6].ToUpperInvariant();
        return true;
    }

    private static void ReportInvalid(string tierKey, string? rawValue)
    {
        if (!WarnedKeys.Add(tierKey))
            return;

        Warn?.Invoke("[ItemInfo] RarityRecolor: \"" + tierKey + "\" 的背景色 \"" + rawValue +
                     "\" 不是有效的 TaxonomyColor，已按 default 处理。可用值：" +
                     string.Join(", ", BackgroundColors) +
                     "（十六进制颜色只用于 tiers_hex.json 的物品名着色）");
    }
}
