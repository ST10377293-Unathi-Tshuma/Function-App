using Azure;
using System;
using Azure.Data.Tables;

namespace Part2FunctionApp.Models
{
    public class Order : ITableEntity
    {
        public string PartitionKey { get; set; } = "Order";
        public string? RowKey { get; set; } // unique ID, usually GUID
        public string CustomerId { get; set; } = null!;// links to Customer RowKey
        public string ProductId { get; set; } = null!; // links to Product RowKey
        public string Status { get; set; } = "Processing";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
