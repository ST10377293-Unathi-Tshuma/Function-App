using System;

namespace Part2FunctionApp.Models
{
    public class ProductDTO
    {
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }
        public string? ETag { get; set; }
        public string? Name { get; set; } 
        public string? Description { get; set; } 
        public int? Price { get; set; }
        public string? ImageUrl { get; set; }
    }
}
