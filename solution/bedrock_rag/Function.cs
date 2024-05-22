// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace bedrock_rag;

public class Function
{
    /// Default constructor. This constructor is used by Lambda to construct the instance.
    public Function()
    {

    }

    /// This method is called for every Lambda invocation.
    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        // Log events for debug purpose
        context.Logger.LogDebug(JsonConvert.SerializeObject(sqsEvent));

        // Parse message body and set variables
        JObject body = JObject.Parse(sqsEvent.Records[0].Body);
        string? newMessage = body["message"]?.ToString();
        JToken? parameters = body["params"];
        string? modelId = parameters?.Value<string?>("option_model") ?? "anthropic.claude-v2:1";
        string? ragSession = parameters?.Value<string?>("rag_session") ?? null;

        string? kbID = Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_ID");

        string apiRegion = sqsEvent.Records[0].AwsRegion;
        string connectionId = sqsEvent.Records[0].MessageAttributes["connectionId"].StringValue;
        string domainName = sqsEvent.Records[0].MessageAttributes["domainName"].StringValue;
        string stage = sqsEvent.Records[0].MessageAttributes["stage"].StringValue;

        // Send request to Amazon Bedrock
        AmazonBedrockAgentRuntimeClient BedrockAgentClient = new();

        string bot_role = "system";
        string bot_response;
        string bot_rag_session;

        try
        {
            RetrieveAndGenerateConfiguration config = new()
            {
                Type = "KNOWLEDGE_BASE",
                KnowledgeBaseConfiguration = new()
                {
                    KnowledgeBaseId = kbID,
                    ModelArn = "arn:aws:bedrock:" + apiRegion + "::foundation-model/" + modelId
                }
            };

            RetrieveAndGenerateResponse response = await BedrockAgentClient.RetrieveAndGenerateAsync(new RetrieveAndGenerateRequest()
            {
                Input = new()
                {
                    Text = newMessage
                },
                RetrieveAndGenerateConfiguration = config,
                SessionId = ragSession
            });

            bot_role = "assistant";
            bot_response = response.Output.Text;
            bot_rag_session = response.SessionId;
        }
        catch (AmazonBedrockAgentRuntimeException e)
        {
            bot_response = "ERROR: " + e.Message;
            bot_rag_session = "";
        }

        context.Logger.LogDebug(bot_role + ": " + bot_response);

        // Sending Bedrock response to API Gateway websocket endpoint
        MemoryStream inputData = new(
            Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(new
                {
                    Role = bot_role,
                    Response = bot_response,
                    RagSession = bot_rag_session
                })
            )
        );

        PostToConnectionRequest request_payload = new()
        {
            ConnectionId = connectionId,
            Data = inputData
        };

        AmazonApiGatewayManagementApiClient ApigwClient = new(new AmazonApiGatewayManagementApiConfig
        {
            AuthenticationRegion = apiRegion,
            ServiceURL = stage.Length > 0 ? "https://" + domainName + "/" + stage : "https://" + domainName
        });

        try
        {
            PostToConnectionResponse api_response = await ApigwClient.PostToConnectionAsync(request_payload);

            context.Logger.LogDebug("Response from API GW: " + JsonConvert.SerializeObject(api_response));
            context.Logger.LogDebug("Sucessful");
        }
        catch (Exception ex)
        {
            context.Logger.LogDebug("Exception from API GW: " + JsonConvert.SerializeObject(ex));
        }
    }
}