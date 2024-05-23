// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Linq;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace bedrock_authorizer;

public class Function
{
    /// Default constructor. This constructor is used by Lambda to construct the instance.
    public Function() 
    { 
    
    }

    public async Task<APIGatewayCustomAuthorizerResponse> FunctionHandler(APIGatewayCustomAuthorizerRequest request, ILambdaContext context)
    {
        // Log events for debug purpose
        context.Logger.LogDebug(JsonConvert.SerializeObject(request));

        try
        {
            request.QueryStringParameters.TryGetValue("token", out string? token);

            if (token is null)
            {
                throw new ValidationException("Unable to locate any authentication token in the request");
            }

            ClaimsPrincipal? claimsPrincipal = await ValidateTokenAndGetClaimsPrincipal(token, context);

            if (claimsPrincipal is not null)
            {
                string? UUID = claimsPrincipal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

                return GetPolicy("Allow", request.RequestContext.ApiId ?? "*", UUID ?? "xxxx-yyyy");
            }

            return GetPolicy("Deny", "*", "xxxx-yyyy");
        }
        catch(Exception ex)
        {
            context.Logger.LogDebug("Returning DENY policy. An exception occured: " + JsonConvert.SerializeObject(ex));
            return GetPolicy("Deny", "*", "xxxx-yyyy");
        }
    }

    private static APIGatewayCustomAuthorizerResponse GetPolicy(string PolicyEffect, string ApiId, string UUID)
    {
        // Generate an output for the APIGW Lambda Authorizer
        return new APIGatewayCustomAuthorizerResponse
        {
            PrincipalID = UUID,
            PolicyDocument = new APIGatewayCustomAuthorizerPolicy()
            {
                Statement =
                [
                    new()
                    {
                        Effect = PolicyEffect,
                        Resource = ["arn:aws:execute-api:*:*:"+ ApiId + "/*"],
                        Action = ["execute-api:Invoke"]
                    }
                ]
            }
        };
    }

    private static async Task<ClaimsPrincipal?> ValidateTokenAndGetClaimsPrincipal(string token, ILambdaContext context)
    {
        string? CognitoAppClientId = Environment.GetEnvironmentVariable("COGNITO_APP_CLIENT_ID");

        try
        {
            if (CognitoAppClientId is null)
            {
                throw new ValidationException("Unable to get COGNITO_APP_CLIENT_ID environment variable");
            }

            // Validation of the id_token
            var validationParams = new TokenValidationParameters()
            {
                ValidateLifetime = true,
                ValidateIssuer = false,
                ValidateAudience = true,
                ValidAudience = CognitoAppClientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = await GetSecurityKeys(context)
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            return tokenHandler.ValidateToken(token, validationParams, out SecurityToken securityToken);
        }
        catch(Exception ex)
        {
            context.Logger.LogDebug("Exception occured while validating token: " + JsonConvert.SerializeObject(ex));
            return null;
        }

    }

    private static async Task<List<SecurityKey>> GetSecurityKeys(ILambdaContext context)
    {
        string? AwsRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        string? CognitoUserPoolId = Environment.GetEnvironmentVariable("COGNITO_USER_POOL_ID");

        List<SecurityKey> SecurityKeys = [];

        try
        {
            if(AwsRegion is null || CognitoUserPoolId is null)
            {
                throw new ValidationException("Unable to get AWS_REGION and/or COGNITO_USER_POOL_ID environment variable(s)");
            }

            // Getting keys' details from jwks.json file from AWS and generating SecurityKey off those
            HttpClient client = new();
            string response = await client.GetStringAsync("https://cognito-idp." + AwsRegion + ".amazonaws.com/" + CognitoUserPoolId + "/.well-known/jwks.json");
            JObject jwks = JObject.Parse(response);
            foreach (var (key, rsa) in from key in jwks["keys"]?.Where(key => key["kty"]?.ToString() == "RSA" && key["alg"]?.ToString() == "RS256")
                                       let rsa = new RSACryptoServiceProvider()
                                       select (key, rsa))
            {
                rsa.ImportParameters(
                                new RSAParameters()
                                {
                                    Modulus = FromBase64Url(key["n"].ToString()),
                                    Exponent = FromBase64Url(key["e"].ToString())
                                }
                            );
                SecurityKeys.Add(new RsaSecurityKey(rsa));
            }

            return SecurityKeys;
        }
        catch(Exception ex)
        {
            context.Logger.LogDebug("Exception occured while generating SecurityKeys: " + JsonConvert.SerializeObject(ex));
            return SecurityKeys;
        }
    }

    private static byte[] FromBase64Url(string base64Url)
    {
        // Modulus and Exponent from AWS are not valid Base64 format and requires conversion
        string padded = base64Url.Length % 4 == 0 ? base64Url : base64Url + "===="[(base64Url.Length % 4)..];
        string base64 = padded.Replace("_", "/").Replace("-", "+");

        return Convert.FromBase64String(base64);
    }
}
