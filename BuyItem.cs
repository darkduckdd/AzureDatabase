using MarketPlace.Data;
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MarketPlaceNew
{
    public static class BuyItem
    {
        public static PlayFabAuthenticationContext AuthContext;
        public static string PlayFabID;
        public static string TitleID;

        [FunctionName("BuyItem")]
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

            if (data?.ItemId == null)
            {
                return new OkObjectResult("params is invalid");
            }
            string playfabID = data?.PlayfabId;
            string entityToken = data?.EntityToken;
            string entityID = data?.EntityId;
            string entityType = data?.EntityType;
            string titleID = data?.TitleId;

            string itemId = data?.ItemId;


            string DeveloperKey = await DatabaseManager.GetDeveloperSecretKey(titleID);


            if (DeveloperKey == null)
            {
                return new OkObjectResult("Key not found");
            }

            PlayFabSettings.staticSettings.DeveloperSecretKey = DeveloperKey; // "UBCNF366KX3K3S3C68SD7I9UXJARXSIWTQE7SRKEQ1JG3FKR6E";
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

            Item item = await GetItem(itemId);


            if (item == null)
            {
                return new OkObjectResult("Id is invalid");
            }

            if (item.IsSold)
            {
                return new OkObjectResult("Item is already sold");
            }

            var playerInventory = await PlayFabServerAPI.GetUserInventoryAsync(new GetUserInventoryRequest
            {
                AuthenticationContext = authContext,
                PlayFabId = playfabID,
            });

            int playerBalance = GetBalance(item.CurrencyID, playerInventory.Result.VirtualCurrency);

            if (playerBalance < item.Price)
            {
                return new BadRequestObjectResult("balance is enought");
            }
            var currentSkinCount = GetSkinCount(item.SkinID, playerInventory.Result.Inventory);

            JObject additionalInfo = new JObject();

            JObject playerInfo = new JObject
            {
                { "currentBalance", playerBalance },
                { "currentSkinAmount", currentSkinCount }
            };

            var updatedItem = await UpdateItem(item);

            if (updatedItem != null)
            {
                var subtractResult = await PlayFabServerAPI.SubtractUserVirtualCurrencyAsync(new SubtractUserVirtualCurrencyRequest
                {
                    Amount = (int)updatedItem.Price,
                    AuthenticationContext = authContext,
                    PlayFabId = playfabID,
                    VirtualCurrency = updatedItem.CurrencyID
                });
                var grantResult = await PlayFabServerAPI.GrantItemsToUserAsync(new GrantItemsToUserRequest
                {
                    AuthenticationContext = authContext,
                    CatalogVersion = catalogName,
                    ItemIds = new List<string>() { updatedItem.SkinID },
                    PlayFabId = playfabID,
                });
                int grantedCount = 0;
                foreach (var grantItem in grantResult.Result.ItemGrantResults)
                {
                    if (grantItem.ItemId == updatedItem.SkinID)
                    {
                        if (grantItem.RemainingUses != null)
                        {
                            grantedCount = (int)grantItem.RemainingUses;
                        }

                    }
                }

                playerInfo.Add("afterSubtractBalance", subtractResult.Result.Balance);
                playerInfo.Add("afterSkinAmount", grantedCount);
                additionalInfo.Add("buyerInfo", playerInfo);


                var titleData = await PlayFabServerAPI.GetTitleInternalDataAsync(new GetTitleDataRequest
                {
                    AuthenticationContext = authContext,
                    Keys = new List<string> { "SaleInfo" }
                });
                var saleInfo = titleData.Result.Data["SaleInfo"];
                JObject jsonSaleInfo = JObject.Parse(saleInfo);

                var ownerInventory = await PlayFabServerAPI.GetUserInventoryAsync(new GetUserInventoryRequest
                {
                    AuthenticationContext = authContext,
                    PlayFabId = updatedItem.PlayfabID
                });
                int ownerBalance = GetBalance(updatedItem.CurrencyID, ownerInventory.Result.VirtualCurrency);

                int totalCommission = GetTotalCommission(jsonSaleInfo);
                float floatPrice = updatedItem.Price * (100 - totalCommission) / 100;
                int ownerPrice = Convert.ToInt32(Math.Floor(floatPrice));
                var addOwnerVirtualCurrency = await PlayFabServerAPI.AddUserVirtualCurrencyAsync(new AddUserVirtualCurrencyRequest
                {
                    Amount = ownerPrice,
                    AuthenticationContext = authContext,
                    PlayFabId = updatedItem.PlayfabID,
                    VirtualCurrency = updatedItem.CurrencyID
                });

                JObject ownerJsonInfo = new JObject
                {
                    { "currentBalance", ownerBalance },
                    { "balanceAfrerAdding", addOwnerVirtualCurrency.Result.Balance },
                    { "additional", ownerPrice }
                };
                additionalInfo.Add("ownerInfo", ownerJsonInfo);

                var authorID = await GetAuthorId(updatedItem.SkinID, catalogName);

                if (authorID != null)
                {
                    JObject authorJsonInfo = new JObject();
                    int authorCommision = GetAuthorCommission(jsonSaleInfo);
                    if (authorCommision != 0)
                    {

                        var authorInventory = await PlayFabServerAPI.GetUserInventoryAsync(new GetUserInventoryRequest
                        {
                            AuthenticationContext = authContext,
                            PlayFabId = authorID,
                        });
                        var authorBalance = GetBalance(updatedItem.CurrencyID, authorInventory.Result.VirtualCurrency);

                        float authorFloatPrice = updatedItem.Price * authorCommision / 100;
                        int authorPrice = Convert.ToInt32(Math.Floor(authorFloatPrice));
                        var addAuthorVirtualCurrency = await PlayFabServerAPI.AddUserVirtualCurrencyAsync(new AddUserVirtualCurrencyRequest
                        {
                            Amount = authorPrice,
                            AuthenticationContext = authContext,
                            PlayFabId = authorID,
                            VirtualCurrency = updatedItem.CurrencyID
                        });
                        authorJsonInfo.Add("authorPlayerId", authorID);
                        authorJsonInfo.Add("authorCurrentBalance", authorBalance);
                        authorJsonInfo.Add("authorBalanceAfterAdding", addAuthorVirtualCurrency.Result.Balance);
                        authorJsonInfo.Add("profitFromTrade", authorPrice);
                        additionalInfo.Add("authorInfo", authorJsonInfo);
                    }
                }

                var partherID = await GetPartherId();
                if (partherID != null)
                {
                    JObject partherJsonInfo = new JObject();
                    int partherCommision = GetPartherCommission(jsonSaleInfo);
                    if (partherCommision != 0)
                    {
                        var partherInventory = await PlayFabServerAPI.GetUserInventoryAsync(new GetUserInventoryRequest
                        {
                            AuthenticationContext = authContext,
                            PlayFabId = partherID,
                        });
                        var partherBalance = GetBalance(updatedItem.CurrencyID, partherInventory.Result.VirtualCurrency);

                        float partherFloatPrice = updatedItem.Price * partherCommision / 100;
                        int partherPrice = Convert.ToInt32(Math.Floor(partherFloatPrice));
                        var addPartherVirtualCurrency = await PlayFabServerAPI.AddUserVirtualCurrencyAsync(new AddUserVirtualCurrencyRequest
                        {
                            Amount = partherPrice,
                            AuthenticationContext = authContext,
                            PlayFabId = partherID,
                            VirtualCurrency = updatedItem.CurrencyID
                        });
                        partherJsonInfo.Add("partnerPlayerId", partherID);
                        partherJsonInfo.Add("partnerBalanceBeforeAdding", partherBalance);
                        partherJsonInfo.Add("profitFromTrade", partherPrice);
                        partherJsonInfo.Add("partnerBalanceAfterAdding", addPartherVirtualCurrency.Result.Balance);
                        additionalInfo.Add("partherInfo", partherJsonInfo);
                    }

                }
                var addResult = await AdditionalInfo(itemId, additionalInfo);
                return new OkObjectResult($"result:{addResult} info: {additionalInfo.ToString()}");
            }
            return new OkObjectResult($"not update");

        }

        private static int GetBalance(string currenctID, Dictionary<string, int> virtualCurrency)
        {
            foreach (var currency in virtualCurrency)
            {
                if (currency.Key == currenctID)
                {
                    return currency.Value;
                }
            }
            return 0;
        }
        private static int GetSkinCount(string skinID, List<ItemInstance> inventoryItems)
        {
            foreach (var currency in inventoryItems)
            {
                if (currency.ItemId == skinID)
                {
                    if (currency.RemainingUses != null)
                    {
                        return (int)currency.RemainingUses;
                    }
                }
            }
            return 0;
        }

        private static async Task<string> GetAuthorId(string skinId, string catalogName)
        {
            var catalog = await PlayFabServerAPI.GetCatalogItemsAsync(new GetCatalogItemsRequest
            {
                AuthenticationContext = AuthContext,
                CatalogVersion = catalogName
            });

            foreach (var item in catalog.Result.Catalog)
            {
                if (item.ItemId == skinId)
                {
                    var customData = JsonConvert.DeserializeObject<JObject>(item.CustomData);

                    customData.TryGetValue("authorID", out JToken authorId);
                    if (authorId != null)
                    {
                        return null;
                    }
                    return authorId.ToString();
                }
            }
            return null;
        }

        private static async Task<string> GetPartherId()
        {
            var userData = await PlayFabServerAPI.GetUserInternalDataAsync(new GetUserDataRequest
            {
                AuthenticationContext = AuthContext,
                PlayFabId = PlayFabID,
                Keys = new List<string>() { "PartnerId" }
            });

            if (userData.Result.Data.Count > 0)
            {
                string partherId = userData.Result.Data["PartnerId"].Value;
                return partherId;
            }
            return null;
        }

        private static int GetTotalCommission(JObject data)
        {
            string royalty = data.GetValue("royalty")?.ToString();
            string commission = data.GetValue("commission")?.ToString();
            string defaultValue = data.GetValue("default")?.ToString();
            if (commission != null && royalty != null && defaultValue != null)
            {

                return int.Parse(commission) + int.Parse(royalty) + int.Parse(defaultValue);
            }

            return 0;
        }
        private static int GetAuthorCommission(JObject data)
        {
            string royalty = data.GetValue("royalty")?.ToString();
            if (royalty != null)
            {
                return int.Parse(royalty);
            }
            return 0;
        }
        private static int GetPartherCommission(JObject data)
        {
            string commission = data.GetValue("commission")?.ToString();
            if (commission != null)
            {
                return int.Parse(commission);
            }
            return 0;
        }
        public static async Task<Item> GetItem(string itemID)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;
            string databaseName = DatabaseInfo.MarketplaceDBName;
            string containerName = TitleID;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);

            try
            {
                ItemResponse<Item> response = await container.ReadItemAsync<Item>(itemID, new PartitionKey(itemID));
                Item item = response.Resource;
                return item;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

        }

        public static async Task<bool> AdditionalInfo(string itemId, JObject data)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;
            string databaseName = DatabaseInfo.MarketplaceDBName;
            string containerName = TitleID;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);
            DateTime now = DateTime.UtcNow;
            string formattedDate = now.ToString("dd-MM-yyyy HH:mm:ss");


            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", itemId);

            var queryIterator = container.GetItemQueryIterator<JObject>(query);

            if (queryIterator.HasMoreResults)
            {
                var result = await queryIterator.ReadNextAsync();

                var document = result.FirstOrDefault();

                if (document != null)
                {
                    document["AdditionalInfo"] = data;
                    document["UpdatedAt"] = formattedDate;
                    var resultContainer = await container.UpsertItemAsync(document);
                    if (resultContainer.StatusCode == HttpStatusCode.OK || resultContainer.StatusCode == HttpStatusCode.UpgradeRequired)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static async Task<Item> UpdateItem(Item item)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;
            string databaseName = DatabaseInfo.MarketplaceDBName;
            string containerName = TitleID;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);
            DateTime now = DateTime.UtcNow;
            string formattedDate = now.ToString("dd-MM-yyyy HH:mm:ss");
            if (item != null)
            {

                item.UpdatedAt = formattedDate;
                item.IsSold = true;
                item.CustomerID = PlayFabID;

                var result = await container.ReplaceItemAsync<Item>(item, item.id);
                if (result.StatusCode == HttpStatusCode.OK || result.StatusCode == HttpStatusCode.Accepted)
                {
                    return result.Resource;
                }
            }

            return null;
        }
    }
}

