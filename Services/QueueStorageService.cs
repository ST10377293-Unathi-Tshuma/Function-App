using Azure.Storage.Queues;
using Part2FunctionApp.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Part2FunctionApp.Services
{
    public class QueueStorageService
    {
        private readonly QueueClient _queueClient;

        //Author Unknown(n.d.) Azure Queue Storage Using Development Storage Account, C# Corner. Available at: https://www.c-sharpcorner.com/blogs/azure-queue-storage-using-development-storageaccount
        //(Accessed: 27 August 2025).
        public QueueStorageService(string storageConnectionString, string queueName)
        {
            var queueServiceClient = new QueueServiceClient(storageConnectionString);
            _queueClient = queueServiceClient.GetQueueClient(queueName);
            _queueClient.CreateIfNotExists();
        }

        // Send an audit log entry to the queue
        public async Task SendLogEntryAsync(AuditLog log)
        {
            var jsonMessage = JsonSerializer.Serialize(log);
            var base64Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonMessage));
            await _queueClient.SendMessageAsync(base64Message);
        }

        // Get audit log entries from queue (peek only)
        public async Task<List<AuditLog>> GetLogEntriesAsync()
        {
            var entryList = new List<AuditLog>();
            var entries = await _queueClient.PeekMessagesAsync(maxMessages: 32);

            foreach (var entry in entries.Value)
            {
                try
                {
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(entry.Body.ToString()));
                    var log = JsonSerializer.Deserialize<AuditLog>(json);
                    if (log != null)
                    {
                        log.MessageId = entry.MessageId;
                        entryList.Add(log);
                    }
                }
                catch
                {
                    entryList.Add(new AuditLog
                    {
                        MessageId = entry.MessageId,
                        RawMessage = entry.Body.ToString()
                    });
                }
            }

            return entryList;
        }
    }
}