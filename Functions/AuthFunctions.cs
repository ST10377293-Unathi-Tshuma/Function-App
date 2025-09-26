using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;

namespace Part2FunctionApp.Functions
{
    public class AuthFunctions
    {
        private readonly TableStorageService<User> _userTableService;
        private readonly AuthService _authService;
        private readonly QueueStorageService _queueStorageService;

        public AuthFunctions(
            TableStorageService<User> userTableService,
            AuthService authService,
            QueueStorageService queueStorageService)
        {
            _userTableService = userTableService;
            _authService = authService;
            _queueStorageService = queueStorageService;
        }

        [FunctionName("Register")]
        public async Task<IActionResult> Register(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing user registration request");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var registerRequest = JsonConvert.DeserializeObject<RegisterRequest>(requestBody);

                if (registerRequest == null ||
                    string.IsNullOrWhiteSpace(registerRequest.Username) ||
                    string.IsNullOrWhiteSpace(registerRequest.Password) ||
                    string.IsNullOrWhiteSpace(registerRequest.Email))
                {
                    return new BadRequestObjectResult("Username, email, and password are required");
                }

                // Check if user already exists
                var existingUsers = await _userTableService.GetAllEntities();
                var existingUser = existingUsers.FirstOrDefault(u =>
                    u.Username.Equals(registerRequest.Username, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Equals(registerRequest.Email, StringComparison.OrdinalIgnoreCase));

                if (existingUser != null)
                {
                    return new BadRequestObjectResult("Username or email already exists");
                }

                // Validate role
                var validRoles = new[] { "Admin", "Manager", "Customer" };
                if (!validRoles.Contains(registerRequest.Role))
                {
                    registerRequest.Role = "Customer"; // Default to customer
                }

                // Create new user
                var user = new User
                {
                    RowKey = Guid.NewGuid().ToString(),
                    Username = registerRequest.Username,
                    Email = registerRequest.Email,
                    PasswordHash = _authService.HashPassword(registerRequest.Password),
                    Role = registerRequest.Role,
                    IsActive = true
                };

                await _userTableService.UpsertEntityAsync(user);

                // Audit log
                var auditLog = new AuditLog
                {
                    TableName = "Users",
                    Action = "Register",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        UserId = user.RowKey,
                        Username = user.Username,
                        Email = user.Email,
                        Role = user.Role
                    })
                };
                await _queueStorageService.SendLogEntryAsync(auditLog);

                log.LogInformation($"User {registerRequest.Username} registered successfully");

                return new OkObjectResult(new
                {
                    success = true,
                    message = "User registered successfully",
                    userId = user.RowKey,
                    username = user.Username,
                    role = user.Role
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error during user registration");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("Login")]
        public async Task<IActionResult> Login(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing user login request");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var loginRequest = JsonConvert.DeserializeObject<LoginRequest>(requestBody);

                if (loginRequest == null ||
                    string.IsNullOrWhiteSpace(loginRequest.Username) ||
                    string.IsNullOrWhiteSpace(loginRequest.Password))
                {
                    return new BadRequestObjectResult("Username and password are required");
                }

                // Find user
                var users = await _userTableService.GetAllEntities();
                var user = users.FirstOrDefault(u =>
                    u.Username.Equals(loginRequest.Username, StringComparison.OrdinalIgnoreCase) &&
                    u.IsActive);

                if (user == null || !_authService.VerifyPassword(loginRequest.Password, user.PasswordHash))
                {
                    log.LogWarning($"Failed login attempt for username: {loginRequest.Username}");
                    return new UnauthorizedObjectResult("Invalid username or password");
                }

                // Generate JWT token
                var token = _authService.GenerateJwtToken(user);

                // Audit log
                var auditLog = new AuditLog
                {
                    TableName = "Users",
                    Action = "Login",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        UserId = user.RowKey,
                        Username = user.Username,
                        Role = user.Role,
                        LoginTime = DateTime.UtcNow
                    })
                };
                await _queueStorageService.SendLogEntryAsync(auditLog);

                log.LogInformation($"User {user.Username} logged in successfully");

                return new OkObjectResult(new
                {
                    success = true,
                    message = "Login successful",
                    token = token,
                    user = new
                    {
                        userId = user.RowKey,
                        username = user.Username,
                        email = user.Email,
                        role = user.Role
                    }
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error during user login");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}