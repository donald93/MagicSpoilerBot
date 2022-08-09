using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord.Rest;
using Discord.Webhook;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using ScryfallApi.Client;
using ScryfallApi.Client.Models;

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
            
            _scryfallApi.Cards.Search("", 0, SearchOptions.CardSort.Released);
        }

        [FunctionName("Function2")]
        public async Task Run2([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req, 
            [DurableClient] IDurableOrchestrationClient starter, 
            ILogger log)
        {
            //log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var sets = await _scryfallApi.Sets.Get();

            var upcomingSets = sets.Data
                .Where(s => s.SetType == "core" || s.SetType == "expansion" || s.SetType == "commander" || s.SetType == "funny" || s.SetType == "token" || s.SetType == "promo")
                .Where(s => s.ReleaseDate > DateTime.Now)
                .OrderBy(s => s.ReleaseDate)
                .Select(s => s.Code);

            //for testing
            var set = upcomingSets.First();

            var cardResults = await _scryfallApi.Cards.Search($"set:{set}", 0, SearchOptions.CardSort.Name);

            var postedCards = new List<Card>();

            var unpostedCards = cardResults.Data.Except(postedCards);

            var webhook = Environment.GetEnvironmentVariable("DiscordWebHook");

            var _client = new DiscordWebhookClient(webhook);
            foreach (var card in unpostedCards)
            {
               await _client.SendMessageAsync($"{card.Name}:{card.ManaCost}:{card.OracleText}\n{card.ImageUris["large"]}");
            }
        }
    }
}
