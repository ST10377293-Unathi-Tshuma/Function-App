using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Services;
using Part2FunctionApp.Models;

namespace Part2FunctionApp.Functions
{
    public class DeleteCustomerFunction
    {
        private readonly TableStorageService<Customer> _customerTableService;
        private readonly TableStorageService<Order> _orderTableService;
        private readonly QueueStorageService _queueStorageService;

        public DeleteCustomerFunction(TableStorageService<Customer> customerTableService, TableStorageService<Order> orderTableService,QueueStorageService queueStorageService)
        {
            _customerTableService = customerTableService;
            _orderTableService = orderTableService;
            _queueStorageService = queueStorageService;
        }

        [FunctionName("DeleteCustomerFunction")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "customer/{customerId}")] HttpRequest req,
            ILogger log, string customerId)
        {
            log.LogInformation($"Deleting customer: {customerId}");

            var existingCustomer = await _customerTableService.GetEntityAsync("Customer", customerId);
            if(existingCustomer == null)
            {
                log.LogWarning($"Customer with ID {customerId} not found.");
                return new NotFoundObjectResult($"Customer with ID {customerId} not found.");
            }

            var customerOrders = await _orderTableService.GetAllEntities()
                .ContinueWith(t => t.Result.FindAll(o => o.CustomerId == customerId && o.Status != "Canceled"));

            if (customerOrders.Count > 0)
            {
                var responseMessage = $"Cannot delete customer {customerId} because they have active orders.";
                log.LogWarning(responseMessage);
                return new BadRequestObjectResult(responseMessage);
            }

            // Delete customer
            await _customerTableService.DeleteEntityAsync("Customer", customerId);


            var auditLog = new AuditLog
            {
                TableName = "Customer",
                Action = "Delete",
                DataSnapshot = JsonConvert.SerializeObject(new
                {
                    DeletedCustomer = existingCustomer.FullName,
                    CustomerId = customerId,
                })
            };

            await _queueStorageService.SendLogEntryAsync(auditLog);


            return new OkObjectResult(new
            {
                success = true,
                message = "Customer deleted successfully",
                deletedCustomerId = customerId
            });

        }
    }
}
