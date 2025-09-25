namespace Part2FunctionApp.Models
{
    public class OrderViewModel
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string CustomerName { get; set; }
        public string ProductName { get; set; }
        public string ProductImageUrl { get; set; }
        public string Status { get; set; }
    }
}