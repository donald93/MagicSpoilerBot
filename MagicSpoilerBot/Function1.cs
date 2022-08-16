using Discord.Webhook;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using ScryfallApi.Client;
using ScryfallApi.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MagicSpoilerBot
{
    public class Function1
    {
        public ScryfallApiClient _scryfallApi;
        public CosmosClient _cosmosClient;

        public Function1(ScryfallApiClient scryfallApi)
        {
            _scryfallApi = scryfallApi;
            _cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("CosmosUrl"), Environment.GetEnvironmentVariable("CosmosPrimaryKey"));
        }

        [FunctionName("Function1")]
        public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            _scryfallApi.Cards.Search("", 0, SearchOptions.CardSort.Released);
        }

        [FunctionName("Function2")]
        public async Task Run2([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            var cosmosContainer = _cosmosClient.GetDatabase("magic-spoiler-bot").GetContainer("Cards");
            //log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var sets = await _scryfallApi.Sets.Get();

            var upcomingSets = sets.Data
                .Where(s => s.SetType == "core" || s.SetType == "expansion" || s.SetType == "commander" || s.SetType == "funny" || s.SetType == "token" || s.SetType == "promo")
                .Where(s => s.ReleaseDate > DateTime.Now)
                .OrderBy(s => s.ReleaseDate)
                .Select(s => s.Code);

            using var feedIterator = cosmosContainer.GetItemLinqQueryable<CardDto>().Where(c => upcomingSets.Contains(c.SetCode)).ToFeedIterator();

            List<CardDto> postedCards = new();

            while(feedIterator.HasMoreResults)
            {
                var resultSet = await feedIterator.ReadNextAsync();
                foreach(CardDto card in resultSet)
                {
                    postedCards.Add(card);
                }
            }

            List<Card> unpostedCards = new();

            foreach(var set in upcomingSets)
            {
                var page = 0;
                ResultList<Card> cardResults = new();

                if(page == 0 || cardResults.HasMore)
                {
                    await Task.Delay(100);
                    try
                    {
                        cardResults = await _scryfallApi.Cards.Search($"set:{set}", page, SearchOptions.CardSort.Name);
                    }
                    catch(ScryfallApiException e)
                    {
                        continue;
                    }
                }

                unpostedCards.AddRange(cardResults.Data.Where(c => !postedCards.Select(p => p.Id).Contains(c.Id.ToString())));
            }

            var webhook = Environment.GetEnvironmentVariable("DiscordWebHook");

            var _client = new DiscordWebhookClient(webhook);
            foreach (var card in unpostedCards)
            {
                await _client.SendMessageAsync($"{card.Name}:{card.ManaCost}:{card.OracleText}\n{card.ImageUris["large"]}");
                await cosmosContainer.CreateItemAsync(card.ToDto());

            }
        }
    }
}
