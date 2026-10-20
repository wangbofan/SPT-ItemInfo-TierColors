using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ItemInfo.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;

namespace ItemInfo;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.odt.iteminfo";
    public string Name { get; init; } = "ItemInfo";
    public string Author { get; init; } = "ODT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("2.3.2");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

// We want to load after PostDBModLoader is complete and all other mods, so we set our type priority to that, plus 200.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public class ItemInfo(
	ISptLogger<ItemInfo> logger, // We are injecting a logger similar to example 1, but notice the class inside <> is different
	ModHelper modHelper,
	JsonUtil jsonUtil,
    TemplateTable templates,
    TradersTable traders,
    HideoutTable hideout,
    LocaleTable localeTable,
    GlobalTable globals,
    LocaleService localeService,
    ItemBaseClassService itemBaseClassService,
	ItemHelper  itemHelper)
    : IOnLoad // Implement the `IOnLoad` interface so that this mod can do something
{
	
	private Timer? _timer;
	public required ModConfig Config;
	public required ModTiers Tiers { get; set; }
	public required ModTiersHex TiersHex { get; set; }
	public required ModTranslation Translation { get; set; }
	public required ModTranslationDebug ModTranslationDebug { get; set; }
	public required ModBsgBlackList ModBsgBlackList { get; set; }
	private double EuroRatio {get; set;}
	private double DollarRatio {get; set;}
	public required Dictionary<string, Dictionary<string, List<string>>> QuestRewardsDb { get; set; }
	public required Dictionary<MongoId, Quest> Quests { get; set; }
	public required Dictionary<MongoId,TemplateItem> Items { get; set; }
	public required Dictionary<ArmorMaterial, ArmorType> Armors { get; set; }
	public required string UserLocale { get; set; }
	public required Dictionary<string, string> i18n { get; set; }
	public required Dictionary<string, string> Localization { get; set; }
	public required List<string> BsgBlacklist { get; set; }
	public string PathToMod { get; set; }
	public Dictionary<MongoId, ModItemDescription> ItemDescription { get; set; } = new();

	/// <summary>
	/// tpl -> 档位（1-5）映射，供客户端 mod 通过 /iteminfo/tiers 拉取。
	/// 客户端拿不到跳蚤均价，所以档位判定必须由服务端做。
	/// </summary>
	public static Dictionary<string, int> ExportTiers { get; } = new();

	/// <summary>
	/// 档位 -> 本地化名称（COMMON/RARE/EPIC/LEGENDARY/MYTHIC）。
	/// </summary>
	public static Dictionary<string, string> ExportTierNames { get; } = new();

	/// <summary>
	/// 档位 -> 十六进制颜色。
	/// </summary>
	public static Dictionary<string, string> ExportTierHex { get; } = new();

	/// <summary>
	/// 导出表的版本号，供客户端判断是否需要刷新缓存。
	/// </summary>
	public static string ExportVersion { get; set; } = "2.3.2";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
	    PathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
		
	    // Get configs
	    Config = modHelper.GetJsonDataFromFile<ModConfig>(PathToMod, "config/config.json");
	    ModBsgBlackList = modHelper.GetJsonDataFromFile<ModBsgBlackList>(PathToMod, "config/bsgblacklist.json");
	    
	    // Get tiers list
	    Tiers = modHelper.GetJsonDataFromFile<ModTiers>(PathToMod, "config/tiers.json");
	    TiersHex = modHelper.GetJsonDataFromFile<ModTiersHex>(PathToMod, "config/tiers_hex.json");

	    // SPT 4.1 / EFT 0.16.x: BackgroundColor is the JSType.TaxonomyColor enum on the client,
	    // so only enum names are legal there. Hex colours stay limited to rich-text name colouring.
	    RarityColors.Warn = warning => logger.Warning(warning);
	    RarityColors.Validate(Tiers, TiersHex);
	    
	    // Get translations
	    Translation = jsonUtil.DeserializeFromFile<ModTranslation>(PathToMod + "/config/translations.json") ?? 
	                  throw new NullReferenceException("Could not find translations file");
	    ModTranslationDebug = Translation.ModTranslationDebug;
	    Translation.Language = new Dictionary<string, Dictionary<string, string>>();
	    BsgBlacklist = ModBsgBlackList.BsgBlackList;
	    
	    foreach (var kvp in Translation.RawLanguages)
	    {
		    if (kvp.Key == "debug") 
			    continue;

		    var langDict = kvp.Value.Deserialize<Dictionary<string, string>>()
		                   ?? new Dictionary<string, string>();

		    Translation.Language[kvp.Key] = langDict;
	    }
	    
	    // Init vars
	    EuroRatio = (double)templates.Handbook.Items.FirstOrDefault(item => item.Id == "569668774bdc2da2298b4568")!
		    .Price!;
	    DollarRatio = (double)templates.Handbook.Items.FirstOrDefault(item => item.Id == "5696686a4bdc2da3298b456a")!
		    .Price!;
	    Quests = templates.Quests;
	    Items = templates.Items;
	    Armors = globals.Configuration.ArmorMaterials;
	    UserLocale = Config.UserLocale;
	    i18n = Translation.Language[UserLocale];
	    Localization = localeService.GetLocaleDb(UserLocale);
	    BsgBlacklist = ModBsgBlackList.BsgBlackList;

	    // 档位导出表要等 i18n 就绪后才能取名（tiers.json 的键 -> 本地化档位名）
	    Utils.Tiers = Tiers;
	    BuildTierExportTables();
	    
        // Pass all the services and vars to Utils
        Utils.Initialize(templates,
						traders,
						hideout,
						localeTable,
						localeService, 
						itemBaseClassService, 
						logger, 
						EuroRatio, 
						DollarRatio, 
						UserLocale, 
						Translation,
						Quests,
						Config);

        if (Config.Delay.Enabled)
        {
	        logger.Info("[Item Info] Mod compatibility delay enabled (" + Config.Delay.Seconds + " seconds), waiting for other mods data to load...");
	        
	        _timer = new Timer(_ =>
	        {
		        ItemInfoMain();
	        }, null, Config.Delay.Seconds * 1000, Timeout.Infinite);
        }
        else
        {
	        ItemInfoMain();
        }

        // lets write a nice log message to the server console so players know our mod has made changes
        logger.Success("[ItemInfo] Item Info loaded!");
        
        // Inform server we have finished
        return Task.CompletedTask;
    }

	/// <summary>
	/// 把 tiers / tiers_hex / translations 里的档位信息整理成客户端可直接消费的三张表。
	/// </summary>
	private void BuildTierExportTables()
	{
		ExportTierNames.Clear();
		ExportTierHex.Clear();

		foreach (string key in RarityColors.TierKeys)
		{
			string name = key;

			if (i18n != null && i18n.TryGetValue(key, out string? localized) && !string.IsNullOrEmpty(localized))
				name = localized;

			ExportTierNames[key] = name;
			ExportTierHex[key] = RarityColors.Text(TiersHex, Tiers, key);
		}
	}

	/// <summary>
	/// 把档位导出表写成 JSON 文件，客户端 mod 读这个文件即可（比走 HTTP 路由稳）。
	/// </summary>
	private void WriteTierExportFile()
	{
		try
		{
			Dictionary<string, object> payload = new()
			{
				["version"] = ExportVersion,
				["generatedAt"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
				["names"] = new Dictionary<string, string>(ExportTierNames),
				["colors"] = new Dictionary<string, string>(ExportTierHex),
				["tiers"] = new Dictionary<string, int>(ExportTiers)
			};

			string json = jsonUtil.Serialize(payload);

			if (string.IsNullOrWhiteSpace(json))
			{
				logger.Warning("[ItemInfo] Failed to serialize tier export file.");
				return;
			}

			string exportPath = System.IO.Path.Combine(PathToMod, "tiers_export.json");
			ItemInfoTierRouter.CachePath = exportPath;
			File.WriteAllText(exportPath, json);
			logger.Info("[ItemInfo] Wrote tier export file with " + ExportTiers.Count + " entries.");
		}
		catch (Exception e)
		{
			logger.Warning("[ItemInfo] Failed to write tier export file: " + e.Message);
		}
	}

    private void ItemInfoMain()
    {
	    TranslationDebug();
	    QuestRewards();
	    
	    Stopwatch stopwatch = Stopwatch.StartNew();
	    
	    logger.Info("[ItemInfo] Processing items...");
	    ItemHandling();
	    stopwatch.Stop();
	    logger.Info("[ItemInfo] Completed processing " + Items.Count +" items in " + stopwatch.ElapsedMilliseconds + " ms.");
	    logger.Info("[ItemInfo] Exported tiers for " + ExportTiers.Count + " items.");
	    WriteTierExportFile();
    }

    private void TranslationDebug()
    {
		logger.Debug("[ItemInfo] Translation debug.");

	    if (!Config.HideLanguageAlert)
	    {
		    logger.Info("[Item Info] This mod supports other languages! \n" +
		                        "Мод поддерживает другие языки! \n" +
		                        "Este mod es compatible con otros idiomas! \n" +
		                        "Ten mod obsługuje inne języki! \n" +
		                        "English, Russian, Spanish, Korean, French, Chinese, Japanese and German are fully translated.\n" +
		                        "Hide this message in config.json");
		    
		    logger.Info("[Item Info] Your selected language is \""+ UserLocale +". \n" +
		                        "You can now customise it in Item Info config.json file. \n" +
		                        "Looking for translators, PM me! \n" +
		                        "Translation debug mode is available in translations.json");
	    }

	    if (Translation.ModTranslationDebug.Enabled)
	    {
		    logger.Warning("Translation debugging mode enabled! Changing userLocale to " + Translation.ModTranslationDebug.LanguageToDebug);
		    UserLocale = Translation.ModTranslationDebug.LanguageToDebug;
	    }

	    foreach (KeyValuePair<string, string> kvp in Translation.Language["en"])
	    {
		    string key = kvp.Key;
		    string enValue = kvp.Value;
		    
		    foreach (KeyValuePair<string, Dictionary<string, string>> lang in Translation.Language)
		    {
			    string langCode = lang.Key;
			    Dictionary<string, string> dict = lang.Value;
			    
			    if(Translation.ModTranslationDebug.Enabled &&
			       key != "" &&
			       langCode != "en" &&
			       langCode == Translation.ModTranslationDebug.LanguageToDebug &&
			       dict.TryGetValue(key, out string? val) &&
			       !string.IsNullOrEmpty(val) &&
			       val == enValue)
				    logger.Warning(Translation.ModTranslationDebug.LanguageToDebug + " language " + val + " is the same as English.");

			    if (dict.ContainsKey(key)) 
				    continue;
			    
			    if (Translation.ModTranslationDebug.Enabled &&
			        Translation.ModTranslationDebug.LanguageToDebug == langCode)
				    logger.Warning(lang.Key + " is missing " + key + " translation.");
				    
			    dict[key] = enValue;
		    }
	    }
    }

    private void QuestRewards()
    {
	    QuestRewardsDb = new Dictionary<string, Dictionary<string, List<string>>>();

	    foreach (MongoId questId in Quests.Keys)
	    {
		    var assortmentUnlocks = new List<Reward>();
		    
		    Dictionary<string, List<Reward>>? dictionary = Quests[questId].Rewards;

		    if (dictionary is not null)
		    {
			    foreach (List<Reward> rewards in dictionary.Values)
			    {
				    assortmentUnlocks.AddRange(rewards.Where(reward => reward.Type == RewardType.AssortmentUnlock));
			    }
		    }

		    if (assortmentUnlocks.Count > 1)
		    {
			    QuestRewardsDb[questId] = new Dictionary<string, List<string>>();
			    
			    foreach (Reward reward in assortmentUnlocks)
			    {
				    if (reward.Target is null)
					    continue;
				    
				    // Ensure the questID entry exists
				    if (!QuestRewardsDb.ContainsKey(questId))
					    QuestRewardsDb[questId] = new Dictionary<string, List<string>>();

				    // Initialize target list
				    QuestRewardsDb[questId][reward.Target] = [];

				    if (reward.Items is null)
					    continue;

				    // Push each _tpl value
				    foreach (Item item in reward.Items)
				    {
					    QuestRewardsDb[questId][reward.Target].Add(item.Template);
				    }
			    }
		    }
	    }
    }

	/// <summary>
	/// 把等效价值换算成 1-5 档。阈值来自 tiers.json 的 TIER_1_MAX..TIER_4_MAX。
	/// </summary>
	private int ComputeTier(double equivalentValue)
	{
		double t1 = ParseThreshold("TIER_1_MAX", 10000d);
		double t2 = ParseThreshold("TIER_2_MAX", 35000d);
		double t3 = ParseThreshold("TIER_3_MAX", 100000d);
		double t4 = ParseThreshold("TIER_4_MAX", 300000d);

		if (equivalentValue <= t1)
			return 1;
		if (equivalentValue <= t2)
			return 2;
		if (equivalentValue <= t3)
			return 3;
		if (equivalentValue <= t4)
			return 4;
		return 5;
	}

	private double ParseThreshold(string key, double fallback)
	{
		if (Tiers is not null &&
		    Tiers.TryGetValue(key, out string? raw) &&
		    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
		    value > 0)
			return value;

		return fallback;
	}

	/// <summary>
	/// 档位数字 -> tiers.json / translations.json 使用的键名。
	/// </summary>
	internal static string TierKeyOf(int tier) => tier switch
	{
		<= 1 => "COMMON",
		2 => "RARE",
		3 => "EPIC",
		4 => "LEGENDARY",
		_ => "MYTHIC"
	};

    private void ItemHandling()
    {
	    int a = 0;
	    StringBuilder descriptionString = new StringBuilder(); ;
	    StringBuilder itemBestTraderName = new StringBuilder();
	    StringBuilder itemName = new StringBuilder();
	    StringBuilder logString = new StringBuilder();
	    StringBuilder tiersHexCode = new StringBuilder();
	    StringBuilder addToName = new StringBuilder();
	    StringBuilder addToShortName = new StringBuilder();
	    // 导出给客户端 mod 的 tpl -> 档位 表（客户端拿不到跳蚤价，只能服务端下发）
	    ExportTiers.Clear();
	    
	    foreach (KeyValuePair<MongoId, TemplateItem> kvp in Items)
	    {
		    // Clearing all vars
		    itemBestTraderName.Clear();
		    tiersHexCode.Clear();
		    itemName.Clear();
		    logString.Clear();
		    addToName.Clear();
		    addToShortName.Clear();
		    
		    MongoId itemId = kvp.Key;
		    ItemDescription[itemId] = new ModItemDescription();
		    TemplateItem templateItem = kvp.Value;
		    HandbookItem? itemInHandbook = Utils.GetItemInHandbook(itemId);
		    TemplateItemProperties? itemProperties = templateItem.Properties;
		    
		    if (itemProperties is null)
			    continue;

			bool isQuestItem = itemProperties.QuestItem ?? false;

			if (templateItem.Type != "Item" || // Check if the item is a real item and not a "node" type.
			    itemInHandbook is null || // Ignore "useless" items
			    isQuestItem || // Ignore quest items.
			    templateItem.Parent == "543be5dd4bdc2deb348b4569") // Ignore currencies.
				continue; 
			
		    // Boilerplate defaults
		    double fleaPrice = Utils.GetFleaPrice(itemId) ?? 0;
		    ValueTuple<double?, string, string> itemBestVendor = Utils.GetItemBestTrader(itemId, UserLocale);
		    double? price = itemBestVendor.Item1;
		    
		    if (price is null)
			    continue;
		    
		    double traderPrice = Math.Round((double)price);
		    List<Utils.ResolvedBarter> itemBarters = Utils.BarterResolver(itemId);
		    ValueTuple<List<double>, string, List<Utils.ResolvedBarter>> barterInfo = Utils.BarterInfoGenerator(itemBarters, UserLocale);
		    int itemRarity = 1;
		    
		    string itemQuestInfo = Utils.QuestInfoGenerator(itemId, UserLocale);

		    itemBestTraderName.Append(itemBestVendor.Item2);
		    
		    Utils.RefreshName(itemId, UserLocale);
		    Utils.RefreshShortName(itemId, UserLocale);
		    
		    itemName.Append(Utils.GetItemName(kvp.Key, UserLocale));
		    itemName.Append(" | " + Utils.GetItemShortName(kvp.Key, UserLocale));

#if DEBUG
		    logString.Append("Processing item " +
		                     (a + 1) +
		                     "/" +
		                     Items.Count +
		                     ": " +
		                     itemName);

		    logger.Info(logString.ToString());
		    a += 1;
#endif

		    // Rarity handling — 价值五档（灰蓝紫橙红）
		    // 价值 V = 单价 × 堆叠上限；系数按商人最低忠诚等级；商人不卖按 1.00。
		    if (templateItem.Parent == "543be5cb4bdc2deb348b4568") // 弹药箱：按箱内弹种总价
		    {
			    IEnumerable<StackSlot>? stackSlots = itemProperties.StackSlots;
			    List<StackSlot>? stackSlotsList = stackSlots?.ToList();

			    if (stackSlotsList is not null &&
			        stackSlotsList.Count > 0)
			    {
				    double? count = stackSlotsList[0].MaxCount;
				    MongoId? ammo = stackSlotsList[0].Properties?.Filters?.ToList()[0].Filter?.ToList()[0];
				    double? value = Utils.GetItemBestTrader(ammo!).price;

				    if (value is not null &&
				        count is not null)
					    traderPrice = (double)(value * count);
				    else
					    traderPrice = 0;
			    }
		    }

		    int lowestLoyaltyLevel = Utils.GetLowestLoyaltyLevel(itemId);
		    double unitPrice = Utils.GetUnitPrice(itemId);
		    double stackSize = Math.Max(1, itemProperties.StackMaxSize ?? 1);
		    double totalValue = unitPrice * stackSize;
		    double equivalentValue = totalValue * Utils.GetLoyaltyMultiplier(lowestLoyaltyLevel);

		    itemRarity = ComputeTier(equivalentValue);

		    // BulletStatsInName
		    if (Config.ModBulletStatsInName.Enabled)
		    {
			    double damageMult = 1;
			    if (itemProperties.AmmoType is "bullet" or "buckshot")
			    {
				    if (itemProperties.AmmoType == "buckshot")
					    damageMult = itemProperties.BuckshotBullets ?? 0;

				    addToName.Clear().Append(" (" +
				                             itemProperties.Damage * damageMult +
				                             "/" +
				                             itemProperties.PenetrationPower +
				                             ")");

				    Utils.AddToName(itemId, addToName.ToString(), "append");
			    } 
			    else if (itemProperties.Name != null &&
			             itemProperties.Name.Contains("ammo_box"))
			    {
				    TemplateItem ammo =
					    Items[itemProperties.StackSlots.First().Properties.Filters.First().Filter.First()];
				    TemplateItemProperties? ammoProperties = ammo.Properties;
				    
				    if (ammoProperties != null &&
				        ammoProperties.AmmoType == "buckshot")
					    damageMult = ammoProperties.BuckshotBullets ?? 0;

				    addToName.Clear().Append(" (" +
				                             ammoProperties.Damage * damageMult +
				                             "/" +
				                             ammoProperties.PenetrationPower +
				                             ")");

				    Utils.AddToName(itemId, addToName.ToString(), "append");
			    }
		    }
		    
		    if (Config.ModArmorInfo.Enabled)
		    {
			    if (itemProperties.ArmorClass > 0 &&
			        itemProperties.ArmorMaterial is not null)
			    {
				    ArmorMaterial armorMaterial = (ArmorMaterial)itemProperties.ArmorMaterial;
				    
				    ArmorType armor = Armors[armorMaterial];

				    ItemDescription[itemId].ArmorDurabilityString = (Config.ModArmorInfo.AddArmorClassInfo
					                                                    ? i18n["Armorclass"] +
					                                                      ": " +
					                                                      itemProperties.ArmorClass +
					                                                      " | "
					                                                    : "") +
				                                                    i18n["Effectivedurability"] +
				                                                    ": " +
				                                                    Math.Round((itemProperties.MaxDurability ?? 0) /
				                                                               armor.Destructibility) +
				                                                    " (" +
				                                                    i18n["Max"] +
				                                                    ": " +
				                                                    Math.Round(itemProperties.MaxDurability ?? 0) +
				                                                    " x " +
				                                                    Localization["Mat" + armorMaterial] +
				                                                    ": " +
				                                                    Math.Round(1 / armor.Destructibility, 1) +
				                                                    ") | " +
				                                                    i18n["Repairdegradation"] +
				                                                    ": " +
				                                                    Math.Round(armor.MinRepairDegradation * 100) +
				                                                    "% - " +
				                                                    Math.Round(armor.MaxRepairDegradation * 100) +
				                                                    "%\n\n";

				    addToName.Clear().Append(" (" +
										   itemProperties.ArmorClass +
										   "/" +
										   Math.Round(itemProperties.MaxDurability ?? 0 / armor.Destructibility) +
										   ")");
				    addToShortName.Clear().Append(" (" + 
												itemProperties.ArmorClass + 
												"/" + 
												Math.Round(itemProperties.MaxDurability ?? 0/ armor.Destructibility) +
												")");

				    if (Config.ModArmorInfo.AddArmorToName)
					    Utils.AddToName(itemId, addToName.ToString(), "append");

				    if (Config.ModArmorInfo.AddArmorToShortName)
					    Utils.AddToShortName(itemId, addToShortName.ToString(), "append");
			    }
		    }
		    
		    if (Config.ModAdvancedAmmoInfo.Enabled)
		    {
			    if (templateItem.Parent == "5485a8684bdc2da71d8b4567")
			    {
				    TemplateItemProperties ammoProps = itemProperties;
				    StringBuilder ammoInfo = new StringBuilder();

				    ammoInfo.Append(i18n["AmmoDamage"] + ": " + ammoProps.Damage);
				    ammoInfo.Append("\n" + i18n["AmmoPenetrationPower"] + ": " + ammoProps.PenetrationPower);
				    ammoInfo.Append("\n" + i18n["AmmoArmorDamage"] + ": " + ammoProps.ArmorDamage);

				    if (ammoProps.ProjectileCount > 1)
				    	ammoInfo.Append("\n" + i18n["AmmoProjectileCount"] + ": " + ammoProps.ProjectileCount);

				    if (ammoProps.BuckshotBullets > 0)
				    	ammoInfo.Append("\n" + i18n["AmmoBuckshotBullets"] + ": " + ammoProps.BuckshotBullets);

				    ammoInfo.Append("\n" + i18n["AmmoInitialSpeed"] + ": " + ammoProps.InitialSpeed);
				    ammoInfo.Append("\n" + i18n["AmmoSpeedRetardation"] + ": " + ammoProps.SpeedRetardation);
				    ammoInfo.Append("\n" + i18n["AmmoBallisticCoeficient"] + ": " + ammoProps.BallisticCoeficient);
				    ammoInfo.Append("\n" + i18n["AmmoTooltipClass"] + ": " + ammoProps.AmmoTooltipClass);
				    ammoInfo.Append("\n" + i18n["AmmoFragmentationChance"] + ": " +
				                    Math.Round((ammoProps.FragmentationChance ?? 0) * 100) + "%");

				    if (ammoProps.MaxFragmentsCount > 1)
				    {
				    	ammoInfo.Append("\n" + i18n["AmmoMinFragmentsCount"] + ": " + ammoProps.MinFragmentsCount);
				    	ammoInfo.Append("\n" + i18n["AmmoMaxFragmentsCount"] + ": " + ammoProps.MaxFragmentsCount);
				    }

				    ammoInfo.Append("\n" + i18n["AmmoRicochetChance"] + ": " +
				                    Math.Round((ammoProps.RicochetChance ?? 0) * 100) + "%");
				    ammoInfo.Append("\n" + i18n["AmmoMisfireChance"] + ": " +
				                    Math.Round((ammoProps.MisfireChance ?? 0) * 100) + "%");
				    ammoInfo.Append("\n" + i18n["AmmoMalfFeedChance"] + ": " +
				                    Math.Round((ammoProps.MalfFeedChance ?? 0) * 100) + "%");
				    ammoInfo.Append("\n" + i18n["AmmoMalfMisfireChance"] + ": " +
				                    Math.Round((ammoProps.MalfMisfireChance ?? 0) * 100) + "%");
				    ammoInfo.Append("\n" + i18n["AmmoDurabilityBurnModificator"] + ": " +
				                    ammoProps.DurabilityBurnModificator);
				    ammoInfo.Append("\n" + i18n["AmmoHeatFactor"] + ": " + ammoProps.HeatFactor);
				    ammoInfo.Append("\n" + i18n["AmmoHeavyBleedingDelta"] + ": " + ammoProps.HeavyBleedingDelta);
				    ammoInfo.Append("\n" + i18n["AmmoLightBleedingDelta"] + ": " + ammoProps.LightBleedingDelta);
				    ammoInfo.Append("\n" + i18n["AmmoStaminaBurnPerDamage"] + ": " + ammoProps.StaminaBurnPerDamage);

				    if (ammoProps.Tracer ?? false)
				    {
				    	ammoInfo.Append("\n" + i18n["AmmoTracer"] + ": " + i18n["Yes"]);
				    	ammoInfo.Append("\n" + i18n["AmmoTracerColor"] + ": " + ammoProps.TracerColor);
				    	ammoInfo.Append("\n" + i18n["AmmoTracerDistance"] + ": " + ammoProps.TracerDistance);
				    }
				    else
				    {
				    	ammoInfo.Append("\n" + i18n["AmmoTracer"] + ": " + i18n["No"]);
				    }

				    ammoInfo.Append("\n" + i18n["AmmoPenetrationChanceObstacle"] + ": " +
				                    ammoProps.PenetrationChanceObstacle);
				    ammoInfo.Append("\n" + i18n["AmmoPenetrationDamageMod"] + ": " + ammoProps.PenetrationDamageMod);
				    ammoInfo.Append("\n" + i18n["AmmoPenetrationPowerDiviation"] + ": " +
				                    ammoProps.PenetrationPowerDiviation);
				    ammoInfo.Append("\n" + i18n["AmmoAccr"] + ": " + ammoProps.AmmoAccr);
				    ammoInfo.Append("\n" + i18n["AmmoDist"] + ": " + ammoProps.AmmoDist);
				    ammoInfo.Append("\n" + i18n["AmmoHear"] + ": " + ammoProps.AmmoHear);
				    ammoInfo.Append("\n" + i18n["AmmoRec"] + ": " + ammoProps.AmmoRec);
				    ammoInfo.Append("\n" + i18n["AmmoShiftChance"] + ": " + ammoProps.AmmoShiftChance);

				    if (ammoProps.ExplosionStrength > 0)
				    {
				    	ammoInfo.Append("\n" + i18n["AmmoExplosionStrength"] + ": " + ammoProps.ExplosionStrength);
				    	ammoInfo.Append("\n" + i18n["AmmoMaxExplosionDistance"] + ": " + ammoProps.MaxExplosionDistance);
				    	ammoInfo.Append("\n" + i18n["AmmoExplosionType"] + ": " + ammoProps.ExplosionType);
				    	ammoInfo.Append("\n" + i18n["AmmoHasGrenadeComponent"] + ": " + ammoProps.HasGrenaderComponent);
				    }

				    ammoInfo.Append("\n" + i18n["AmmoBulletMassGram"] + ": " + ammoProps.BulletMassGram);
				    ammoInfo.Append("\n" + i18n["AmmoBulletDiameterMillimeters"] + ": " +
				                    ammoProps.BulletDiameterMilimeters);
				    ammoInfo.Append("\n" + i18n["AmmoWeight"] + ": " + ammoProps.Weight);
				    ammoInfo.Append("\n\n");

				    ItemDescription[itemId].AdvancedAmmoInfoString = ammoInfo.ToString();
			    }
		    }

		    if (Config.ModContainerInfo.Enabled)
		    {
			    if (itemProperties.Grids is not null)
			    {
				    IEnumerable<Grid> gridsIe = itemProperties.Grids;
				    List<Grid> grids = gridsIe.ToList();

				    if (grids.Count > 0)
				    {
					    int totalSlots = 0;

					    foreach (Grid grid in grids)
					    {
						    totalSlots += grid.Properties?.CellsH * grid.Properties?.CellsV ?? 0;
					    }

					    double slotEfficiency = Math.Round((double)totalSlots / ((itemProperties.Width ?? 1) * (itemProperties.Height ?? 1)) , 2);

					    ItemDescription[itemId].SlotEfficiencyString = i18n["Slotefficiency"] +
					                                                   ": x" +
					                                                   slotEfficiency +
					                                                   " (" +
					                                                   totalSlots +
					                                                   "/" +
					                                                   itemProperties.Width * itemProperties.Height +
					                                                   ")\n\n";
				    }
			    }
		    }

		    if (Config.ModPriceInfo.Enabled)
			    ItemDescription[itemId].PriceString = (Config.ModPriceInfo.AddFleaPrice
				                                          ? i18n["Fleaprice"] +
				                                            ": " +
				                                            Utils.FormatPrice(fleaPrice) +
				                                            (fleaPrice > 0 ? "₽" : "") +
				                                            " | "
				                                          : "") +
			                                          (Config.ModPriceInfo.AddItemValue
				                                          ? i18n["ItemValue"] +
				                                            ": " +
				                                            Utils.FormatPrice(itemInHandbook.Price ?? 0) +
				                                            " | "
				                                          : "") +
			                                          i18n["Valuation1"] +
			                                          itemBestTraderName +
			                                          i18n["Valuation2"] +
			                                          ": " +
			                                          Utils.FormatPrice(traderPrice) +
			                                          "₽";

		    if (Config.ModHeadsetInfo.Enabled)
		    {
			    if (itemProperties.Distortion is not null)
			    {
				    double? gain = itemProperties.CompressorGain;
				    double? thresh = itemProperties.CompressorThreshold;

				    ItemDescription[itemId].HeadsetDescription = "<color=#f59542>" +
				                                                 i18n["AmbientVolume"] +
				                                                 ": " +
				                                                 Math.Round(
					                                                 ((itemProperties.AmbientCompressorSendLevel ??
					                                                   -10) + 10 +
					                                                  (itemProperties
						                                                   .EffectsReturnsGrEnvCommonCompressorSendLeveloupVolume ??
					                                                   -7) + 7 +
					                                                  (itemProperties.EnvNatureCompressorSendLevel ??
					                                                   -5) + 5 +
					                                                  (itemProperties.EnvTechnicalCompressorSendLevel ??
					                                                   -7) + 7) * 10) / 10 +
				                                                 "db</color> | " +
				                                                 i18n["Boost"] +
				                                                 ": +" +
				                                                 gain + Math.Abs((thresh ?? -20) + 20) +
				                                                 "db" +
				                                                 (itemProperties.Distortion > 0
					                                                 ? " | " +
					                                                   i18n["Distortion"] +
					                                                   ": " +
					                                                   Math.Round(
						                                                   (itemProperties.Distortion ?? 0) * 100) +
					                                                   "%"
					                                                 : "") +
				                                                 "\n\n";
			    }
		    }

		    if (Config.ModBarterInfo.Enabled)
		    {
			    if (barterInfo.Item2.Length > 1)
				    ItemDescription[itemId].BarterString = barterInfo.Item2 + "\n";
		    }
		    
		    if (Config.ModQuestInfo.Enabled)
		    {
			    if (itemQuestInfo.Length > 1)
			    {
				    ItemDescription[itemId].UsedForQuestsString = itemQuestInfo + "\n";

				    if (Config.ModQuestInfo.FirInName &&
				        itemQuestInfo.Contains("✔"))
					    Utils.AddToName(itemId, "✔", "append");
			    }
		    }
		    
		    // Rarity recolor handling — 五档配色（灰蓝紫橙红）
		    if (Config.ModRarityRecolor.Enabled &&
		        !Config.RarityRecolorBlacklist.Contains(templateItem.Parent))
		    {
			    if ((!Config.ModRarityRecolor.BypassAmmoRecolor ||
			         templateItem.Parent != BaseClasses.AMMO) &&
			        (!Config.ModRarityRecolor.BypassKeysRecolor ||
			         (templateItem.Parent != BaseClasses.KEY_MECHANICAL &&
			          templateItem.Parent != BaseClasses.KEYCARD)))
			    {
				    foreach (KeyValuePair<string, int> customItem in Config.ModRarityRecolor.CustomRarity)
				    {
					    if (customItem.Key == itemId)
						    itemRarity = Math.Clamp(customItem.Value, 1, 5); // 手工表统一收敛到 1-5
				    }

				    string tier = TierKeyOf(itemRarity);

				    itemProperties.BackgroundColor = RarityColors.Background(Tiers, TiersHex, tier);
				    tiersHexCode.Clear().Append(RarityColors.Text(TiersHex, Tiers, tier));

				    if (Config.ModRarityRecolor.AddColorToName)
					    Utils.AddColorToName(itemId, tiersHexCode.ToString(), UserLocale);

				    Utils.AddColorToShortName(itemId, RarityColors.Text(TiersHex, Tiers, "COMMON"), UserLocale);

				    ExportTiers[itemId] = itemRarity;

				    if (Config.ModRarityRecolor.AddTierNameToPricesInfo)
					    ItemDescription[itemId].PriceString += " | " +
					                                           "<color=" +
					                                           tiersHexCode +
					                                           ">" +
					                                           i18n[tier] +
					                                           "</color>\n\n";
			    }
		    }
	    }
	    
	    logger.Info("[ItemInfo] Processing additional info...");

	    foreach (KeyValuePair<MongoId, TemplateItem> kvp in Items)
	    {
		    descriptionString.Clear();

		    MongoId itemId = kvp.Key;
		    TemplateItem templateItem = kvp.Value;
		    HandbookItem? itemInHandbook = Utils.GetItemInHandbook(itemId);
		    TemplateItemProperties? itemProperties = templateItem.Properties;
		    
		    if (itemProperties is null)
			    continue;

		    bool isQuestItem = itemProperties.QuestItem ?? false;

		    if (templateItem.Type != "Item" || // Check if the item is a real item and not a "node" type.
		        itemInHandbook is null || // Ignore "useless" items
		        isQuestItem || // Ignore quest items.
		        templateItem.Parent == "543be5dd4bdc2deb348b4569") // Ignore currencies.
			    continue;
		    
		    if (Config.ModHideoutInfo.Enabled)
		    {
			    string itemHideoutInfo = Utils.HideoutInfoGenerator(itemId, UserLocale);

			    if (itemHideoutInfo.Length > 1)
				    ItemDescription[itemId].UsedForHideoutString = itemHideoutInfo + "\n";
		    }
		    
		    if (Config.ModProductionInfo.Enabled)
		    {
			    string productionInfo = Utils.ProductionGenerator(itemId, UserLocale);

			    if (productionInfo.Length > 1)
				    ItemDescription[itemId].ProductionString = productionInfo + "\n";
		    }
		    
		    if (Config.ModCraftingMaterialInfo.Enabled)
		    {
			    string itemCraftingMaterialInfo = Utils.CraftingMaterialInfoGenerator(itemId, UserLocale);

			    if (itemCraftingMaterialInfo.Length > 1)
				    ItemDescription[itemId].UsedForCraftingString = itemCraftingMaterialInfo + "\n";
		    }
		    
		    if (Config.ModBarterResourceInfo.Enabled)
		    {
			    string barterResourceInfo = Utils.BarterResourceInfoGenerator(itemId, UserLocale);
			    
			    if (barterResourceInfo.Length > 1)
				    ItemDescription[itemId].UsedForBarterString = barterResourceInfo + "\n";
		    }
		    
		    descriptionString.Append(ItemDescription[itemId].PriceString +
		                             ItemDescription[itemId].HeadsetDescription +
		                             ItemDescription[itemId].ArmorDurabilityString +
		                             ItemDescription[itemId].SlotEfficiencyString +
		                             ItemDescription[itemId].UsedForQuestsString +
		                             ItemDescription[itemId].UsedForHideoutString +
		                             ItemDescription[itemId].BarterString +
		                             ItemDescription[itemId].ProductionString +
		                             ItemDescription[itemId].UsedForCraftingString +
		                             ItemDescription[itemId].UsedForBarterString +
		                             ItemDescription[itemId].AdvancedAmmoInfoString);
		    
		    Utils.AddToDescription(itemId, descriptionString.ToString(), "prepend");
	    }
    }
}

/// <summary>
/// `/iteminfo/tiers` 的响应体：客户端 mod 用它把 tpl 映射成 1-5 档。
/// 客户端拿不到跳蚤均价，所以档位判定必须由服务端在这里做完。
/// </summary>
public class TierExportResponse
{
    public string Version { get; set; } = "2.3.2";
    public string GeneratedAt { get; set; } = "";
    public Dictionary<string, string> Names { get; set; } = new();
    public Dictionary<string, string> Colors { get; set; } = new();
    public Dictionary<string, int> Tiers { get; set; } = new();
}

[Injectable]
public class ItemInfoTierRouter(JsonUtil jsonUtil) : StaticRouter(
    jsonUtil,
    new RouteAction[]
    {
        new RouteAction<EmptyRequestData>(
        "/iteminfo/tiers",
        async (string url, EmptyRequestData info, MongoId sessionId, string? output, CancellationToken cancellationToken) =>
        {
            TierExportResponse response = new()
            {
                GeneratedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Names = new Dictionary<string, string>(ItemInfo.ExportTierNames),
                Colors = new Dictionary<string, string>(ItemInfo.ExportTierHex),
                Tiers = new Dictionary<string, int>(ItemInfo.ExportTiers)
            };

            return jsonUtil.Serialize(response) ?? "{}";
        }
        ),
        // 兼容个别客户端的缓存习惯：带上查询串也要能命中
        new RouteAction<EmptyRequestData>(
        "/iteminfo/tiers.json",
        async (string url, EmptyRequestData info, MongoId sessionId, string? output, CancellationToken cancellationToken) =>
        {
            TierExportResponse response = new()
            {
                GeneratedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Names = new Dictionary<string, string>(ItemInfo.ExportTierNames),
                Colors = new Dictionary<string, string>(ItemInfo.ExportTierHex),
                Tiers = new Dictionary<string, int>(ItemInfo.ExportTiers)
            };

            return jsonUtil.Serialize(response) ?? "{}";
        }
        )
    })
{
    /// <summary>
    /// 客户端读不到 HTTP 时可以退回去读这个文件（写在 mod 目录下的 tiers_export.json）。
    /// </summary>
    public static string? CachePath { get; set; }
}

public class ModTiers : Dictionary<string, string>;

public class ModTiersHex : Dictionary<string, string>;