// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenSearch.Client;
using OpenSearch.Net;
using OpenSearch.Net.Auth.AwsSigV4;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CustomResourceAossIndex;

public class Function
{
    public async Task<string> FunctionHandler(CustomResourceRequest inputRequest, ILambdaContext context)
    {
        // Log events for debug purpose
        context.Logger.LogDebug("Event payloads:" + JsonConvert.SerializeObject(inputRequest));

        CustomResourceResponse sendResponse = new()
        {
            Status = "SUCCESS",
            StackId = inputRequest.StackId,
            RequestId = inputRequest.RequestId,
            LogicalResourceId = inputRequest.LogicalResourceId,
            PhysicalResourceId = inputRequest.PhysicalResourceId ?? "bedrock-dotnet-demo-kb-default-index",
            Data = new(),
            Reason = "N/A"
        };

        try
        {
            if (inputRequest.RequestType == "Create" || inputRequest.RequestType == "Update")
            {
                var createAossIndexResult = await CreateAossIndex();
                context.Logger.LogDebug("createAossIndexResult: " + JsonConvert.SerializeObject(createAossIndexResult));
            }
        }
        catch (Exception ex)
        {
            sendResponse.Status = "FAILED";
            sendResponse.Reason = "Failed: " + ex.Message;
        }

        context.Logger.LogDebug("Following data to be sent to CFn: " + JsonConvert.SerializeObject(sendResponse));

        return await CustomResourceResponse.CompleteCustomResourceResponse(inputRequest.ResponseURL, sendResponse);
    }

    private static async Task<string> CreateAossIndex()
    {
        // Create default index in the vector collection of the OpenSearch Serverless
        string aoss_collection_endpoint = Environment.GetEnvironmentVariable("AOSS_COLLECTION_ENDPOINT");

        Uri endpoint = new(aoss_collection_endpoint);
        AwsSigV4HttpConnection connection = new(service: "aoss");
        ConnectionSettings config = new(endpoint, connection);
        OpenSearchClient AossClient = new(config);

        string IndexName = "bedrock-dotnet-demo-kb-default-index";
        string IndexConfig = @"
            {
                ""settings"": {
                    ""knn"": ""true""
                },
                ""mappings"": {
                    ""properties"": {
                        ""AMAZON_BEDROCK_METADATA"": {
                            ""type"": ""text"",
                            ""index"": false
                        },
                        ""AMAZON_BEDROCK_TEXT_CHUNK"": {
                            ""type"": ""text""
                        },
                        ""bedrock-dotnet-demo-kb-default-vector"": {
                            ""type"": ""knn_vector"",
                            ""dimension"": 1536,
                            ""method"": {
                                ""engine"": ""faiss"",
                                ""space_type"": ""l2"",
                                ""name"": ""hnsw"",
                                ""parameters"": {}
                            }
                        }
                    }
                }
            }
        ";

        StringResponse AossResponse = new();
        for(int retry = 0; retry < 3; retry++)
        {
            try
            {
                AossResponse = await AossClient.LowLevel.Indices.CreateAsync<StringResponse>(IndexName, IndexConfig);

                JObject AossResponseBody = JObject.Parse(AossResponse.Body);

                if (AossResponseBody.ContainsKey("acknowledged") && AossResponseBody.Value<bool>("acknowledged") == true)
                {
                    // SUCCEEDED to create Index: Wait 1 minute as sometime it takes time for the index to be available for the Bedrock KB
                    Thread.Sleep(60000);
                    break;
                }
                else
                {
                    // FAILED to create Index: Wait 10 seconds before retrying
                    Thread.Sleep(10000);
                }
            }
            catch (Exception ex)
            {
                // FAILED to create Index: Wait 10 seconds before retrying
                Thread.Sleep(10000);
                continue;
            }
        }

        return AossResponse.Body;
    }
}

