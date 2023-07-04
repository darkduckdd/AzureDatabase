using MarketPlace.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MarketPlaceNew
{

    public static class DeleteItem
    {
        public static PlayFabAuthenticationContext AuthContext;
        public static string PlayFabID;
        public static string TitleID;

        [FunctionName("DeleteItem")]
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

            string playfabID = data?.PlayfabId;
            string entityToken = data?.EntityToken;
            string entityID = data?.EntityId;
            string entityType = data?.EntityType;

            string itemId = data?.ItemId;
            string titleID = data?.TitleId;

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

            Item item = await DatabaseManager.GetItem(DatabaseInfo.MarketplaceDBName, titleID, itemId);

            var deleteResult = await DatabaseManager.DeleteItem(DatabaseInfo.MarketplaceDBName, titleID, itemId);
            if (deleteResult)
            {
                var grantResult = await PlayFabServerAPI.GrantItemsToUserAsync(new PlayFab.ServerModels.GrantItemsToUserRequest
                {
                    AuthenticationContext = authContext,
                    CatalogVersion = catalogName,
                    PlayFabId = playfabID,
                    ItemIds = new List<string>() { item.SkinID }
                });
                return new OkObjectResult($"{grantResult.Result.ItemGrantResults.Count}");
            }

            return new OkObjectResult($"Can't delete");
        }
    }
}
