namespace BarberHub.Api.Shared.Config;

public class JwtOptions
{
    public string Issuer { get; set; } = "BarberHub.Api";
    public string Audience { get; set; } = "BarberHub.Mobile";
    public string SigningKey { get; set; } = string.Empty;   // >= 32 chars, from user-secrets / env in prod
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
