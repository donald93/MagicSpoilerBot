using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using ScryfallApi.Client;

namespace MagicSpoilerBot
{
    public class Function1
    {
        public ScryfallApiClient _scryfallApi;

        public Function1(ScryfallApiClient scryfallApi)
        {
            _scryfallApi = scryfallApi;
        }

        [FunctionName("Function1")]
        public void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
            _scryfallApi.Cards.Search("", 0, ScryfallApi.Client.Models.SearchOptions.CardSort.Released);
        }

        [FunctionName("Function2")]
        public async Task Run2([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req, 
            [DurableClient] IDurableOrchestrationClient starter, 
            ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var results = await _scryfallApi.Cards.Search("legal:commander", 0, ScryfallApi.Client.Models.SearchOptions.CardSort.Released);

            //grab the latest set
            //get all cards from latest set
            //compare all cards with cards already posted
            //post new cards
        }
    }
}
