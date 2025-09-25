using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Part2FunctionApp.Services;

namespace Part2FunctionApp.Functions
{
    public class GetCustomerFunction
    {
        private readonly TableStorageService<Customer> _customerTableService;
        private const string PartitionKey = "Customer";

        public GetCustomerFunction(TableStorageService<Customer> customerTableService)
        {
            _customerTableService = customerTableService ?? throw new ArgumentNullException(nameof(customerTableService));
        }

        [FunctionName("GetCustomerFunction")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers/{customerId}")] HttpRequest req,
            ILogger log,
            string customerId)
        {
            log.LogInformation("Processing request to retrieve customer with ID: {CustomerId}", customerId);

            // Validate customerId
            if (string.IsNullOrWhiteSpace(customerId))
            {
                log.LogWarning("Customer ID is missing or empty.");
                return new BadRequestObjectResult("Customer ID is required.");
            }

            try
            {
                var customer = await _customerTableService.GetEntityAsync(PartitionKey, customerId);

                if (customer == null)
                {
                    log.LogWarning("Customer with ID {CustomerId} not found.", customerId);
                    return new NotFoundObjectResult($"Customer with ID {customerId} not found.");
                }

                return new OkObjectResult(customer);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving customer with ID {CustomerId}.", customerId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}