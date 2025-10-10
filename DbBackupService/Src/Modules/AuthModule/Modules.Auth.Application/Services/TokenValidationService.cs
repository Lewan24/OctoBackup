using Microsoft.Extensions.Logging;
using Modules.Auth.Infrastructure.Repositories;
using Modules.Auth.Shared.Interfaces;
using OneOf;
using OneOf.Types;

namespace Modules.Auth.Application.Services;

internal class TokenValidationService(IUserTokenService tokensRepoService, ILogger<TokenValidationService> logger)
    : ITokenValidationService
{
    // TODO: Add this to configurable section in admin's configuration panel
    private readonly bool _enableTokenLogging = false;
    
    public OneOf<True, False> IsValid(string? token, string? userEmail)
    {
        if(_enableTokenLogging)
            logger.LogInformation("Validating token {Token} for user {Email}...", token, userEmail);

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userEmail))
        {
            logger.LogInformation("Token or Email is empty.");
            return new False();
        }

        if(_enableTokenLogging)
            logger.LogDebug("Searching DB with passed token...");
        var userToken = tokensRepoService.GetUserTokens(t =>
            t.Token == token &&
            t.Email?.ToUpper() == userEmail.ToUpper()).FirstOrDefault();

        if (userToken is null)
        {
            logger.LogInformation("Can't find any matched token in DB.");
            return new False();
        }

        if (string.IsNullOrWhiteSpace(userToken.Token))
        {
            logger.LogInformation("Found Token in DB, but it's empty.");
            return new False();
        }

        if (userToken.ExpirationDate <= DateTime.UtcNow)
        {
            logger.LogInformation("Token is expired.");
            return new False();
        }

        if(_enableTokenLogging)
            logger.LogInformation("Token {Token} for user {Email} is Valid.", token, userEmail);
        return new True();
    }
}