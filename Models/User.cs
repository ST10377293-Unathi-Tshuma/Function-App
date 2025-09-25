using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace Part2FunctionApp.Models
{
    public class User : ITableEntity
    {
        public string PartitionKey { get; set; } = "User";
        public string RowKey { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    public class TokenPayload
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string Role { get; set; } = "Customer"; 
    }
}
