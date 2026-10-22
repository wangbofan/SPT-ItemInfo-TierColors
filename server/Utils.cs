using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ItemInfo.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils.Json;

namespace ItemInfo;

public static class Utils
{
	private static LocaleService _localeService = null!;
	private static ItemBaseClassService _itemBaseClassService = null!;
	private static ISptLogger<ItemInfo> _logger = null!;
	private static double _euroRatio;
	private static double _dollarRatio;
	private static string _userLocale = null!;
	private static ModTranslation _translation = null!;
	private static Dictionary<MongoId, Quest> _quests = null!;
	private static TemplateTable _templates = null!;
	private static Dictionary<MongoId, TemplateItem> _items = null!;
	private static List<HandbookItem> _handbookItems = null!;
	private static Dictionary<MongoId, Trader> _traders = null!;
	public static Dictionary<string, Dictionary<string, string>> _locales = new();
	private static HashSet<string> _serverSupportedLocale = null!;
	private static Dictionary<string, LazyLoad<GlobalLocaleDictionary>> _lazyloadList = new();
	private static HideoutProductionData _hideoutProductionData = null!;
	private static List<HideoutArea> _hideoutAreas = null!;
	private static HideoutSettingsBase _hideoutSettingsBase = null!;
	private static List<Trader> _tradersList = null!;
	private static ModConfig _config = null!;

	public static void Initialize(TemplateTable templates,
								TradersTable traders,
								HideoutTable hideout,
								LocaleTable localeTable,
								LocaleService localeService, 
								ItemBaseClassService itemBaseClassService, 
								ISptLogger<ItemInfo> logger,
								double euroRatio,
								double dollarRatio,
								string userLocale,
								ModTranslation translation,
								Dictionary<MongoId, Quest> quest,
								ModConfig config)
	{
		_localeService = localeService;
		_itemBaseClassService = itemBaseClassService;
		_logger = logger;
		_euroRatio = euroRatio;
		_dollarRatio = dollarRatio;
		_userLocale = userLocale;
		_translation = translation;
		_quests = quest;
		_templates = templates;
		_items = templates.Items;
		_handbookItems = templates.Handbook.Items;
		_traders = traders;
		_serverSupportedLocale = _localeService.GetServerSupportedLocales();
		_hideoutProductionData = hideout.Production;
		_hideoutAreas = hideout.Areas;
		_hideoutSettingsBase = hideout.Settings;
		_config = config;

		foreach (string lang in _translation.Language.Keys)
		{
			_locales[lang] = _localeService.GetLocaleDb(lang);
			_lazyloadList[lang] = localeTable.Global[lang];
		}
		
		_tradersList = [_traders[Traders.THERAPIST],
						_traders[Traders.RAGMAN],
						_traders[Traders.JAEGER],
						_traders[Traders.MECHANIC],
						_traders[Traders.PRAPOR],
						_traders[Traders.SKIER],
						_traders[Traders.PEACEKEEPER]
		];
	}
    public static string GetItemName(string itemId, string locale = "en")
    {
	    try 
	    {
		    if (_locales[locale].GetValueOrDefault(itemId + " Name", itemId) != "")
			    return _locales[locale].GetValueOrDefault(itemId + " Name", itemId);

		    if (_locales["en"].GetValueOrDefault(itemId + " Name", itemId) != "")
			    return _locales["en"].GetValueOrDefault(itemId + " Name", itemId);
		    
		    return _items[itemId].Properties?.Name ?? // If THIS fails, the modmaker REALLY fucked up
		           "GetItemName() null Name";
	    } 
	    catch (Exception)
	    {
		    return "GetItemName() caught Exception";
	    }
    }

    public static string GetItemShortName(string itemId, string locale = "en")
    {
	    try
	    {
		    if (_locales[locale].GetValueOrDefault(itemId + " ShortName", itemId) != "")
			    return _locales[locale].GetValueOrDefault(itemId + " ShortName", itemId);

		    if (_locales["en"].GetValueOrDefault(itemId + " ShortName", itemId) != "")
			    return _locales["en"].GetValueOrDefault(itemId + " ShortName", itemId);
		    
		    return _items[itemId].Properties?.ShortName ?? 
		           "GetItemShortName() null ShortName";
	    }
	    catch (Exception)
	    {
		    return "GetItemShortName() caught Exception";
	    }
    }
    
    public static string GetItemDescription(string itemId, string locale = "en")
    {
	    try
	    {
		    if (_locales[locale].GetValueOrDefault(itemId + " Description", itemId) != "")
			    return _locales[locale].GetValueOrDefault(itemId + " Description", itemId);

		    if (_locales["en"].GetValueOrDefault(itemId + " Description", itemId) != "")
			    return _locales["en"].GetValueOrDefault(itemId + " Description", itemId);
		    
		    return _items[itemId].Properties?.Description ?? 
		           "GetItemDescription() null Description";
	    }
	    catch (Exception)
	    {
		    return "GetItemDescription() caught Exception";
	    }
    }

    public static string GetCraftingAreaName(int areaType, string locale = "en")
    {
	    string stringName = "hideout_area_" + areaType + "_name";
	    return _locales[locale][stringName];
    }

    /*public static int GetCraftingRarity(int areaType, int level)
    {
	    foreach (string stage in _hideoutAreas[areaType].Stages!.Keys)
	    {
		    if (int.Parse(stage) > 1)
			    return level + 1;
		    return 4;
	    }

	    return 0;
    }*/

    public static string FormatPrice(double price, bool formatPrice = true)
    {
	    return formatPrice ? 
		    price.ToString("N0", CultureInfo.GetCultureInfo("en-US")) : 
		    price.ToString(CultureInfo.GetCultureInfo("en-US"));
    }

    public static HandbookItem? GetItemInHandbook(string itemId)
    {
	    foreach (var handbookItem in _handbookItems)
	    {
		    if (handbookItem.Id == itemId) 
			    return handbookItem;
	    }

	    return null;
    }

    public static (double? price, string name, string parentId) GetItemBestTrader(string itemId, string? lang = null)
    {
	    HandbookItem? handbookItem = GetItemInHandbook(itemId);
	    (double? Multi, string name) bestTrader = ResolveBestTrader(itemId, lang ?? _userLocale);

	    double? result = handbookItem?.Price * bestTrader.Multi;

	    return new ValueTuple<double?, string, string>(result, bestTrader.name, handbookItem?.ParentId ?? "GetItemBestTrader() handbookItem");
    }

    /// <summary>
    /// 该物品在商人处能被买到的**最低**忠诚等级（LL）。兑换与直购一视同仁，只看等级。
    /// 没有任何商人出售（或只被黑名单商人出售）时返回 0，表示"商人不卖"。
    /// </summary>
    public static int GetLowestLoyaltyLevel(string itemId)
    {
	    int lowest = 0;

	    try
	    {
		    MongoId itemMongoId = itemId;

		    foreach (KeyValuePair<MongoId, Trader> kvp in _traders)
		    {
			    Trader trader = kvp.Value;
			    TraderAssort? assort = trader.Assort;

			    if (assort?.LoyalLevelItems is null ||
			        assort.Items is null ||
			        assort.Items.Count == 0 ||
			        _config.BlacklistedTradersFromRarityCalc.Contains(kvp.Key))
				    continue;

			    foreach (Item item in assort.Items)
			    {
				    if (item.Template != itemMongoId)
					    continue;

				    if (!assort.LoyalLevelItems.TryGetValue(item.Id, out int level) ||
				        level <= 0)
					    continue;

				    if (lowest == 0 || level < lowest)
					    lowest = level;
			    }
		    }
	    }
	    catch (Exception ex)
	    {
		    _logger.Warning("[ItemInfo] GetLowestLoyaltyLevel() failed for item \"" +
		                    GetItemName(itemId) +
		                    "\" because of another mod. Continuing safely. Exception: " + ex);
	    }

	    return lowest;
    }

    /// <summary>
    /// 商人货的忠诚系数（v2.3.3 起改小步进）：越晚才买得到，系数越大一点点。
    /// 取值来自 config/tiers.json 的 LOYALTY_1..4，缺省回落到 1.00 / 1.05 / 1.10 / 1.15。
    /// 跳蚤禁售 / 无货源的大系数见 GetAvailabilityMultiplier()。
    /// </summary>
    public static double GetLoyaltyMultiplier(int loyaltyLevel)
    {
	    int level = Math.Clamp(loyaltyLevel, 1, 4);
	    double parsed = ParseTierDouble("LOYALTY_" + level, double.NaN);

	    if (!double.IsNaN(parsed))
		    return parsed;

	    return level switch
	    {
		    1 => 1.00,
		    2 => 1.05,
		    3 => 1.10,
		    _ => 1.15
	    };
    }

    /// <summary>
    /// 可得性系数（v2.3.3）：越难到手，等效价值越贵、档位越高。
    /// 跳蚤禁售且商人无货源 → 2.60；跳蚤禁售但商人有 → 1.90；
    /// 商人不卖但跳蚤能交易 → 1.40；正常商人货 → 按忠诚等级小步递增。
    /// </summary>
    public static double GetAvailabilityMultiplier(bool canSellOnFlea, int lowestLoyaltyLevel)
    {
	    bool banned = !canSellOnFlea;

	    if (banned &&
	        lowestLoyaltyLevel <= 0)
		    return ParseTierDouble("AVAIL_BANNED_NO_TRADER", 2.60);

	    if (banned)
		    return ParseTierDouble("AVAIL_BANNED", 1.90);

	    if (lowestLoyaltyLevel <= 0)
		    return ParseTierDouble("AVAIL_NO_TRADER", 1.40);

	    return GetLoyaltyMultiplier(lowestLoyaltyLevel);
    }

    /// <summary>
    /// 可得性地板（v2.3.3）：买不到的东西至少给到某个档位，避免被低价手册价拖到低档。
    /// 跳蚤禁售且商人无货源 → 至少 3 档；跳蚤禁售但商人有 → 至少 2 档。
    /// </summary>
    public static int GetAvailabilityFloorTier(bool canSellOnFlea, int lowestLoyaltyLevel)
    {
	    bool banned = !canSellOnFlea;

	    if (banned &&
	        lowestLoyaltyLevel <= 0)
		    return (int)ParseTierDouble("FLOOR_BANNED_NO_TRADER", 3d);

	    if (banned)
		    return (int)ParseTierDouble("FLOOR_BANNED", 2d);

	    return 1;
    }

    /// <summary>读 config/tiers.json 的数值项，解析失败或非正数时用 fallback。</summary>
    private static double ParseTierDouble(string key, double fallback)
    {
	    if (Tiers is not null &&
	        Tiers.TryGetValue(key, out string? raw) &&
	        double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double value) &&
	        value > 0)
		    return value;

	    return fallback;
    }

    /// <summary>由 ItemInfo 在读取 config/tiers.json 之后注入，供系数解析使用。</summary>
    public static Dictionary<string, string>? Tiers { get; set; }

    /// <summary>
    /// 单价口径：跳蚤均价优先，没有再用图鉴/手册价（GetFleaPrice 已实现该顺序）。
    /// 两者都没有时返回 0。
    /// </summary>
    public static double GetUnitPrice(string itemId)
    {
	    double? price = GetFleaPrice(itemId);
	    return price is not null && price > 0 ? (double)price : 0;
    }

    /// <summary>
    /// 档位基准价：**只用图鉴/手册价**（BSG 静态基准价）。
    /// 不用 prices.json，因为那是跳蚤动态询价，运行时会按报价随机浮动/被抬价，档位就不稳定了。
    /// </summary>
    public static double GetBasePrice(string itemId)
    {
	    double? price = GetItemInHandbook(itemId)?.Price;

	    return price is not null && price > 0 ? (double)price : 0;
    }

    public static double? GetFleaPrice(string itemId)
    {
	    try
	    {
		    if (_templates.Prices.ContainsKey(itemId) &&
		        _templates.Prices[itemId] >= 0)
			    return _templates.Prices[itemId];

		    return GetItemInHandbook(itemId)?.Price >= 0 ? GetItemInHandbook(itemId)?.Price : 0;
	    }
	    catch (Exception e)
	    {
		    _logger.Warning("[ItemInfo] GetFleaPrice() failed for item \"" +
							GetItemName(itemId) + " with id: " +
							itemId +
							"\" because of another mod. Continuing safely. Exception: " + e);
		    return 0;
	    }
	    
    }

    /*public static double? GetBestPrice(string itemId)
    {
	    return _templates.Prices[itemId] >= 0 ? 
		    _templates.Prices[itemId] : GetItemBestTrader(itemId).price;
    }*/
    
    public static void AddLocaleTransformer(Dictionary<string, LazyLoad<GlobalLocaleDictionary>> lazyloadList,
											string lang,
											string type,
											string place,
											string itemId,
											string addToName,
											string originalName)
    {
	    lazyloadList[lang].AddTransformer(localeData =>
	    {
		    if (localeData is null)
			    return localeData;

		    switch (place)
		    {
			    case "prepend":
				    localeData[itemId + " " + type] = addToName + originalName;
				    break;
			    case "append":
				    localeData[itemId + " " + type] = originalName + addToName;
				    break;
		    }
		    
		    return localeData;
	    });
    }
    
    public static void AddToName(string itemId, string addToName, string place, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    AddToName(itemId, addToName, place, locale);
		    }
	    }
	    else
	    {
		    string originalName = GetItemName(itemId, lang);
		    
		    AddLocaleTransformer(_lazyloadList,
								lang,
								"Name",
								place,
								itemId,
								addToName,
								originalName);

		    switch (place)
		    {
			    case "append":
				    _locales[lang][itemId + " Name"] = originalName + addToName;
				    break;
			    case "prepend":
				    _locales[lang][itemId + " Name"] = addToName + originalName;
				    break;
		    }
		    
	    }
    }
    
    public static void RefreshName(string itemId, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    if (_locales.ContainsKey(locale))
				    RefreshName(itemId, locale);
		    }
	    }
	    else
	    {
		    AddLocaleTransformer(_lazyloadList,
								lang,
								"Name",
								"",
								itemId,
								"",
								"");
	    }
    }
    
    public static void AddToShortName(string itemId, string addToShortName, string place, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    AddToShortName(itemId, addToShortName, place, locale);
		    }
	    }
	    else
	    {
		    string originalShortName = GetItemShortName(itemId, lang);

		    AddLocaleTransformer(_lazyloadList,
								lang,
								"ShortName",
								place,
								itemId,
								addToShortName,
								originalShortName);
		    
		    switch (place)
		    {
			    case "append":
				    _locales[lang][itemId + " ShortName"] = originalShortName + addToShortName;
				    break;
			    case "prepend":
				    _locales[lang][itemId + " ShortName"] = addToShortName + originalShortName;
				    break;
		    }
	    }
    }
    
    public static void RefreshShortName(string itemId, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    RefreshShortName(itemId, locale);
		    }
	    }
	    else
	    {
		    AddLocaleTransformer(_lazyloadList,
								lang,
								"ShortName",
								"",
								itemId,
								"",
								"");
	    }
    }
    
    public static void AddToDescription(string itemId, string addToDescription, string place, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    AddToDescription(itemId, addToDescription, place, locale);
		    }
	    }
	    else
	    {
		    string originalDescription = GetItemDescription(itemId, lang);

		    AddLocaleTransformer(_lazyloadList,
								lang,
								"Description",
								place,
								itemId,
								addToDescription,
								originalDescription);
	    }
    }
    
    public static void ReplaceDescription(string itemId, string replaceDescription, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    AddToName(itemId, replaceDescription, locale);
		    }
	    }
	    else
	    {
		    _locales[lang][itemId + " Description"] = replaceDescription;
	    }
    }

    public static void AddColorToName(string itemId, string tiersHexCode, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    AddColorToName(itemId, tiersHexCode, locale);
		    }
	    }
	    else
	    {
		    AddLocaleTransformer(_lazyloadList,
								lang,
								"Name",
								"prepend",
								itemId,
								"<b><color=" + tiersHexCode + ">",
								_locales[lang][itemId + " Name"] + "</color></b>");

		    _locales[lang][itemId + " Name"] =
			    "<b><color=" + tiersHexCode + ">" + _locales[lang][itemId + " Name"] + "</color></b>";
	    }
    }
    
    public static void AddColorToShortName(string itemId, string tiersHexCode, string lang = "")
    {
	    if (lang == "")
	    {
		    foreach (string locale in _lazyloadList.Keys)
		    {
			    AddColorToShortName(itemId, tiersHexCode, locale);
		    }
	    }
	    else
	    {
		    AddLocaleTransformer(_lazyloadList,
								lang,
								"ShortName",
								"prepend",
								itemId,
								"<color=" + tiersHexCode + ">",
								_locales[lang][itemId + " ShortName"] + "</color>");
		    
		    _locales[lang][itemId + " ShortName"] =
			    "<color=" + tiersHexCode + ">" + _locales[lang][itemId + " ShortName"] + "</color>";
	    }
    }

    public static (double? multi, string name) ResolveBestTrader(string itemId, string lang = "en")
    {
	    double? traderMulti = 0f;
	    string traderName = _translation.Language.TryGetValue(lang, out Dictionary<string, string>? localized) ? localized.GetValueOrDefault("None", "None") : "None";
	    HashSet<MongoId> itemBaseClasses = _itemBaseClassService.GetItemBaseClasses(itemId);

	    foreach (Trader trader in _tradersList)
	    {
		    if (trader.Base.ItemsBuy is null || 
		        trader.Base.ItemsBuyProhibited is null)
			    continue;

		    bool canBuyByCategory = trader.Base.ItemsBuy.Category.Any(x => itemBaseClasses.Contains(x));
		    bool canBuyById = trader.Base.ItemsBuy.IdList.Contains(itemId);
		    bool isProhibited = trader.Base.ItemsBuyProhibited.IdList.Contains(itemId);

		    if ((!canBuyByCategory && !canBuyById) ||
		        isProhibited) 
			    continue;
		    
		    traderMulti = (100f - trader.Base.LoyaltyLevels?[0].BuyPriceCoefficient) / 100f;
		    string nicknameKey = trader.Base.Id + " Nickname";
		    if ((_locales.TryGetValue(lang, out Dictionary<string, string>? localeDb) && localeDb.TryGetValue(nicknameKey, out string? nickname)) ||
		        (_locales.TryGetValue("en", out Dictionary<string, string>? fallbackDb) && fallbackDb.TryGetValue(nicknameKey, out nickname)))
		    {
		    	traderName = nickname;
		    }
		    else
		    {
		    	traderName = trader.Base.Id;
		    }
		    
		    return (traderMulti, traderName);
	    }

	    return (traderMulti, traderName);
    }
    
    public static List<ResolvedBarter> BarterResolver(string itemId)
	{
		List<ResolvedBarter> itemBarters = [];
	
		try
		{
			foreach (KeyValuePair<MongoId, Trader> kvp in _traders)
			{
				Trader trader = kvp.Value;
				MongoId traderId = kvp.Key;

				if (trader.Assort.Items.Count == 0)
				{
					//_logger.Warning("[ItemInfo] trader.Assort.Items is empty");
					continue;
				}
				
				List<Item> allTraderBarters = trader.Assort.Items;
				List<Item> traderBarters = [];
	
				foreach (Item barter in allTraderBarters)
				{
					if (barter.Template == itemId)
						traderBarters.Add(barter);
				}
	
				if (traderBarters.Count == 0)
					continue;
	
				List<ResolvedBarter> barters = [];
	
				foreach (Item barter in traderBarters)
				{
					// create placeholder safely
					var placeholderBarter = new PlaceholderItem
					{
						Id = barter.Id,
						Template = barter.Template,
						ParentId = barter.ParentId,
						OriginalItemId = barter.Template
					};
	
					PlaceholderItem recursionResult = RecursionBarter(placeholderBarter, allTraderBarters);

					trader.Assort.BarterScheme.TryGetValue(barter.Id, out var schemeLists);
					
					if (schemeLists is null || 
					    schemeLists.Count == 0)
					{
						//_logger.Warning($"[ItemInfo] BarterScheme missing or invalid for trader {trader.Base?.Id ?? "unknown"} barter {barter.Id}");
						continue;
					}
	
					List<BarterScheme> barterResources = schemeLists[0];
					
					int loyaltyLevel = trader.Assort.LoyalLevelItems[barter.Id];
	
					//string traderId = trader.Base.Id;
	
					var resolved = new ResolvedBarter
					{
						ParentItem = !string.IsNullOrEmpty(recursionResult.OriginalItemId)
							? recursionResult.OriginalItemId == itemId ? null : recursionResult.OriginalItemId
							: null,
						BarterResources = barterResources,
						BarterLoyaltyLevel = loyaltyLevel,
						TraderId = traderId,
						BarterId = barter.Id
					};
	
					barters.Add(resolved);
				}
	
				if (barters.Count > 0)
					itemBarters.AddRange(barters);
			}
		}
		catch (Exception ex)
		{
			_logger.Warning("[ItemInfo] BarterResolver for item \"" +
							GetItemName(itemId) +
							"\" failed because of another mod. Continuing safely. Exception: " + ex);
		}
	
		return itemBarters;
	}


    public static (List<double> price, string barters, List<ResolvedBarter> resolvedBarters) BarterInfoGenerator(
	    List<ResolvedBarter> itemBarters, string locale = "en")
    {
	    StringBuilder barterString = new StringBuilder();
	    List<ResolvedBarter> resolvedBarters = [];
	    List<double> prices = [];

	    foreach (ResolvedBarter barter in itemBarters)
	    {
		    double totalBarterPrice = 0;
		    string totalBarterPriceString = "";

		    if (barter.TraderId is null)
			    continue;
		    
		    string traderName = _locales[_userLocale].GetValueOrDefault(barter.TraderId + " Nickname", barter.TraderId);
		    string partOf = "";

		    if (barter.ParentItem is not null)
			    partOf = " ∈ " + GetItemShortName(barter.ParentItem, locale);

		    barterString.Append(_translation.Language[locale]["Bought"] +
								partOf +
								" " +
								_translation.Language[locale]["at"] +
								" " +
								traderName +
								" " +
								_translation.Language[locale]["lv"] +
								barter.BarterLoyaltyLevel +
								" < ");


		    foreach (BarterScheme resource in barter.BarterResources)
		    {
			    switch (resource.Template)
			    {
				    case "5449016a4bdc2d6f028b456f":
					    double roubles = resource.Count ?? 0; // Null guard
					    barterString.Append(FormatPrice(Math.Round(roubles)) + 
					                        "₽ + ");
					    break;
				    
				    case "569668774bdc2da2298b4568":
					    double euros = resource.Count ?? 0; // Null guard
					    barterString.Append(FormatPrice(Math.Round(euros)) + 
					                        "€ ≈ " + 
					                        FormatPrice(Math.Round(_euroRatio * euros)) + 
					                        "₽ + ");
					    break;
				    
				    case "5696686a4bdc2da3298b456a":
					    double dollars = resource.Count ?? 0; // Null guard
					    barterString.Append(FormatPrice(Math.Round(dollars)) + 
					                        "$ ≈ " + 
					                        FormatPrice(Math.Round(_dollarRatio * dollars)) + 
					                        "₽ + ");
					    break;
				    
				    default:
					    totalBarterPrice += GetFleaPrice(resource.Template) * resource.Count ?? -999999; // Null guard
					    barterString.Append(GetItemShortName(resource.Template, locale) + 
					                        " x" + 
					                        resource.Count + 
					                        " + ");
						    break;
			    }
		    }

		    // 兑换（以物易物）与直购一视同仁：只记录忠诚等级本身，不再 +1 抬档。
		    if (!_config.BlacklistedTradersFromRarityCalc.Contains(barter.TraderId)) // Exclude blacklisted traders from rarity calc
			    resolvedBarters.Add(barter);

		    if (totalBarterPrice != 0)
			    totalBarterPriceString = " | Σ ≈ " +  
			                             FormatPrice(Math.Round(totalBarterPrice)) + 
			                             "₽"; 
		    
		    barterString.Remove(barterString.Length - 3, 3).Append(totalBarterPriceString + "\n");
	    }
	    
	    return (prices, barterString.ToString(), resolvedBarters);
    }
    
    public static string BarterResourceInfoGenerator(string itemId, string locale = "en")
	{
		StringBuilder baseBarterString = new StringBuilder();
		StringBuilder extendedBarterString = new StringBuilder();
	
		foreach (KeyValuePair<MongoId, Trader> traderPair in _traders)
		{
			Trader trader = traderPair.Value;
			string traderName = _locales[locale].GetValueOrDefault(trader.Base.Id + " Nickname", trader.Base.Id);
			
			if (trader.Base.Id == "638f541a29ffd1183d187f57" || // Skip Lightkeeper
			    trader.Assort.BarterScheme.Count == 0)
				continue;

			foreach (KeyValuePair<MongoId, List<List<BarterScheme>>> barterPair in trader.Assort.BarterScheme)
			{
				List<BarterScheme> barterList = barterPair.Value[0]; // Already guarded below
				
				if (barterList is null)
					continue;
	
				foreach (BarterScheme srcs in barterList)
				{
					if (srcs.Template != itemId)
						continue;
	
					string barterForItem = "<unknown>";
					
					foreach (Item originalBarter in trader.Assort.Items)
					{
						if (originalBarter.Id != barterPair.Key) 
							continue;
						
						barterForItem = originalBarter.Template;
						break;
					}
	
					int barterLoyaltyLevel = 0;
					
					if (trader.Assort.LoyalLevelItems.TryGetValue(barterPair.Key, out int item))
						barterLoyaltyLevel = item;
	
					baseBarterString.Append(_translation.Language[_userLocale]["Traded"] + 
											" x" + 
											srcs.Count + 
											" " +
											_translation.Language[_userLocale]["at"] + 
											" " + 
											traderName + 
											" " +
											_translation.Language[_userLocale]["lv"] + 
											barterLoyaltyLevel + 
											" > " + 
											GetItemName(barterForItem, locale));
					
					double totalBarterPrice = 0;
					
					extendedBarterString.Clear().Append(" < … + ");
	
					foreach (BarterScheme barterResource in barterList)
					{
						
						double? fleaPrice = GetFleaPrice(barterResource.Template);
						
						if (fleaPrice is null)
							throw new NullReferenceException("Item \"" + GetItemName(itemId) + "\" fleaPrice is null");
	
						totalBarterPrice += fleaPrice.Value * barterResource.Count ?? 
						                    throw new NullReferenceException("Item \"" + GetItemName(itemId) + "\" barterResource.Count is null");
	
						if (barterResource.Template == itemId)
							continue;
	
						extendedBarterString.Append(GetItemShortName(barterResource.Template, locale) + 
													" x" + 
													barterResource.Count + 
													" + ");
					}
	
					if (extendedBarterString.ToString().EndsWith(" + "))
						extendedBarterString.Remove(extendedBarterString.Length - 3, 3);
	
					if (totalBarterPrice > 0)
					{
						double? barterItemPrice = GetFleaPrice(barterForItem);
						
						if (barterItemPrice is null)
							throw new NullReferenceException("Item \"" + GetItemName(itemId) + "\" barterItemPrice is null");
	
						double diff = Math.Round(barterItemPrice.Value - totalBarterPrice);
						
						extendedBarterString.Append(" | Δ ≈ " + 
						                            FormatPrice(diff) + 
						                            "₽");
					}
	
					baseBarterString.Append(extendedBarterString + "\n");
				}
			}
		}
	
		return baseBarterString.ToString();
	}

	public static string QuestInfoGenerator(string itemId, string locale = "en")
	{
		StringBuilder questString = new StringBuilder();
		StringBuilder unlockString = new StringBuilder();
		StringBuilder partString = new StringBuilder();

		foreach (KeyValuePair<MongoId, Quest> questDict in _quests)
		{
			MongoId questId = questDict.Key;
			Quest quest = questDict.Value;
			string? questName = quest.QuestName;
			List<QuestCondition>? questCondition = quest.Conditions.AvailableForFinish;
			
			if (questName is null ||
			    questCondition is null)
				continue;

			foreach (QuestCondition condition in questCondition)
			{
				List<string>? targetList = condition.Target?.List;
				
				if (targetList is null)
					continue;

				if (condition.ConditionType != "HandoverItem" ||
				    !targetList.Contains(itemId))
					continue;
				
				MongoId trader = _quests[questId].TraderId;
				string traderName = _locales[locale].GetValueOrDefault(trader + " Nickname", trader);

				bool? onlyFoundInRaid = condition.OnlyFoundInRaid;

				if (onlyFoundInRaid is null)
					continue;

				questString.Append(_translation.Language[locale]["Found"] +
				                   " " +
				                   ((bool)onlyFoundInRaid
					                   ? "(✔) "
					                   : "") +
				                   "x" +
				                   condition.Value +
				                   " > " +
				                   questName +
				                   " @ " +
				                   traderName +
				                   "\n");
			}

			List<Reward> questRewards = [];

			// Combine Started + Success lists
			questRewards.AddRange(_quests[questId].Rewards?["Started"] ?? 
			                      throw new NullReferenceException("_quests[questId].Rewards?[\"Started\"] is null"));
			questRewards.AddRange(_quests[questId].Rewards?["Success"] ?? 
			                      throw new NullReferenceException("_quests[questId].Rewards?[\"Success\"] is null"));

			// Filter manually for type == "AssortmentUnlock"
			List<Reward> filteredRewards = [];

			foreach (Reward reward in questRewards)
			{
				if (reward.Type == RewardType.AssortmentUnlock)
					filteredRewards.Add(reward);
			}

			questRewards = filteredRewards;

			if (questRewards.Count > 0)
			{
				foreach (Reward questReward in questRewards)
				{
					if (questReward.TraderId is null)
						continue;
					
					MongoId questGiverId = _quests[questId].TraderId;
					string? traderId = questReward.TraderId.ToString();
					
					if (traderId is null)
						continue;
					
					int ll = questReward.LoyaltyLevel ?? 0;
					string traderName = _locales[_userLocale].GetValueOrDefault(traderId + " Nickname", traderId);
					string questGiverName = _locales[_userLocale].GetValueOrDefault(questGiverId + " Nickname", questGiverId);

					if (questReward.Items is null)
						continue;

					foreach (Item item in questReward.Items)
					{
						if (questReward.Target is null)
							continue;

						if (item.Template.ToString().Contains(itemId))
						{
							if (item.Id == questReward.Target)
							{
								Item? targetItem = null;

								foreach (var x in questReward.Items)
								{
									if (x.Id != questReward.Target)
										continue;

									targetItem = x;
									break;
								}

								partString.Clear().Append(targetItem != null
									? GetItemName(targetItem.Template, locale)
									: string.Empty);
							}
							
							unlockString.Append("↺ \"" +
							                    questName +
							                    "\"" +
							                    (traderName == questGiverName ? "" : " " + questGiverName) +
							                    "✔ @ " +
							                    traderName +
							                    " " +
							                    _translation.Language[locale]["lv"] +
							                    ll +
							                    (partString.Length > 0 ? " ∈ " + partString : "") +
							                    "\n");
						}
					}
				}
			}
		}
		questString.Append(unlockString);
		
		return questString.ToString();
	}

	public static string ProductionGenerator(string itemId, string locale = "en")
	{
		StringBuilder craftableString = new StringBuilder();
		StringBuilder componentString = new StringBuilder();
		StringBuilder recipeAreaString = new StringBuilder();
		StringBuilder recipeDivision = new StringBuilder();
		StringBuilder questReq = new StringBuilder();
		
		List<HideoutProduction>? recipes = _hideoutProductionData.Recipes;

		if (recipes is null)
			return craftableString.ToString();
		
		foreach (HideoutProduction recipe in recipes)
		{
			if (itemId != recipe.EndProduct ||
			    recipe.AreaType == HideoutAreas.ChristmasIllumination) 
				continue;
			
			List<Requirement>? requirements = recipe.Requirements;
				
			if (recipe.Locked is null ||
			    requirements is null ||
			    ((bool)recipe.Locked &&
			     requirements.All(x => x.QuestId is not null)))
				continue;
			
			double totalRecipePrice = 0;
			
			componentString.Clear();
			recipeDivision.Clear();
			questReq.Clear();
			recipeAreaString.Clear().Append(GetCraftingAreaName((int)recipe.AreaType!, locale));
			
			foreach (Requirement requirement in requirements)
			{
				MongoId? craftComponentId = requirement.TemplateId;
				double? craftComponentPrice = GetFleaPrice(craftComponentId!);
				
				switch (requirement.Type)
				{
					case "Area":
						recipeAreaString.Clear().Append(GetCraftingAreaName((int)recipe.AreaType!, locale) +
						                                " " +
						                                _translation.Language[locale]["lv"] +
						                                requirement.RequiredLevel);
						break;
					
					case "Item":
						int? craftComponentCount = requirement.Count;

						if (craftComponentId is null)
						{
							_logger.Error("[ItemInfo] craftComponentId is null.");
							break;
						}

						if (craftComponentCount is null)
						{
							_logger.Error("[ItemInfo] craftComponentCount is null.");
							break;
						}

						componentString.Append(GetItemShortName(craftComponentId, locale) +
											   " x" +
											   craftComponentCount +
											   " + ");
						totalRecipePrice += (double)(craftComponentPrice * craftComponentCount)!;
						break;
					
					case "Resource":
						double? resourceProportion = requirement.Resource /
						                             _items[(MongoId)requirement.TemplateId!].Properties?.Resource;
						
						if (craftComponentId is null)
						{
							_logger.Error("[ItemInfo] craftComponentId is null.");
							break;
						}
						
						componentString.Append(GetItemShortName(craftComponentId, locale) +
											   " x" +
											   Math.Round((double)resourceProportion! * 100) +
											   "% + ");
						break;
					
					case "QuestComplete":
						if (requirement.QuestId is null)
							break;

						if (_locales[locale].ContainsKey(requirement.QuestId))
						{
							questReq.Clear().Append(" " +
							                        _locales[locale][requirement.QuestId + " name"] +
							                        "✔)");
						}

						break;
				}
			}

			if (recipe.Count > 1)
			{
				recipeDivision.Clear().Append(" " +
				                              _translation.Language[locale]["peritem"]);
			}

			if (componentString.Length > 3)
				componentString.Remove(componentString.Length - 3, 3);

			if (recipe.EndProduct == "59faff1d86f7746c51718c9c")
			{
				double? bitcoinTime = recipe.ProductionTime;

				if (bitcoinTime is null) 
					continue;
				
				craftableString.Append(_translation.Language[locale]["Crafted"] +
				                   " @ " +
				                   recipeAreaString +
				                   " | 1x " + _translation.Language[locale]["GPU"] + ":" +
				                   ConvertTime(GpuTime(1, (double)bitcoinTime), locale) +
				                   ", 10x " + _translation.Language[locale]["GPU"] + ": " +
				                   ConvertTime(GpuTime(10, (double)bitcoinTime), locale) +
				                   ", 25x " + _translation.Language[locale]["GPU"] + ": " +
				                   ConvertTime(GpuTime(25, (double)bitcoinTime), locale) +
				                   ", 50x " + _translation.Language[locale]["GPU"] + ": " +
				                   ConvertTime(GpuTime(50, (double)bitcoinTime), locale));
			}
			else
			{
				craftableString.Append(_translation.Language[locale]["Crafted"] +
									   " x" +
									   recipe.Count +
									   " @ " +
									   recipeAreaString +
									   questReq +
									   " < " +
									   componentString +
									   " | Σ" +
									   recipeDivision +
									   " ≈ " + FormatPrice(Math.Round((double)(totalRecipePrice / recipe.Count)!)) +
									   "₽\n");
			}
		}

		return craftableString.ToString();
	}

	public static string HideoutInfoGenerator(string itemId, string locale = "en")
	{
		StringBuilder hideoutString = new StringBuilder();

		foreach (HideoutArea hideoutArea in _hideoutAreas)
		{
			Dictionary<string, Stage>? stages = hideoutArea.Stages;

			if (stages is null)
				continue;
			
			foreach (KeyValuePair<string, Stage> kvp in stages)
			{
				string stageNumber = kvp.Key;
				Stage stage = kvp.Value;
				List<StageRequirement>? stageRequirement = stage.Requirements;
				
				if (stageRequirement is null)
					continue;

				foreach (StageRequirement requirement in stageRequirement)
				{
					if (requirement.TemplateId == itemId)
						hideoutString.Append(_translation.Language[locale]["Need"] +
											 " x" +
											 requirement.Count +
											 " > " +
											 GetCraftingAreaName((int)hideoutArea.Type!, locale) +
											 " " +
											 _translation.Language[locale]["lv"] +
											 stageNumber +
											 "\n");
				}
			}
		}
		
		return hideoutString.ToString();
	}

	public static string CraftingMaterialInfoGenerator(string itemId, string locale = "en")
	{
		StringBuilder usedForCraftingString = new StringBuilder();
		
		if (_hideoutProductionData.Recipes is null)
			return usedForCraftingString.ToString();

		foreach (HideoutProduction recipe in _hideoutProductionData.Recipes)
		{
			List<Requirement>? requirements = recipe.Requirements;
			
			if (requirements is null)
				continue;

			foreach (Requirement t in requirements)
			{
				if (t.TemplateId is null ||
				    t.TemplateId != itemId)
					continue;
				
				string usedForCraftingComponentString = " < … + ";
				string recipeAreaString = "";
				double totalRecipePrice = 0;
				string questReq = "";

				foreach (Requirement requirement in requirements)
				{
					switch (requirement.Type)
					{
						case "Area":
							recipeAreaString = GetCraftingAreaName((int)requirement.AreaType!, locale) +
							                   " " +
							                   _translation.Language[locale]["lv"] +
							                   requirement.RequiredLevel;
							break;

						case "Item":
							if (requirement.TemplateId is null)
							{
								_logger.Warning("[ItemInfo] requirement.TemplateId is null.");
								break;
							}

							if (requirement.Count is null)
							{
								_logger.Warning("[ItemInfo] requirement.Count is null.");
								break;
							}
							
							if (requirement.TemplateId != itemId)
								usedForCraftingComponentString += GetItemShortName(requirement.TemplateId, locale) +
								                                  " x" +
								                                  requirement.Count +
								                                  " + ";

							totalRecipePrice += (double)(GetFleaPrice(requirement.TemplateId) * requirement.Count)!;
							break;

						case "Resource":
							if (requirement.TemplateId is null)
							{
								_logger.Warning("[ItemInfo] requirement.TemplateId is null.");
								break;
							}
							
							double resourceProportion =
								(double)(requirement.Resource /
								         _items[(MongoId)requirement.TemplateId].Properties?.Resource)!;

							if (requirement.TemplateId != itemId)
								usedForCraftingComponentString += GetItemShortName(requirement.TemplateId, locale) +
								                                  " x" +
								                                  Math.Round(resourceProportion * 100) +
								                                  "% + ";
							totalRecipePrice +=
								Math.Round((double)(GetFleaPrice(requirement.TemplateId) * resourceProportion)!);
							break;

						case "QuestComplete":
							if (string.IsNullOrEmpty(requirement.QuestId))
								break;
							
							questReq += " (" +
							            _locales[locale][requirement.QuestId + " name"] +
							            "✔) ";
							break;
					}
				}
				
				usedForCraftingComponentString =
					usedForCraftingComponentString[..^3];
				usedForCraftingComponentString += "  | Δ ≈ " +
				                                  FormatPrice(Math.Round(
					                                  (double)(GetFleaPrice(recipe.EndProduct) * recipe.Count -
					                                           totalRecipePrice)!)) +
				                                  "₽";
				usedForCraftingString.Append((t.Type == "Tool"
					                             ? _translation.Language[locale]["Tool"]
					                             : _translation.Language[locale]["Part"] +
					                               " x" +
					                               t.Count) +
				                             " > " +
				                             GetItemName(recipe.EndProduct, locale) +
				                             " x" +
				                             recipe.Count +
				                             " @ " +
				                             recipeAreaString +
				                             questReq +
				                             usedForCraftingComponentString +
				                             "\n");
			}
		}
		
		return usedForCraftingString.ToString();
	}

    public static PlaceholderItem RecursionBarter(PlaceholderItem barter, List<Item> allTraderBarters)
    {
	    if (barter.ParentId == "hideout")
		    return barter;

	    try
	    {
		    Item? parent = null;
		    
		    foreach (var x in allTraderBarters)
		    {
			    if (x.Id != barter.ParentId!) 
				    continue;
			    
			    parent = x;
			    break;
		    }

		    if (parent is null)
			    return barter;

		    var parentBarter = new PlaceholderItem
		    {
			    Id = parent.Id,
			    Template = parent.Template,
			    ParentId = parent.ParentId,
			    OriginalItemId = parent.Template
		    };

		    return RecursionBarter(parentBarter, allTraderBarters);
	    }
	    catch
	    {
		    return barter;
	    }
    }

    public static string ConvertTime(double time, string locale = "en")
    {
	    double hours = Math.Truncate(time / 60 / 60);
	    double minutes = Math.Round((time - hours * 60 * 60) / 60);
	    
	    return hours +
	           _locales[locale]["HOURS"] +
	           minutes +
	           _locales[locale]["Min"];
    }

    public static double GpuTime(int gpus, double time)
    {
	    if (_hideoutSettingsBase.GpuBoostRate is null)
		    return 0;
	    
	    return time / (1 + (gpus - 1) * (double)_hideoutSettingsBase.GpuBoostRate);
    }

    public class ResolvedBarter
    {
	    public string? ParentItem { get; set; }
	    public List<BarterScheme> BarterResources { get; set; } = null!;
	    public int BarterLoyaltyLevel { get; set; }
	    public string? TraderId { get; set; }
	    public string? BarterId { get; set; }
    }

    public record PlaceholderItem : Item
    {
	    public string OriginalItemId = "";
    }
}