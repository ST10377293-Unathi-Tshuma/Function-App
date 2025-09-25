using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;
using System;
using System.Threading.Tasks;

namespace Part2FunctionApp.Functions
{
    public class UpdateCustomerFunction
    {
        private readonly TableStorageService<Customer> _customerTableService;
        private readonly QueueStorageService _queueStorageService;

        public UpdateCustomerFunction(TableStorageService<Customer> customerTableService, QueueStorageService queueStorageService)
        {
            _customerTableService = customerTableService;
            _queueStorageService = queueStorageService;
        }

        [FunctionName("UpdateCustomer")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customers/{customerId}")] HttpRequest req,
            string customerId,
            ILogger log)
        {
            log.LogInformation($"Processing request to update customer with PartitionKey: Customer, RowKey: {customerId}");

            // Read form data instead of raw JSON to support multipart/form-data
            var formData = await req.ReadFormAsync();
            var fullName = formData["FullName"];
            var email = formData["Email"];

            // Retrieve the existing customer
            var existingCustomer = await _customerTableService.GetEntityAsync("Customer", customerId);
            if (existingCustomer == null)
            {
                log.LogWarning($"Customer with RowKey {customerId} not found.");
                return new NotFoundObjectResult(new { success = false, message = "Customer not found" });
            }


            // Update only provided fields
            if (!string.IsNullOrEmpty(fullName)) existingCustomer.FullName = fullName;
            if (!string.IsNullOrEmpty(email)) existingCustomer.Email = email;

            // Save updated customer
            await _customerTableService.UpsertEntityAsync(existingCustomer);

            // Create DTO
            var updatedCustomerDto = new CustomerDTO
            {
                PartitionKey = existingCustomer.PartitionKey,
                RowKey = existingCustomer.RowKey,
                Timestamp = existingCustomer.Timestamp,
                ETag = existingCustomer.ETag.ToString(),
                FullName = existingCustomer.FullName,
                Email = existingCustomer.Email,
            };

            // Audit log
            var audit = new AuditLog
            {
                TableName = "Customers",
                Action = "Update",
                DataSnapshot = JsonConvert.SerializeObject(updatedCustomerDto),
                Timestamp = DateTime.UtcNow
            };

            await _queueStorageService.SendLogEntryAsync(audit);

            log.LogInformation($"Customer {customerId} updated successfully.");
            return new OkObjectResult(updatedCustomerDto);
        }
    }
}
