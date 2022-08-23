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
    public class MagicSpoilerFunction
    {
        public ScryfallApiClient _scryfallApi;
        public CosmosClient _cosmosClient;

        private readonly IEnumerable<string> _setTypesToPost = new List<string>() { "expansion", "commander", "funny", "token", "promo" };

        public MagicSpoilerFunction(ScryfallApiClient scryfallApi)
        {
            _scryfallApi = scryfallApi;
            _cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("CosmosUrl"), Environment.GetEnvironmentVariable("CosmosPrimaryKey"));
        }

        [FunctionName("CardPost_TimerTrigger")]
        public async Task CardPostTimer([TimerTrigger("0 */15 * * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var cosmosContainer = _cosmosClient.GetDatabase("magic-spoiler-bot").GetContainer("Cards");

            var sets = await _scryfallApi.Sets.Get();

            var upcomingSets = sets.Data
                .Where(s => _setTypesToPost.Contains(s.SetType))
                .Where(s => s.ReleaseDate > DateTime.Now)
                .Select(s => s.Code);

            log.LogInformation($"Upcoming sets: ", upcomingSets.ToList());

            using var feedIterator = cosmosContainer.GetItemLinqQueryable<CardDto>().Where(c => upcomingSets.Contains(c.SetCode)).ToFeedIterator();

            List<CardDto> postedCards = new();

            while (feedIterator.HasMoreResults)
            {
                var resultSet = await feedIterator.ReadNextAsync();

                log.LogInformation($"Found {resultSet.Count} posted cards");

                foreach (CardDto card in resultSet)
                {
                    postedCards.Add(card);
                }
            }

            List<Card> unpostedCards = new();

            foreach (var set in upcomingSets)
            {
                var page = 0;
                ResultList<Card> cardResults = new();

                if (page == 0 || cardResults.HasMore)
                {
                    await Task.Delay(100);
                    try
                    {
                        cardResults = await _scryfallApi.Cards.Search($"set:{set}", page, SearchOptions.CardSort.Name);
                    }
                    catch (ScryfallApiException e)
                    {
                        continue;
                    }
                }

                unpostedCards.AddRange(cardResults.Data.Where(c => !postedCards.Select(p => p.Id).Contains(c.Id.ToString()) && c.Rarity != "common"));
            }

            log.LogInformation($"Found {unpostedCards.Count} unposted cards");

            var webhook = Environment.GetEnvironmentVariable("DiscordWebHook");

            var _client = new DiscordWebhookClient(webhook);
            foreach (var card in unpostedCards)
            {
                log.LogInformation($"Posting {card.Name}");
                await _client.SendMessageAsync($"{card.Name}:{card.ManaCost}:{card.OracleText}\n{card.ImageUris["large"]}");
                await cosmosContainer.UpsertItemAsync(card.ToDto());
            }
        }

        [FunctionName("FetchUpcomingSets")]
        public async Task<IEnumerable<string>> FetchUpcomingSets([ActivityTrigger] IDurableActivityContext context, ILogger log)
        {
            var sets = await _scryfallApi.Sets.Get();

            var upcomingSets = sets.Data
                .Where(s => _setTypesToPost.Contains(s.SetType))
                .Where(s => s.ReleaseDate > DateTime.Now)
                .Select(s => s.Code);

            log.LogInformation($"Upcoming sets: ", upcomingSets.ToList());

            return upcomingSets;
        }

        [FunctionName("GetPostedCards")]
        public async Task<IEnumerable<CardDto>> GetPostedCards([ActivityTrigger] IEnumerable<string> upcomingSets, ILogger log)
        {
            var cosmosContainer = _cosmosClient.GetDatabase("magic-spoiler-bot").GetContainer("Cards");

            using var feedIterator = cosmosContainer.GetItemLinqQueryable<CardDto>().Where(c => upcomingSets.Contains(c.SetCode)).ToFeedIterator();

            List<CardDto> postedCards = new();

            while (feedIterator.HasMoreResults)
            {
                var resultSet = await feedIterator.ReadNextAsync();

                log.LogInformation($"Found {resultSet.Count} posted cards");

                foreach (CardDto card in resultSet)
                {
                    postedCards.Add(card);
                }
            }

            return postedCards;
        }

        [FunctionName("FindUnpostedCards")]
        public async Task<IEnumerable<Card>> FindUnpostedCards([ActivityTrigger] IEnumerable<string> upcomingSets, IEnumerable<CardDto> postedCards, ILogger log)
        {
            List<Card> unpostedCards = new();

            foreach (var set in upcomingSets)
            {
                var page = 0;
                ResultList<Card> cardResults = new();

                if (page == 0 || cardResults.HasMore)
                {
                    await Task.Delay(100);
                    try
                    {
                        cardResults = await _scryfallApi.Cards.Search($"set:{set}", page, SearchOptions.CardSort.Name);
                    }
                    catch (ScryfallApiException e)
                    {
                        continue;
                    }
                }

                unpostedCards.AddRange(cardResults.Data.Where(c => !postedCards.Select(p => p.Id).Contains(c.Id.ToString()) && c.Rarity != "common"));
            }

            log.LogInformation($"Found {unpostedCards.Count} unposted cards");

            return unpostedCards;
        }

        [FunctionName("PostCardToDiscordChannel")]
        public async Task PostCardToDiscordChannel([ActivityTrigger] Card card, string webhook, ILogger log)
        {
            log.LogInformation($"Posting {card.Name}");

            var _client = new DiscordWebhookClient(webhook);

            await _client.SendMessageAsync($"{card.Name}:{card.ManaCost}:{card.OracleText}\n{card.ImageUris["large"]}");
            var cosmosContainer = _cosmosClient.GetDatabase("magic-spoiler-bot").GetContainer("Cards");

            await cosmosContainer.UpsertItemAsync(card.ToDto());
        }


        [FunctionName("CardPost_HttpTrigger")]
        public async Task CardPostHttp([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var cosmosContainer = _cosmosClient.GetDatabase("magic-spoiler-bot").GetContainer("Cards");

            var sets = await _scryfallApi.Sets.Get();

            var upcomingSets = sets.Data
                .Where(s => _setTypesToPost.Contains(s.SetType))
                .Where(s => s.ReleaseDate > DateTime.Now)
                .Select(s => s.Code);

            log.LogInformation($"Upcoming sets: ", upcomingSets.ToList());

            using var feedIterator = cosmosContainer.GetItemLinqQueryable<CardDto>().Where(c => upcomingSets.Contains(c.SetCode)).ToFeedIterator();

            List<CardDto> postedCards = new();

            while (feedIterator.HasMoreResults)
            {
                var resultSet = await feedIterator.ReadNextAsync();

                log.LogInformation($"Found {resultSet.Count} posted cards");

                foreach (CardDto card in resultSet)
                {
                    postedCards.Add(card);
                }
            }

            List<Card> unpostedCards = new();

            foreach (var set in upcomingSets)
            {
                var page = 0;
                ResultList<Card> cardResults = new();

                if (page == 0 || cardResults.HasMore)
                {
                    await Task.Delay(100);
                    try
                    {
                        cardResults = await _scryfallApi.Cards.Search($"set:{set}", page, SearchOptions.CardSort.Name);
                    }
                    catch (ScryfallApiException e)
                    {
                        continue;
                    }
                }

                unpostedCards.AddRange(cardResults.Data.Where(c => !postedCards.Select(p => p.Id).Contains(c.Id.ToString()) && c.Rarity != "common"));
            }

            log.LogInformation($"Found {unpostedCards.Count} unposted cards");

            var webhook = Environment.GetEnvironmentVariable("DiscordWebHook");

            foreach (var card in unpostedCards)
            {
                log.LogInformation($"Posting {card.Name}");
                var _client = new DiscordWebhookClient(webhook);

                await _client.SendMessageAsync($"{card.Name}:{card.ManaCost}:{card.OracleText}\n{card.ImageUris["large"]}");
                await cosmosContainer.UpsertItemAsync(card.ToDto());
            }
        }
    }
}
