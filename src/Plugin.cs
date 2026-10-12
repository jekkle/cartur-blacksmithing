using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturBlacksmithing
{
    /// <summary>
    /// More to the game's own Crafting skill, for weapons and armour: gear you make lasts longer,
    /// you count as extra workbench and forge upgrades, at 70 you repair from the inventory, and at
    /// 80 gear takes one more upgrade. Inspired by Smoothbrain's Blacksmithing (deprecated on
    /// Thunderstore in 2026); written from a behaviour spec and the game's code, none of theirs.
    ///
    /// Read off assembly_valheim (1.0), and why each patch is shaped the way it is:
    ///  - Valheim 1.0 has a Crafting skill of its own: every station crafts with it (CraftingStation
    ///    m_craftingSkill defaults to Crafting), repairing and building raise it, and it already
    ///    shortens craft time. Smoothbrain's mod predates it and added a second skill; this one
    ///    builds on the game's, so there is nothing new to grind.
    ///  - The crafter's level is stamped in ItemData.m_customData. DoCrafting adds a new item, or an
    ///    upgraded one, through Inventory.AddItem(ItemData) / AddItem(ItemData, pos), with its
    ///    durability already set from the unstamped maximum - so it is stamped and refilled there.
    ///    An upgrade is a fresh item: the old stamp is carried over by hand.
    ///  - Every repair gate (UpdateRepair, CanRepair, HaveRepairableItems, RepairOneItem) lets you
    ///    through without a station when Player.NoCostCheat() is true. NoCostCheat is a one-line
    ///    getter the JIT inlines into its callers, so a patch on it never runs (measured: CanRepair
    ///    stayed false). Instead its field, m_noPlacementCost, is set for the length of those four
    ///    calls at RepairLevel and put back after.
    ///  - The extra upgrade raises m_maxQuality, which the recipe list, the upgrade tab and
    ///    DoCrafting read. Outside the editor every item instance carries its own copy of SharedData
    ///    (ItemDrop.Awake only re-shares the prefab's under Application.isEditor), so the prefabs are
    ///    raised for new items and the copies in the player's bag are kept in step.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturblacksmithing";
        public const string PluginName = "Cartur's Blacksmithing";
        public const string PluginVersion = "1.0.0";
        internal const string Name = PluginName;
        internal const string Version = PluginVersion;
        internal const string ServerSection = "1 - Server";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigFile Cfg;

        internal static ConfigEntry<float> DurabilityBonus;
        internal static ConfigEntry<int> Workbench1, Workbench2, Forge1, Forge2, BlackForge1, BlackForge2, Galdr1, Galdr2;
        internal static ConfigEntry<int> RepairLevel;
        internal static ConfigEntry<int> UpgradeLevel;
        internal static ConfigEntry<float> UpgradePrice;
        internal static ConfigEntry<bool> EnforceVersion;

        private void Awake()
        {
            Log = Logger;
            Cfg = Config;

            DurabilityBonus = Config.Bind(ServerSection, "DurabilityBonus", 1f,
                new ConfigDescription("Extra durability on weapons and armour made at Crafting 100. 1 = double, 0 = vanilla.", new AcceptableValueRange<float>(0f, 4f)));
            ConfigEntry<int> Level(string key, int value, string what) =>
                Config.Bind(ServerSection, key, value, new ConfigDescription($"Crafting level from which you count as {what}. 0 = never.", new AcceptableValueRange<int>(0, 100)));
            Workbench1 = Level("WorkbenchLevel1", 10, "one workbench upgrade");
            Workbench2 = Level("WorkbenchLevel2", 20, "a second workbench upgrade");
            Forge1 = Level("ForgeLevel1", 30, "one forge upgrade");
            Forge2 = Level("ForgeLevel2", 40, "a second forge upgrade");
            BlackForge1 = Level("BlackForgeLevel1", 50, "one black forge upgrade");
            BlackForge2 = Level("BlackForgeLevel2", 60, "a second black forge upgrade");
            Galdr1 = Level("GaldrTableLevel1", 50, "one galdr table upgrade");
            Galdr2 = Level("GaldrTableLevel2", 60, "a second galdr table upgrade");
            RepairLevel = Config.Bind(ServerSection, "RepairLevel", 70,
                new ConfigDescription("Crafting level from which you can repair from the inventory, with no station. 0 = never.", new AcceptableValueRange<int>(0, 100)));
            UpgradeLevel = Config.Bind(ServerSection, "UpgradeLevel", 80,
                new ConfigDescription("Crafting level from which weapons and armour take one upgrade past their normal maximum. 0 = never.", new AcceptableValueRange<int>(0, 100)));
            UpgradePrice = Config.Bind(ServerSection, "UpgradePrice", 3f,
                new ConfigDescription("Material cost of that extra upgrade, as a multiple of a normal upgrade at the same level.", new AcceptableValueRange<float>(1f, 10f)));
            EnforceVersion = Config.Bind(ServerSection, "EnforceVersion", true,
                "On a server: refuse clients running a different version of this mod.");

            try
            {
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
                Log.LogInfo($"{PluginName} {PluginVersion} loaded - durability, station levels, inventory repair and the extra upgrade on the Crafting skill.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"{PluginName} failed to patch, game continues without it: {e}");
            }
        }

        private float _checkAt;

        private void Update()
        {
            if (Time.time >= _checkAt)
            {
                _checkAt = Time.time + 2f;
                Smithing.ApplyExtraUpgrade();
            }
        }

        internal static IEnumerable<ConfigEntryBase> ServerOwnedEntries(HashSet<string> sections) =>
            Cfg.Keys.Where(k => sections.Contains(k.Section)).Select(k => Cfg[k]);

        internal static ConfigEntryBase FindEntry(string section, string key) =>
            Cfg.Keys.Where(k => k.Section == section && k.Key == key).Select(k => Cfg[k]).FirstOrDefault();

        internal static void WithoutSaving(Action change)
        {
            bool was = Cfg.SaveOnConfigSet;
            Cfg.SaveOnConfigSet = false;
            try { change(); }
            finally { Cfg.SaveOnConfigSet = was; }
        }
    }

    internal static class Smithing
    {
        internal const string LevelData = "cartur_smith";

        internal static int LocalLevel() =>
            Player.m_localPlayer != null ? Mathf.RoundToInt(Player.m_localPlayer.GetSkillLevel(Skills.SkillType.Crafting)) : 0;

        internal static bool Reached(ConfigEntry<int> level) => level.Value > 0 && LocalLevel() >= level.Value;

        /// Weapons and armour: everything worn or wielded that wears out, except ammo, tools,
        /// utility items and trinkets. A one-handed weapon that is thrown away when used is not gear.
        internal static bool IsGear(ItemDrop.ItemData.SharedData s)
        {
            switch (s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    return true;
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                    return !s.m_attack.m_consumeItem;
                default:
                    return false;
            }
        }

        internal static int SmithLevel(ItemDrop.ItemData item) =>
            item != null && item.m_customData.TryGetValue(LevelData, out string s) && int.TryParse(s, out int v) ? v : 0;

        internal static float DurabilityFactor(int level) => 1f + level / 100f * Plugin.DurabilityBonus.Value;

        // ---- the extra upgrade ----

        // Keyed by item name: every instance has its own SharedData copy (see the class comment).
        private static readonly Dictionary<string, int> OriginalMax = new Dictionary<string, int>();
        private static bool _raised;

        internal static int NormalMax(ItemDrop.ItemData.SharedData s) => OriginalMax.TryGetValue(s.m_name, out int max) ? max : s.m_maxQuality;

        /// Gear that already upgrades (normal maximum above 1) takes one more level while the local
        /// player has UpgradeLevel, and goes back when they do not. Prefabs change once per switch;
        /// the copies in the bag are re-synced every call, since items arrive in it all the time.
        internal static void ApplyExtraUpgrade()
        {
            if (ObjectDB.instance == null)
                return;
            bool want = Reached(Plugin.UpgradeLevel);
            if (OriginalMax.Count == 0 || want != _raised)
            {
                foreach (GameObject prefab in ObjectDB.instance.m_items)
                {
                    ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    ItemDrop.ItemData.SharedData s = drop != null ? drop.m_itemData.m_shared : null;
                    if (s == null)
                        continue;
                    if (!OriginalMax.ContainsKey(s.m_name))
                    {
                        if (!IsGear(s) || s.m_maxQuality <= 1)
                            continue;
                        OriginalMax[s.m_name] = s.m_maxQuality;
                    }
                    s.m_maxQuality = OriginalMax[s.m_name] + (want ? 1 : 0);
                }
                _raised = want;
            }
            Inventory inv = Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory() : null;
            if (inv == null)
                return;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (OriginalMax.TryGetValue(item.m_shared.m_name, out int max))
                    item.m_shared.m_maxQuality = max + (want ? 1 : 0);
        }

        /// The recipe whose requirements are being counted right now, for the extra upgrade's price.
        internal static ItemDrop.ItemData.SharedData Pricing;
    }

    // ---- stamping crafted gear ----------------------------------------------------------------------

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class Crafting
    {
        internal static bool Active;
        internal static int CarriedLevel;

        private static void Prefix(Recipe ___m_craftRecipe, ItemDrop.ItemData ___m_craftUpgradeItem, out ItemDrop.ItemData.SharedData __state)
        {
            __state = Smithing.Pricing;
            Active = ___m_craftRecipe != null && Smithing.IsGear(___m_craftRecipe.m_item.m_itemData.m_shared);
            CarriedLevel = Smithing.SmithLevel(___m_craftUpgradeItem);
            if (___m_craftRecipe != null)
                Smithing.Pricing = ___m_craftRecipe.m_item.m_itemData.m_shared;
        }

        private static void Finalizer(ItemDrop.ItemData.SharedData __state)
        {
            Active = false;
            CarriedLevel = 0;
            Smithing.Pricing = __state;
        }

        /// Stamps the higher of the crafter's level and the upgraded item's old stamp, and fills the
        /// item to its new maximum - AddItem set its durability before the stamp existed.
        internal static void Stamp(ItemDrop.ItemData item)
        {
            if (!Active || item == null || !Smithing.IsGear(item.m_shared))
                return;
            int level = Mathf.Max(Smithing.LocalLevel(), CarriedLevel);
            if (level <= 0)
                return;
            item.m_customData[Smithing.LevelData] = level.ToString(CultureInfo.InvariantCulture);
            item.m_durability = item.GetMaxDurability();
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
    internal static class StampAdded
    {
        private static void Prefix(ItemDrop.ItemData item) => Crafting.Stamp(item);
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
    internal static class StampAddedAt
    {
        private static void Prefix(ItemDrop.ItemData item) => Crafting.Stamp(item);
    }

    // ---- durability ------------------------------------------------------------------------------------

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetMaxDurability), typeof(int))]
    internal static class Durability
    {
        private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            int level = Smithing.SmithLevel(__instance);
            if (level > 0)
                __result *= Smithing.DurabilityFactor(level);
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
    internal static class Tooltip
    {
        private static void Postfix(ItemDrop.ItemData item, int qualityLevel, bool crafting, ref string __result)
        {
            if (item == null || !item.m_shared.m_useDurability || !Smithing.IsGear(item.m_shared))
                return;
            // A recipe shows what this smith would make; an item shows what its smith made.
            int level = crafting ? Smithing.LocalLevel() : Smithing.SmithLevel(item);
            if (level <= 0)
                return;
            float baseMax = item.m_shared.m_maxDurability + Mathf.Max(0, qualityLevel - 1) * item.m_shared.m_durabilityPerLevel;
            __result += $"\n\nForged at Crafting <color=orange>{level}</color>: <color=orange>+{Mathf.Round(baseMax * (Smithing.DurabilityFactor(level) - 1f))}</color> durability";
        }
    }

    // ---- station levels ----------------------------------------------------------------------------------

    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetLevel))]
    internal static class StationLevel
    {
        private static void Postfix(CraftingStation __instance, ref int __result)
        {
            switch (__instance.m_name)
            {
                case "$piece_workbench": __result += Bonus(Plugin.Workbench1, Plugin.Workbench2); break;
                case "$piece_forge": __result += Bonus(Plugin.Forge1, Plugin.Forge2); break;
                case "$piece_blackforge": __result += Bonus(Plugin.BlackForge1, Plugin.BlackForge2); break;
                case "$piece_magetable": __result += Bonus(Plugin.Galdr1, Plugin.Galdr2); break;
            }
        }

        private static int Bonus(ConfigEntry<int> first, ConfigEntry<int> second) =>
            (Smithing.Reached(first) ? 1 : 0) + (Smithing.Reached(second) ? 1 : 0);
    }

    // ---- repairing from the inventory ------------------------------------------------------------------

    [HarmonyPatch]
    internal static class InventoryRepair
    {
        private static readonly FieldInfo NoCost = AccessTools.Field(typeof(Player), "m_noPlacementCost");
        private static int _depth;
        private static bool _set;

        private static IEnumerable<MethodBase> TargetMethods() => new[] { "UpdateRepair", "CanRepair", "HaveRepairableItems", "RepairOneItem" }
            .Select(n => (MethodBase)AccessTools.Method(typeof(InventoryGui), n)).Where(m => m != null);

        private static void Prefix()
        {
            Player p = Player.m_localPlayer;
            if (_depth++ == 0 && p != null && NoCost != null && !(bool)NoCost.GetValue(p) && Smithing.Reached(Plugin.RepairLevel))
            {
                NoCost.SetValue(p, true);
                _set = true;
            }
        }

        private static void Finalizer()
        {
            if (--_depth == 0 && _set)
            {
                _set = false;
                if (Player.m_localPlayer != null)
                    NoCost.SetValue(Player.m_localPlayer, false);
            }
        }
    }

    // ---- pricing the extra upgrade -----------------------------------------------------------------

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Recipe), typeof(bool), typeof(int), typeof(int))]
    internal static class PricingCheck
    {
        private static void Prefix(Recipe recipe, out ItemDrop.ItemData.SharedData __state)
        {
            __state = Smithing.Pricing;
            if (recipe != null && recipe.m_item != null)
                Smithing.Pricing = recipe.m_item.m_itemData.m_shared;
        }
        private static void Finalizer(ItemDrop.ItemData.SharedData __state) => Smithing.Pricing = __state;
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupRequirementList")]
    internal static class PricingDisplay
    {
        private static void Prefix(InventoryGui __instance, out ItemDrop.ItemData.SharedData __state)
        {
            __state = Smithing.Pricing;
            Recipe recipe = Traverse.Create(__instance).Field("m_selectedRecipe").Property("Recipe").GetValue<Recipe>();
            if (recipe != null && recipe.m_item != null)
                Smithing.Pricing = recipe.m_item.m_itemData.m_shared;
        }
        private static void Finalizer(ItemDrop.ItemData.SharedData __state) => Smithing.Pricing = __state;
    }

    [HarmonyPatch(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount))]
    internal static class UpgradePrice
    {
        private static void Postfix(int qualityLevel, ref int __result)
        {
            ItemDrop.ItemData.SharedData s = Smithing.Pricing;
            if (s != null && qualityLevel > Smithing.NormalMax(s))
                __result = Mathf.FloorToInt(__result * Plugin.UpgradePrice.Value);
        }
    }
}
