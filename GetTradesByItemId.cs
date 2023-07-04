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
    public static class GetTradesByItemId
    {
        public static string PlayFabID;
        public static string TitleID;

        [FunctionName("GetItemByItemId")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (requestBody == null)
            {
                return new OkObjectResult("body is null");
            }
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            int itemInPage = TradeInfo.ItemInPage;

            string playfabID = data?.PlayfabId;
            string titleID = data?.TitleId;

            string skinId = data?.ItemId;
            string sortOrder = data?.SortOrder;
            var filterSort = 1;
            if (sortOrder != null)
            {
                filterSort = int.Parse(sortOrder);
            }


            string filterCurrency = data?.CurrencyType;
            string filterPrice = data?.PriceRange;
            int pageIndex = (int)data?.Index;
            int minPrice = 0;
            int maxPrice = 0;
            if (filterPrice != null)
            {
                var priceRange = filterPrice.Split("-");
                minPrice = int.Parse(priceRange[0]);
                maxPrice = int.Parse(priceRange[1]);
            }


            PlayFabID = playfabID;
            TitleID = titleID;
            int itemCount = await DatabaseManager.GetItemCountByFilters(DatabaseInfo.MarketplaceDBName, titleID, skinId, filterCurrency, maxPrice, minPrice);
            if (itemCount == 0)
            {
                return new BadRequestObjectResult("item count is zero");
            }
            JObject tradeData = new JObject();
            bool canShowMore = itemCount >= pageIndex * itemInPage;
            tradeData.Add("offersAmount", itemCount);
            tradeData.Add("canShowMore", canShowMore);
            int offset = (pageIndex - 1) * itemInPage;

            List<Item> items = await DatabaseManager.GetItemsByFilters(DatabaseInfo.MarketplaceDBName, titleID, skinId, offset, filterCurrency, maxPrice, minPrice, filterSort);

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
