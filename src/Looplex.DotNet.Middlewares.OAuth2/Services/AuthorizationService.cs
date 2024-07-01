﻿using Looplex.DotNet.Core.Common.Utils;
using Looplex.DotNet.Middlewares.OAuth2.DTOs;
using Looplex.OpenForExtension.Commands;
using Looplex.OpenForExtension.Context;
using Looplex.OpenForExtension.ExtensionMethods;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Security.Claims;
using Looplex.DotNet.Core.Application.ExtensionMethods;

namespace Looplex.DotNet.Middlewares.OAuth2.Services
{
    public class AuthorizationService(
        IConfiguration configuration,
        IClientCredentialService clientStorageService,
        IIdTokenService idTokenService) : IAuthorizationService
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly IClientCredentialService _clientStorageService = clientStorageService;
        private readonly IIdTokenService _idTokenService = idTokenService;

        public async Task CreateAccessToken(IDefaultContext context)
        {
            context.Plugins.Execute<IHandleInput>(context);
            string authorization = context.GetRequiredValue<string>("Authorization");
            var clientCredentialsDTO = context.GetRequiredValue<ClientCredentialsDTO>("ClientCredentialsDTO");

            context.Plugins.Execute<IValidateInput>(context);
            ValidateAuthorizationHeader(authorization);
            ValidateGrantType(clientCredentialsDTO);
            string? email = ValidateIdToken(clientCredentialsDTO);
            await ValidateClientCredentials(authorization![7..]);

            context.Plugins.Execute<IDefineActors>(context);

            context.Plugins.Execute<IBind>(context);

            context.Plugins.Execute<IBeforeAction>(context);

            if (!context.SkipDefaultAction)
            {
                string accessToken = CreateAccessToken(email!);

                context.Result = accessToken;
            }

            context.Plugins.Execute<IAfterAction>(context);

            context.Plugins.Execute<IReleaseUnmanagedResources>(context);
        }

        private static void ValidateAuthorizationHeader(string? authorization)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer "))
            {
                throw new HttpRequestException("Invalid authorization.", null, HttpStatusCode.Unauthorized);
            }
        }

        private static void ValidateGrantType(ClientCredentialsDTO clientCredentialsDTO)
        {
            if (clientCredentialsDTO.GrantType != "client_credentials")
            {
                throw new HttpRequestException("grant_type is invalid.", null, HttpStatusCode.Unauthorized);
            }
        }

        private string ValidateIdToken(ClientCredentialsDTO clientCredentialsDTO)
        {
            var oicdAudience = _configuration["OicdAudience"]!;
            var oicdIssuer = _configuration["OicdIssuer"]!;
            var oicdTenantId = _configuration["OicdTenantId"]!;

            if (!_idTokenService.ValidateIdToken(oicdIssuer, oicdTenantId, oicdAudience, clientCredentialsDTO.IdToken, out string? email))
            {
                throw new HttpRequestException("IdToken is invalid.", null, HttpStatusCode.Unauthorized);
            }
            return email!;
        }

        private async Task ValidateClientCredentials(string credentials)
        {
            string[] parts = StringUtils.Base64Decode(credentials).Split(':');

            if (parts.Length != 2)
            {
                throw new HttpRequestException("Invalid credentials format.", null, HttpStatusCode.Unauthorized);
            }

            Guid clientId = Guid.Parse(parts[0]);
            string clientSecret = parts[1];

            Guid adminClientId = Guid.Parse(_configuration["AdminClientId"]!);
            string adminClientSecret = _configuration["AdminClientSecret"]!;

            if (clientId != adminClientId || clientSecret != adminClientSecret)
            { 
                var client = await _clientStorageService.GetByIdAndSecretOrDefaultAsync(clientId, clientSecret);
                if (client == default)
                {
                    throw new HttpRequestException("Invalid clientId or clientSecret.", null, HttpStatusCode.Unauthorized);
                }
                if (client.NotBefore > DateTime.UtcNow)
                {
                    throw new HttpRequestException("Client access not allowed.", null, HttpStatusCode.Unauthorized);
                }
                if (client.ExpirationTime <= DateTime.UtcNow)
                {
                    throw new HttpRequestException("Client access is expired.", null, HttpStatusCode.Unauthorized);
                }
            }
        }

        private string CreateAccessToken(string email)
        {
            var claims = new ClaimsIdentity([
                new(ClaimTypes.Email, email!),
            ]);

            var audience = _configuration["Audience"]!;
            var issuer = _configuration["Issuer"]!;
            var tokenExpirationTimeInMinutes = _configuration.GetValue<int>("TokenExpirationTimeInMinutes");

            using var jwtService = new JwtService(
                StringUtils.Base64Decode(_configuration["PrivateKey"]!),
                StringUtils.Base64Decode(_configuration["PublicKey"]!));
            var accessToken = jwtService.GenerateToken(issuer, audience, claims, TimeSpan.FromMinutes(tokenExpirationTimeInMinutes));
            return accessToken;
        }
    }
}
