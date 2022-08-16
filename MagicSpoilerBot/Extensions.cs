using ScryfallApi.Client.Models;

namespace MagicSpoilerBot
{
    public static class Extensions
    {
        public static CardDto ToDto(this Card card)
        {
            return new CardDto()
            {
                Id = card.Id.ToString(),
                SetCode = card.Set,
            };
        }
    }
}
