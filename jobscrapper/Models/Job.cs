using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace jobscrapper.Models
{
    public class Job
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("company")]
        public string Company { get; set; } = string.Empty;
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;
        [JsonPropertyName("location")]
        public string Location { get; set; } = string.Empty;
        [JsonPropertyName("salary")]
        public string Salary { get; set; } = string.Empty;
        [JsonPropertyName("posteddate")]

        public string PostedDate { get; set; } = string.Empty;

        [JsonPropertyName("shortdescription")]
        public string ShortDescription { get; set; } = string.Empty;

        [JsonPropertyName("fulldescription")]
        public string FullDescription { get; set; } = string.Empty;
        [JsonPropertyName("link")]

        public string Link { get; set; } = string.Empty;
        [JsonPropertyName("scrapedat")]

        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("jobtext")]

        public string JobText { get; set; } = string.Empty; // Concat for ML
        [JsonIgnore]
        public string FullText => $"{Title} {ShortDescription}";
    }

    //public class Job
    //{
    //    [JsonPropertyName("id")]
    //    public string? Id { get; set; }

    //    [JsonPropertyName("title")]
    //    public string Title { get; set; } = "";

    //    [JsonPropertyName("company")]
    //    public string Company { get; set; } = "";

    //    [JsonPropertyName("description")]
    //    public string Description { get; set; } = "";

    //    [JsonPropertyName("category")]
    //    public string Category { get; set; } = "";  // e.g., "IT", "Marketing"

    //    [JsonPropertyName("skills")]
    //    public List<string>? Skills { get; set; } = new();

    //    [JsonPropertyName("location")]
    //    public string? Location { get; set; }

    //    [JsonPropertyName("salary")]
    //    public string? Salary { get; set; }

    //    [JsonPropertyName("url")]
    //    public string? Url { get; set; }

    //    // Computed for ML (full text for embedding)
    //    [JsonIgnore]  // Don't serialize this
    //    public string FullText => $"{Title} {Company} {Description} {string.Join(" ", Skills ?? new List<string>())}";

    //    // For pretty printing
    //    public override string ToString() => $"{Title} @ {Company} [{Category}]";
    //}

}
