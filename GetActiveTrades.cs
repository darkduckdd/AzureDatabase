using MarketPlace.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MarketPlaceNew
{
    public static class GetActiveTrades
    {
        public static string PlayFabID;
        public static string TitleID;

        [FunctionName("GetActiveTrades")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {

            int itemInPage = TradeInfo.ItemInPage;
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (requestBody == null)
            {
                return new OkObjectResult("body is null");
            }
            dynamic data = JsonConvert.DeserializeObject(requestBody);


            string indexString = data?.Index;
            int index = 1;
            if (indexString != null)
            {
                index = int.Parse(indexString);
            }
            string playfabID = data?.PlayfabId;
            string titleID = data?.TitleId;

            PlayFabID = playfabID;
            TitleID = titleID;

            int itemCount = await DatabaseManager.GetActiveItemCount(DatabaseInfo.MarketplaceDBName, titleID, playfabID);
            if (itemCount == 0)
            {
                return new BadRequestObjectResult("item count is zero");
            }
            JObject tradeData = new JObject();
            bool canShowMore = itemCount >= index * itemInPage;
            tradeData.Add("activeTradeCount", itemCount);
            tradeData.Add("canShowMore", canShowMore);
            int offset = (index - 1) * itemInPage;
            List<Item> items = await DatabaseManager.GetActiveItems(DatabaseInfo.MarketplaceDBName, titleID, playfabID, offset);
            if (items.Count == 0)
            {
                return new BadRequestObjectResult("item count is zero");
            }
            JArray tradeArray = new JArray();
            foreach (var item in items)
            {
                JObject itemJson = new JObject
                {
                    { "tradeId", item.id }
                };
                JObject currency = new JObject
                {
                    { "id", item.CurrencyID }
                };
                itemJson.Add("currency", currency);
                JObject offeringItems = new JObject
                {
                    { "skinCollection", item.SkinCollection },
                    { "skinId", item.SkinID },
                    { "class", item.SkinClass },
                    { "price", item.Price },
                    { "playerId", item.PlayfabID }
                };
                itemJson.Add("offeringItems", offeringItems);
                tradeArray.Add(itemJson);
            }
            tradeData.Add("tradeData", tradeArray);

            return new OkObjectResult($"{tradeData}");

        }


    }
}
