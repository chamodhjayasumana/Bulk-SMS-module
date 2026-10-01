using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BulkSms.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthUserOptions _users;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IOptions<AuthUserOptions> users, IOptions<JwtOptions> jwt, ILogger<AuthController> logger)
    {
        _users = users.Value;
        _jwt = jwt.Value;
        _logger = logger;
    }

    public record LoginRequest(string Username, string Password);

    public record LoginResponse(string Token, string AccessType, string UserId, DateTime ExpiresAt);

    [HttpPost("token")]
    public ActionResult<ApiResponse<LoginResponse>> Token([FromBody] LoginRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResponse<LoginResponse>.Fail("Username and password are required."));

        var user = _users.Users.FirstOrDefault(u =>
            string.Equals(u.Username, request.Username, StringComparison.OrdinalIgnoreCase) &&
            u.Password == request.Password);

        if (user is null)
        {
            _logger.LogWarning("Failed login attempt for username {Username}", request.Username);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Invalid credentials."));
        }

        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiryMinutes);
        var token = CreateToken(user, expires);

        _logger.LogInformation("User {UserId} authenticated with accessType {AccessType}", user.UserId, user.AccessType);

        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse(token, user.AccessType, user.UserId, expires)));
    }

    private string CreateToken(AuthUser user, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim("id", user.UserId),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("accessType", user.AccessType)
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
