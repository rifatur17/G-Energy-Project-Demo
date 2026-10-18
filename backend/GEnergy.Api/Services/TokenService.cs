using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GEnergy.Api.Entities;
using GEnergy.Api.DTOs;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace GEnergy.Api.Services;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int ExpirationMinutes { get; set; } = 30;
}
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public LoginResponse Create(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expiry = now.AddMinutes(o.ExpirationMinutes);
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [new("sub", user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("userCode", user.UserCode), new("role", user.Role), new("jti", Guid.NewGuid().ToString())],
            now, expiry, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), user.UserCode, user.Role, expiry);
    }
}
