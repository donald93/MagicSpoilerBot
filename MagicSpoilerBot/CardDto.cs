using Newtonsoft.Json;

namespace MagicSpoilerBot
{
    public class CardDto
    {
        [JsonProperty(PropertyName="id")]
        public string Id { get; set; }
        public string SetCode { get; set; }
    }
}