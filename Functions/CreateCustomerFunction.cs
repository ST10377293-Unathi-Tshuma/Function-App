using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Part2FunctionApp.Functions
{
    public class CreateCustomerFunction
    {
        private readonly TableStorageService<Customer> _customerTableService;
        private readonly QueueStorageService _queueService;

        public CreateCustomerFunction(TableStorageService<Customer> customerTableService, QueueStorageService queueService)
        {
            _customerTableService = customerTableService ?? throw new ArgumentNullException(nameof(_customerTableService));
            _queueService = queueService;
        }

        [FunctionName("CreateCustomer")]
        public async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customers")] HttpRequest req,
    ILogger log)
        {
            log.LogInformation("Processing customer creation request");

            // Read form data
            var formData = await req.ReadFormAsync();
            var fullName = formData["FullName"];
            var email = formData["Email"];

            // Create new customer entity
            var customer = new Customer
            {
                PartitionKey = "Customer",
                RowKey = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                FullName = fullName,
                Email = email,
            };

            // Save to table
            await _customerTableService.UpsertEntityAsync(customer);

            // Return DTO
            var customerDto = new CustomerDTO
            {
                PartitionKey = customer.PartitionKey,
                RowKey = customer.RowKey,
                Timestamp = customer.Timestamp,
                ETag = customer.ETag.ToString(),
                FullName = customer.FullName,
                Email = customer.Email
            };

            return new OkObjectResult(customerDto);
        }


    }
}
