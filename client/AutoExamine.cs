#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace OdtTierColors
{
    /// <summary>
    /// 「自动检视全部物品」：让客户端的「这件东西你检视过没有」判断永远返回「是」，
    /// 并在第一次用到时把整本百科 LearnAll 一遍（图标和描述不再显示问号）。
    /// 用途：修掉「无法应用未检视的物品」/「无法装备未检视的物品」这类装备预设、装配界面的报错。
    /// 纯客户端内存操作，不发请求、不写存档。
    ///
    /// 挂两个位置：
    ///   1) EFT.Profile.Examined(MongoID) / (Item)         —— 百科 ContainsKey 判断
    ///   2) EFT.InventoryLogic.InventoryController.Examined —— 所有格子/装配检查的唯一入口
    ///      （Slot.CheckConditions、Weapon.Apply、Magazine.ApplyItem、ItemController.ExecutePossibleAction 都走这里）
    /// </summary>
    internal static class AutoExamine
    {
        internal static ConfigEntry<bool> EnabledEntry;

        private static Profile _pending;
        private static Profile _learned;
        private static bool _learning;
        private static bool _loggedFail;
        private static readonly HashSet<string> _blocked = new HashSet<string>();
        private static FieldInfo _encyclopediaField;
        private static bool _encyclopediaLookedUp;

        internal static void Patch(Harmony harmony)
        {
            var plainProfile = new HarmonyMethod(Method("ProfilePrefix"));
            var itemProfile = new HarmonyMethod(Method("ProfileItemPrefix"));
            var plainController = new HarmonyMethod(Method("ControllerPrefix"));
            var itemController = new HarmonyMethod(Method("ControllerItemPrefix"));

            int total = 0;
            int ok = PatchType(harmony, typeof(Profile), plainProfile, itemProfile, ref total)
                   + PatchType(harmony, typeof(InventoryController), plainController, itemController, ref total);

            if (ok == 0)
            {
                TierColorPlugin.LogSource.LogError("[ODT-TierColors] 找不到检视判断方法，自动检视没有生效。");
                return;
            }

            TierColorPlugin.LogSource.LogInfo("[ODT-TierColors] 自动检视已挂接 " + ok + "/" + total + " 个检视判断方法。");
        }

        private static MethodInfo Method(string name)
        {
            return typeof(AutoExamine).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static int PatchType(Harmony harmony, Type type, HarmonyMethod plain, HarmonyMethod withItem, ref int total)
        {
            int ok = 0;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name != "Examined" || m.ReturnType != typeof(bool))
                {
                    continue;
                }
                var ps = m.GetParameters();
                if (ps.Length != 1)
                {
                    continue;
                }

                total++;
                try
                {
                    harmony.Patch(m, prefix: ps[0].ParameterType == typeof(Item) ? withItem : plain);
                    ok++;
                }
                catch (Exception ex)
                {
                    TierColorPlugin.LogSource.LogWarning("[ODT-TierColors] " + type.Name + ".Examined 挂接失败：" + ex.Message);
                }
            }
            return ok;
        }

        private static bool Off()
        {
            return EnabledEntry == null || !EnabledEntry.Value;
        }

        private static void Remember(IInventoryProfileInfo profile)
        {
            var concrete = profile as Profile;
            if (concrete != null && !ReferenceEquals(_learned, concrete))
            {
                // 真正的 LearnAll 交给 Update 里做，别在「判断」里递归。
                _pending = concrete;
            }
        }

        // ---- EFT.Profile -------------------------------------------------

        private static bool ProfilePrefix(Profile __instance, ref bool __result)
        {
            if (Off())
            {
                return true;
            }
            __result = true;
            Remember(__instance);
            return false;
        }

        private static bool ProfileItemPrefix(Profile __instance, Item __0, ref bool __result)
        {
            if (Off())
            {
                return true;
            }
            __result = true;
            Remember(__instance);
            return false;
        }

        // ---- EFT.InventoryLogic.InventoryController -----------------------

        private static bool ControllerPrefix(InventoryController __instance, ref bool __result)
        {
            if (Off())
            {
                return true;
            }
            __result = true;
            Remember(__instance == null ? null : __instance.Profile);
            return false;
        }

        private static bool ControllerItemPrefix(InventoryController __instance, Item __0, ref bool __result)
        {
            if (Off())
            {
                return true;
            }
            __result = true;
            if (__instance != null)
            {
                Remember(__instance.Profile);
                NoteBlocked(__instance, __0);
            }
            return false;
        }

        /// <summary>
        /// 只在「原版本来会报错」时记一行日志，方便对照游戏里看到的现象。
        /// 原版判断 = _examined(全检视buff) || item.ExaminedByDefault || 百科里有这个 templateId。
        /// </summary>
        private static void NoteBlocked(InventoryController controller, Item item)
        {
            if (item == null || _blocked.Count >= 12)
            {
                return;
            }

            try
            {
                var tpl = item.TemplateId.ToString();
                if (_blocked.Contains(tpl))
                {
                    return;
                }

                if (item.ExaminedByDefault || HasEncyclopediaEntry(controller.Profile, item.TemplateId))
                {
                    return;
                }

                _blocked.Add(tpl);
                string name;
                try { name = item.Name; } catch { name = "?"; }
                TierColorPlugin.LogSource.LogInfo("[ODT-TierColors] 原本会报「未检视」的物品（已放行）：" + name + " [" + tpl + "]");
            }
            catch
            {
                // 诊断失败不影响放行
            }
        }

        private static bool HasEncyclopediaEntry(IInventoryProfileInfo info, MongoID id)
        {
            var profile = info as Profile;
            if (profile == null)
            {
                return true;
            }

            try
            {
                if (!_encyclopediaLookedUp)
                {
                    _encyclopediaLookedUp = true;
                    _encyclopediaField = typeof(Profile).GetField("Encyclopedia", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (_encyclopediaField == null)
                {
                    return true;
                }

                var dict = _encyclopediaField.GetValue(profile) as IDictionary;
                return dict == null || dict.Contains(id);
            }
            catch
            {
                return true;
            }
        }

        internal static void Tick()
        {
            var target = _pending;
            if (target == null || _learning)
            {
                return;
            }
            if (Off())
            {
                _pending = null;
                return;
            }

            _pending = null;
            _learning = true;
            try
            {
                target.LearnAll();
                _learned = target;
                TierColorPlugin.LogSource.LogInfo("[ODT-TierColors] 百科里的物品已全部标记为已检视。");
            }
            catch (Exception ex)
            {
                _learned = target;
                if (!_loggedFail)
                {
                    _loggedFail = true;
                    TierColorPlugin.LogSource.LogWarning("[ODT-TierColors] LearnAll 调用失败（不影响强制放行）：" + ex.Message);
                }
            }
            finally
            {
                _learning = false;
            }
        }
    }
}
