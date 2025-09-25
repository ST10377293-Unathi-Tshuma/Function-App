using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Part2FunctionApp.Models
{
    public class AuditLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TableName { get; set; }   // e.g., "Products"
        public string Action { get; set; }      // e.g., "Created"
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string DataSnapshot { get; set; } // JSON of changed entity

        // Queue metadata
        public string MessageId { get; set; }
        public DateTime? InsertionTime { get; set; }
        public string RawMessage { get; set; }   // Fallback for failed deserialization
    }
}
