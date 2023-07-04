using MarketPlace.Data;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MarketPlaceNew
{
    public static class DatabaseManager
    {

        public static async Task<string> GetDeveloperSecretKey(string titleID)
        {
            string URI = DatabaseInfo.URI; //"https://walletdb.documents.azure.com:443/";
            string privateKey = DatabaseInfo.PrivateKey;
            string databaseName = DatabaseInfo.SecretKeysDB;
            string containerName = DatabaseInfo.SecretKeysContainer;


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

            try
            {
                ItemResponse<JObject> response = await container.ReadItemAsync<JObject>(titleID, new PartitionKey(titleID));
                JObject item = response.Resource;
                return item["SecretKey"].ToString();
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public static async Task<bool> DeleteItem(string databaseName, string containerName, string itemID)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

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

            try
            {
                var response = await container.DeleteItemAsync<object>(itemID, new PartitionKey(itemID));
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return true;
                }
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            return false;
        }

        public static async Task<Item> GetItem(string databaseName, string containerName, string itemID)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

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

        public static async Task<int> GetItemCountByFilters(string databaseName, string containerName, string skinID, string filterCurrency = null, int maxPrice = 0, int minPrice = 0)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);
            string queryString = "SELECT VALUE COUNT(1) FROM c WHERE c.IsSold = @IsSold AND c.SkinID = @SkinID";

            if (filterCurrency != null)
            {
                queryString += $" AND c.CurrencyID = \"{filterCurrency}\"";
            }

            if (maxPrice != 0)
            {
                queryString += $" AND c.Price <= {maxPrice}";
            }
            if (minPrice != 0)
            {
                queryString += $" AND c.Price >= {minPrice}";
            }
            var feedIterator = container.GetItemQueryIterator<int>(
            new QueryDefinition(queryString)
            .WithParameter("@SkinID", skinID)
            .WithParameter("@IsSold", false));

            // Получаем результат запроса
            var queryResultSetIterator = await feedIterator.ReadNextAsync();
            var count = queryResultSetIterator.First();
            return count;
        }
        public static async Task<List<Item>> GetItemsByFilters(string databaseName, string containerName, string skinID, int offset, string filterCurrency = null, int maxPrice = 0, int minPrice = 0, int sortFilter = 1)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);

            string queryString = "SELECT * FROM c WHERE c.IsSold = @IsSold AND c.SkinID = @SkinID";

            if (filterCurrency != null)
            {
                queryString += $" AND c.CurrencyID = \"{filterCurrency}\"";
            }

            if (maxPrice != 0)
            {
                queryString += $" AND c.Price <= {maxPrice}";
            }
            if (minPrice != 0)
            {
                queryString += $" AND c.Price >= {minPrice}";
            }
            if (sortFilter == 1)
            {
                queryString += $" ORDER BY c.Price DESC";
            }
            else
            {
                queryString += $" ORDER BY c.Price ASC";
            }
            queryString += $" OFFSET @offset LIMIT @limit";
            var query = new QueryDefinition(queryString)
            .WithParameter("@SkinID", skinID)
            .WithParameter("@IsSold", false).WithParameter("@offset", offset).WithParameter("@limit", TradeInfo.ItemInPage);

            using var resultSetIterator = container.GetItemQueryIterator<Item>(query);

            var results = new List<Item>();

            while (resultSetIterator.HasMoreResults)
            {
                var response = await resultSetIterator.ReadNextAsync();

                foreach (var item in response)
                {
                    results.Add(item);
                }
            }
            return results;
        }

        public static async Task<int> GetHistoryItemCount(string databaseName, string containerName, string playfabID)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);

            var feedIterator = container.GetItemQueryIterator<int>(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.IsSold = @IsSold AND (c.PlayfabID = @PlayfabID OR c.CustomerID = @PlayfabID)")
            .WithParameter("@PlayfabID", playfabID)
            .WithParameter("@IsSold", true));

            // Получаем результат запроса
            var queryResultSetIterator = await feedIterator.ReadNextAsync();
            var count = queryResultSetIterator.First();
            return count;
        }

        public static async Task<List<Item>> GetHistoryItems(string databaseName, string containerName, string playfabID, int offset)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);


            var query = new QueryDefinition("SELECT  * FROM c WHERE c.IsSold = @IsSold AND (c.PlayfabID = @PlayfabID OR c.CustomerID = @PlayfabID) ORDER BY c.UpdatedAt DESC OFFSET @offset LIMIT @limit")
            .WithParameter("@PlayfabID", playfabID)
            .WithParameter("@IsSold", true).WithParameter("@offset", offset).WithParameter("@limit", TradeInfo.ItemInPage);


            var resultSetIterator = container.GetItemQueryIterator<Item>(query);

            var itemList = new List<Item>();

            while (resultSetIterator.HasMoreResults)
            {
                var response = await resultSetIterator.ReadNextAsync();

                foreach (var item in response)
                {
                    itemList.Add(item);
                }
            }
            return itemList;
        }
        public static async Task<int> GetActiveItemCount(string databaseName, string containerName, string playfabID)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);
            var query = container.GetItemLinqQueryable<Item>()
            .Where(item => item.PlayfabID == playfabID && item.IsSold == false).CountAsync();

            // Получение количества записей
            int count = await query;
            return count;
        }
        public static async Task<List<Item>> GetActiveItems(string databaseName, string containerName, string playfabID, int offset)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;

            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);
            var query = new QueryDefinition("SELECT * FROM c WHERE c.PlayfabID = @PlayfabID AND c.IsSold = @IsSold ORDER BY c.CreatedAt DESC OFFSET @offset LIMIT @limit").WithParameter("@PlayfabID", playfabID).WithParameter("@IsSold", false).WithParameter("@offset", offset)
            .WithParameter("@limit", TradeInfo.ItemInPage);

            using var resultSetIterator = container.GetItemQueryIterator<Item>(query);

            var itemList = new List<Item>();

            while (resultSetIterator.HasMoreResults)
            {
                var response = await resultSetIterator.ReadNextAsync();

                foreach (var item in response)
                {
                    itemList.Add(item);
                }
            }
            return itemList;
        }

        public static async Task<int> GetGroupedItemsCount(string databaseName, string containerName, string filterWeapon = null, List<string> filterSkins = null, string collection = null)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;
            string condition = "";
            if (filterWeapon != null)
            {
                condition += $" AND ENDSWITH(c.SkinID, \"({filterWeapon})\")";
            }

            if (filterSkins != null && filterSkins.Count > 0)
            {
                string skins = "";
                for (int i = 0; i < filterSkins.Count; i++)
                {
                    if (i + 1 == filterSkins.Count)
                    {
                        skins += $"\"{filterSkins[i]}\"";
                    }
                    else
                    {
                        skins += $"\"{filterSkins[i]}\",";
                    }

                }
                condition += $" AND c.SkinCollection IN ({skins})";
            }

            if (collection != null)
            {
                condition += $" AND c.SkinCollection = \"{collection}\"";
            }
            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);

            string query = $"SELECT VALUE COUNT(1) FROM ( SELECT  c.SkinID, c.SkinClass, c.SkinCollection  FROM c WHERE c.IsSold = false {condition} GROUP BY c.SkinID, c.SkinClass, c.SkinCollection)";
            var feedIterator = container.GetItemQueryIterator<int>(
            new QueryDefinition(query));

            // Получаем результат запроса
            var queryResultSetIterator = await feedIterator.ReadNextAsync();
            var count = queryResultSetIterator.First();
            return count;
        }

        public static async Task<List<JObject>> GetGroupedItems(string databaseName, string containerName, int offset, string filterWeapon = null, List<string> filterSkins = null, string collection = null)
        {
            string URI = DatabaseInfo.URI;
            string privateKey = DatabaseInfo.PrivateKey;
            string condition = "";
            if (filterWeapon != null)
            {
                condition += $" AND ENDSWITH(c.SkinID, \"({filterWeapon})\")";
            }

            if (filterSkins != null && filterSkins.Count > 0)
            {
                string skins = "";
                for (int i = 0; i < filterSkins.Count; i++)
                {
                    if (i + 1 == filterSkins.Count)
                    {
                        skins += $"\"{filterSkins[i]}\"";
                    }
                    else
                    {
                        skins += $"\"{filterSkins[i]}\",";
                    }

                }
                condition += $" AND c.SkinCollection IN ({skins})";
            }

            if (collection != null)
            {
                condition += $" AND c.SkinCollection = \"{collection}\"";
            }
            var cosmosClient = new CosmosClient(URI, privateKey);
            var container = cosmosClient.GetContainer(databaseName, containerName);

            string query = $"SELECT c.SkinID, c.SkinClass, c.SkinCollection, COUNT(1) AS OffersAmount  FROM c WHERE c.IsSold = false {condition} GROUP BY c.SkinID, c.SkinClass, c.SkinCollection ORDER BY c.SkinCollection DESC OFFSET {offset} LIMIT {TradeInfo.ItemInPage}";
            var feedIterator = container.GetItemQueryIterator<JObject>(
            new QueryDefinition(query));

            List<JObject> items = new List<JObject>();
            while (feedIterator.HasMoreResults)
            {
                var response = await feedIterator.ReadNextAsync();
                foreach (var item in response)
                {
                    items.Add(item);
                }
            }
            return items;
        }
    }
}
