using MarketPlace.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.ServerModels;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MarketPlaceNew
{
    public static class GetGroupedTrades
    {
        public static PlayFabAuthenticationContext AuthContext;
        public static string PlayFabID;
        public static string TitleID;

        [FunctionName("GetGroupedTrades")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (requestBody == null)
            {
                return new OkObjectResult("body is null");
            }
            int itemInPage = TradeInfo.ItemInPage;

            dynamic data = JsonConvert.DeserializeObject(requestBody);

            string playfabID = data?.PlayfabId;
            string entityToken = data?.EntityToken;
            string entityID = data?.EntityId;
            string entityType = data?.EntityType;
            string titleID = data?.TitleId;

            string rarity = data?.Rarity;
            string filterWeapon = data?.Weapon;
            string indexString = data?.Index;
            int index = 1;
            if (indexString != null)
            {
                index = int.Parse(indexString);
            }
            string filterCollection = data?.Collection;

            string DeveloperKey = await DatabaseManager.GetDeveloperSecretKey(titleID);


            if (DeveloperKey == null)
            {
                return new BadRequestObjectResult("Key not found");
            }

            PlayFabSettings.staticSettings.DeveloperSecretKey = DeveloperKey;
            PlayFabSettings.staticSettings.TitleId = titleID;

            var authContext = new PlayFabAuthenticationContext
            {
                EntityToken = entityToken,
                EntityType = entityType,
                PlayFabId = playfabID,
                EntityId = entityID
            };

            AuthContext = authContext;
            PlayFabID = playfabID;
            TitleID = titleID;
            List<string> skinsOfRareness = new List<string>();
            if (rarity != null)
            {

                var titleData = await PlayFabServerAPI.GetTitleInternalDataAsync(new GetTitleDataRequest
                {
                    AuthenticationContext = authContext,
                    Keys = new List<string> { "SkinRarity" }
                });
                var skinRarity = titleData.Result.Data["SkinRarity"];
                JArray jsonArray = JArray.Parse(skinRarity);
                foreach (var jsonObject in jsonArray)
                {
                    string itemRarity = jsonObject["rarity"].ToString();
                    if (itemRarity == rarity)
                    {
                        skinsOfRareness.Add(jsonObject["name"].ToString());
                    }
                }
            }
            var itemCount = await DatabaseManager.GetGroupedItemsCount(DatabaseInfo.MarketplaceDBName, titleID, filterWeapon, skinsOfRareness, filterCollection);

            if (itemCount == 0)
            {
                return new BadRequestObjectResult("item count is zero");

            }
            JObject tradeData = new JObject();
            bool canShowMore = itemCount >= index * itemInPage;
            tradeData.Add("canShowMore", canShowMore);
            int offset = (index - 1) * itemInPage;

            List<JObject> items = await DatabaseManager.GetGroupedItems(DatabaseInfo.MarketplaceDBName, titleID, offset, filterWeapon, skinsOfRareness, filterCollection);

            if (items.Count == 0)
            {
                return new BadRequestObjectResult("item count is zero");
            }

            JArray tradeArray = new JArray();
            foreach (var item in items)
            {
                JObject itemJson = new JObject();
                JObject group = new JObject
                {
                    { "class", item["SkinClass"] },
                    { "skinCollection", item["SkinCollection"] },
                    { "skinId", item["SkinID"] }
                };
                itemJson.Add("group", group);
                JObject offeringItems = new JObject();
                itemJson.Add("offersAmount", item["OffersAmount"]);
                tradeArray.Add(itemJson);
            }
            tradeData.Add("tradeData", tradeArray);

            return new OkObjectResult($"{tradeData}");
        }
    }
}
