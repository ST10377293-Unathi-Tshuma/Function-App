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
    public class GetAllCustomersFunction
    {
        private readonly TableStorageService<Customer> _customerTableService;

        public GetAllCustomersFunction(TableStorageService<Customer> customerTableService)
        {
            _customerTableService = customerTableService ?? throw new ArgumentNullException(nameof(customerTableService));
        }

        [FunctionName("GetAllCustomersFunction")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing request to retrieve all customers.");

            try
            {
                var customers = await _customerTableService.GetAllEntities();

                if (customers == null)
                {
                    log.LogInformation("No customers found.");
                    return new OkObjectResult(new { Message = "No customers found.", Customers = new Customer[0] });
                }

                var response = new
                {
                    Customers = customers,
                };

                return new OkObjectResult(response);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving customers.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}