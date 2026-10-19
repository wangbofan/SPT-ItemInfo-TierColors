#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace OdtTierColors
{
    /// <summary>
    /// 物品名字的换色：服务端把 &lt;color=#RRGGBB&gt; 写进语言表，
    /// 这里把「已知的等级源色」换成玩家当前在 F12 里选的颜色。
    /// 纯字符串处理，不依赖 Unity，方便单独测试。
    /// </summary>
    internal static class NameRemap
    {
        /// <summary>十个等级，顺序与面板一致。</summary>
        public static readonly string[] TierKeys =
        {
            "COMMON", "RARE", "EPIC", "LEGENDARY", "UBER",
            "UNOBTAINIUM", "OVERPOWERED", "CUSTOM", "CUSTOM2", "UNKNOWN"
        };

        /// <summary>历代随包发布过的配色 + ODT 原版配色，都当作「等级身份令牌」。</summary>
        private static readonly string[][] Palettes =
        {
            // v1.4（当前默认）
            new[] { "FFFFFF", "4ADE80", "4D9DFF", "C084FC", "FF8A00", "FFE066", "FF3B3B", "A8FF00", "FF2E93", "9AA3AD" },
            // v1.3
            new[] { "FFFFFF", "38B6FF", "C77DFF", "FFF66B", "FFD633", "4DFF3C", "FF5A5A", "A8FF00", "FF9F2E", "2EE06B" },
            // v1.0 / v1.1 / v1.2
            new[] { "FFFFFF", "38B6FF", "C77DFF", "FFF66B", "FFD633", "4DFF3C", "FF5A5A", "FF4D5E", "FF9F2E", "2EE06B" },
            // ODT 原版 tiers_hex.json
            new[] { "FFFFFF", "2694DA", "9F5ACF", "F6F15D", "FFD700", "39FF14", "FF4040", "CA1F2B", "CA741F", "10A43A" }
        };

        private static readonly Dictionary<string, string> SourceToTier = BuildIndex();

        private static Dictionary<string, string> BuildIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var palette in Palettes)
            {
                for (int i = 0; i < palette.Length && i < TierKeys.Length; i++)
                {
                    if (!index.ContainsKey(palette[i]))
                    {
                        index[palette[i]] = TierKeys[i];
                    }
                }
            }
            return index;
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        /// <summary>把文本里所有「已知等级源色」替换成当前颜色；没有变化时返回原实例。</summary>
        public static string Apply(string text, IReadOnlyDictionary<string, string> currentHexByTier)
        {
            if (string.IsNullOrEmpty(text) || currentHexByTier == null || text.IndexOf("<color=#", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return text;
            }

            StringBuilder sb = null;
            int copied = 0;
            int search = 0;
            const string Tag = "<color=#";

            while (true)
            {
                int hit = text.IndexOf(Tag, search, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                {
                    break;
                }

                int start = hit + Tag.Length;
                int end = start;
                while (end < text.Length && end - start < 8 && IsHex(text[end]))
                {
                    end++;
                }

                int digits = end - start;
                search = end;

                if (digits < 6)
                {
                    continue;
                }

                string source = text.Substring(start, 6);
                string tier;
                if (!SourceToTier.TryGetValue(source, out tier))
                {
                    continue;
                }

                string target;
                if (!currentHexByTier.TryGetValue(tier, out target) || string.IsNullOrEmpty(target))
                {
                    continue;
                }
                if (target[0] == '#')
                {
                    target = target.Substring(1);
                }
                if (target.Length < 6)
                {
                    continue;
                }

                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // 已经是这个颜色，别动原字符串
                }

                if (sb == null)
                {
                    sb = new StringBuilder(text.Length + 16);
                }
                sb.Append(text, copied, start - copied);
                sb.Append(target, 0, 6);
                copied = start + 6;
            }

            if (sb == null)
            {
                return text;
            }

            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }
    }
}
