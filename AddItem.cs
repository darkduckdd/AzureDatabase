using MarketPlace.Data;
using MarketPlaceNew;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.ServerModels;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Marketplace
{
    public static class AddItem
    {

        public static PlayFabAuthenticationContext AuthContext;
        public static string PlayFabID;
        public static string TitleID;

        [FunctionName("AddItem")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string catalogName = "Items";
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (requestBody == null)
            {
                return new OkObjectResult("body is null");
            }
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            if (data?.SkinId == null || data?.Price == null || data?.SkinClass == null || data?.CurrencyId)
            {
                return new OkObjectResult("params is invalid");
            }
            string playfabID = data?.PlayfabId;
            string entityToken = data?.EntityToken;
            string entityID = data?.EntityId;
            string entityType = data?.EntityType;
            string titleID = data?.TitleId;

            string skinID = data?.SkinId;
            int price = (int)data?.Price;
            string skinClass = data?.SkinClass;
            string currencyID = data?.CurrencyId;

            string DeveloperKey = await DatabaseManager.GetDeveloperSecretKey(titleID);


            if (DeveloperKey == null)
            {
                return new BadRequestObjectResult("Key not found");
            }

            PlayFabSettings.staticSettings.DeveloperSecretKey = DeveloperKey;// "UBCNF366KX3K3S3C68SD7I9UXJARXSIWTQE7SRKEQ1JG3FKR6E";
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

            string collection = null;

            int remainingUses = 0;
            string instanceId = null;
            var inventoryResult = await PlayFabServerAPI.GetUserInventoryAsync(new GetUserInventoryRequest
            {
                PlayFabId = playfabID,
                AuthenticationContext = authContext,
            });

            var inventory = inventoryResult.Result.Inventory;
            bool isInInventory = false;
            foreach (var item in inventory)
            {
                if (item.ItemId == skinID)
                {
                    if (item.RemainingUses != null)
                    {
                        remainingUses = item.RemainingUses.Value;

                    }
                    instanceId = item.ItemInstanceId;
                    isInInventory = true;
                }
            }
            if (!isInInventory)
            {
                return new BadRequestObjectResult("Skin is not in inventory");
            }

            var catalogResult = await PlayFabServerAPI.GetCatalogItemsAsync(new GetCatalogItemsRequest
            {
                AuthenticationContext = authContext,
                CatalogVersion = catalogName
            });
            var catalogItems = catalogResult.Result.Catalog.ToArray();
            foreach (var item in catalogItems)
            {
                if (item.ItemId == skinID)
                {
                    var customData = JObject.Parse(item.CustomData);
                    if (customData.TryGetValue("collection", out JToken collectionToken))
                    {
                        collection = collectionToken.ToString();
                    }

                }
            }
            bool saveResult = await SaveItem(playfabID, currencyID, price, skinID, skinClass, collection);

            if (saveResult)
            {
                if (remainingUses <= 1)
                {
                    var revokeResult = await PlayFabServerAPI.RevokeInventoryItemAsync(new RevokeInventoryItemRequest
                    {
                        AuthenticationContext = authContext,
                        ItemInstanceId = instanceId,
                        PlayFabId = playfabID
                    });

                }
                else
                {
                    var consumeResult = await PlayFabServerAPI.ConsumeItemAsync(new ConsumeItemRequest
                    {
                        AuthenticationContext = authContext,
                        PlayFabId = playfabID,
                        ItemInstanceId = instanceId,
                        ConsumeCount = 1
                    });
                }
                return new OkObjectResult($"save");

            }
            return new OkObjectResult($"not save");
        }

        public static async Task<bool> SaveItem(string playfabID, string currencyID, int price, string skinID, string skinClass, string skinCollection)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;
            string databaseName = DatabaseInfo.MarketplaceDBName;
            string containerName = TitleID;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var database = cosmosClient.GetDatabase(databaseName);
            var containerResponse = await database.CreateContainerIfNotExistsAsync(containerName, "/id");
            Container container = null;
            if (containerResponse.StatusCode == HttpStatusCode.Created)
            {
                container = cosmosClient.GetContainer(databaseName, containerName);
            }
            else if (containerResponse.StatusCode == HttpStatusCode.OK)
            {
                container = cosmosClient.GetContainer(databaseName, containerName);
            }

            if (container == null)
            {
                await Task.Delay(10000);
                container = cosmosClient.GetContainer(databaseName, containerName);
            }

            DateTime now = DateTime.UtcNow;
            string formattedDate = now.ToString("dd-MM-yyyy HH:mm:ss");

            var item = new Item
            {
                id = DateTime.UtcNow.Ticks.ToString() + Guid.NewGuid().ToString(),
                CurrencyID = currencyID,
                Price = price,
                SkinID = skinID,
                SkinClass = skinClass,
                SkinCollection = skinCollection,
                IsSold = false,
                PlayfabID = playfabID,
                CustomerID = null,
                CreatedAt = formattedDate,
                UpdatedAt = formattedDate,

            };
            var response = await container.CreateItemAsync(item);
            if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
