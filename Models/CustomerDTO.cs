using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Part2FunctionApp.Models
{
    public class CustomerDTO
    {
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string? ETag { get; set; }

        public string FullName { get; set; }
        public string Email { get; set; }
    }

}
