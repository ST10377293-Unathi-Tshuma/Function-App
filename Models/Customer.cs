using Azure;
using Azure.Data.Tables;
using System;

namespace Part2FunctionApp{
    public class Customer : ITableEntity
    {
        public string? PartitionKey { get; set; } = "Customer";
        public string? RowKey { get; set; } 
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
