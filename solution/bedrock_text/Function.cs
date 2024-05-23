// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace bedrock_text;

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
        string? sessionID = body["MessageGroupId"]?.ToString();
        string? newMessage = body["message"]?.ToString();
        JToken? parameters = body["params"];
        string modelId = parameters?.Value<string>("option_model") ?? "amazon.titan-text-express-v1";
        float temperature = parameters?.Value<float>("option_temperature") ?? 0F;
        float topP = parameters?.Value<float>("option_top_p") ?? 1F;
        int maxToken = parameters?.Value<int>("option_max_token") ?? 512;

        string apiRegion = sqsEvent.Records[0].AwsRegion;
        string connectionId = sqsEvent.Records[0].MessageAttributes["connectionId"].StringValue;
        string domainName = sqsEvent.Records[0].MessageAttributes["domainName"].StringValue;
        string stage = sqsEvent.Records[0].MessageAttributes["stage"].StringValue;

        string? ddbTableName = Environment.GetEnvironmentVariable("DDB_TABLE_NAME");

        // Get conversation history from DynamoDB
        AmazonDynamoDBClient DDBclient = new();

        var get_history_request = new QueryRequest
        {
            TableName = ddbTableName,
            KeyConditionExpression = "SessionId = :s_Id",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
                {":s_Id", new AttributeValue { N =  sessionID }}
            },
            ScanIndexForward = false
        };

        List<Dictionary<string, AttributeValue>> conversation_history = [];
        Dictionary<string, AttributeValue> lastEvaluatedKey;

        // Set the number of historical conversation to pull. More data provides better context to the FM but impacts performance.
        int max_historical_conversation = 6;

        do
        {
            var get_history_response = await DDBclient.QueryAsync(get_history_request);
            conversation_history.AddRange(get_history_response.Items);
            lastEvaluatedKey = get_history_response.LastEvaluatedKey;
            if (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0)
                get_history_request.ExclusiveStartKey = lastEvaluatedKey;
            else
                get_history_request.ExclusiveStartKey = null;
        } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0 && conversation_history.Count <= max_historical_conversation);

        // Initialize FM object
        var fm = new FoundationModel()
        {
            ModelId = modelId
        };

        // Generate a conversation chain using the historical conversation and the new message
        List<string> conversation_chain_desc = [$"{fm.GetUserRoleName()}{newMessage}\n{fm.GetBotRoleName()}"];
        foreach (Dictionary<string, AttributeValue> item in conversation_history.Take(max_historical_conversation))
        {
            conversation_chain_desc.Add(item["Role"].S + item["Message"].S + "\n");
        }
        conversation_chain_desc.Reverse();
        string conversation_chain = string.Join("", conversation_chain_desc);

        // Add the new message to the DDB, so it can be added to the future conversation chain
        var user_request_to_history = new PutItemRequest
        {
            TableName = ddbTableName,
            Item = new Dictionary<string, AttributeValue>()
            {
                { "SessionId", new AttributeValue { N = sessionID }},
                { "Role", new AttributeValue { S = fm.GetUserRoleName() }},
                { "Message", new AttributeValue { S = newMessage }},
                { "TTL", new AttributeValue { N = getCurrentEpoch() + 600 }} // Set TTL 10 mins (600s) from current time
            }
        };
        await DDBclient.PutItemAsync(user_request_to_history);


        // Send request to Amazon Bedrock
        AmazonBedrockRuntimeClient bedrockClient = new();

        string payload = fm.GeneratePayload(conversation_chain, temperature, topP, maxToken);
        string bot_role = "system";
        string bot_response;

        try
        {
            InvokeModelResponse response = await bedrockClient.InvokeModelAsync(new InvokeModelRequest()
            {
                ModelId = modelId,
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload),
                ContentType = "application/json",
                Accept = "application/json"
            });

            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                bot_role = "assistant";
                bot_response = fm.RetrieveAiResponse(response.Body).Trim();

                // Add the response from AI to the DDB, so it can be added to the future conversation chain
                var bot_response_to_history = new PutItemRequest
                {
                    TableName = ddbTableName,
                    Item = new Dictionary<string, AttributeValue>()
                    {
                        { "SessionId", new AttributeValue { N = sessionID }},
                        { "Role", new AttributeValue { S = fm.GetBotRoleName() }},
                        { "Message", new AttributeValue { S = bot_response }},
                        { "TTL", new AttributeValue { N = getCurrentEpoch() + 600 }} // Set TTL 10 mins (600s) from current time
                    }
                };
                await DDBclient.PutItemAsync(bot_response_to_history);
            }
            else
            {
                bot_response = "ERROR: Received status code " + response.HttpStatusCode + " from Amazon Bedrock endpoint";
            }
        }
        catch (AmazonBedrockRuntimeException e)
        {
            bot_response = "ERROR: " + e.Message;
        }

        context.Logger.LogDebug(bot_role + ": " + bot_response);

        // Sending Bedrock response to API Gateway websocket endpoint
        MemoryStream inputData = new(
            Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(new
                {
                    Role = bot_role,
                    Response = bot_response
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

    // Private function to generate current timestamp
    private static string getCurrentEpoch()
    {
        DateTime currentTime = DateTime.UtcNow;
        TimeSpan epochTimeSpan = currentTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long epochTime = (long)epochTimeSpan.TotalSeconds;

        return epochTime.ToString();
    }
}
