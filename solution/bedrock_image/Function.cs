// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Transfer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace bedrock_image;

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
        string? newMessage = body["message"]?.ToString() ?? "";
        JToken? parameters = body["params"];
        string? modelId = parameters?.Value<string>("option_model") ?? "amazon.titan-image-generator-v1";
        string[]? resolution = parameters?.Value<string>("option_resolution")?.Split("x");
        int width = Int32.TryParse(resolution?[0], out width) ? width : 512;
        int height = Int32.TryParse(resolution?[1], out height) ? height : 512;
        float cfgScale = parameters?.Value<float?>("option_cfgscale") ?? 8.0F;
        string? sampler = parameters?.Value<string?>("option_sampler");
        string? stylePreset = parameters?.Value<string?>("option_style_preset");

        string apiRegion = sqsEvent.Records[0].AwsRegion;
        string connectionId = sqsEvent.Records[0].MessageAttributes["connectionId"].StringValue;
        string domainName = sqsEvent.Records[0].MessageAttributes["domainName"].StringValue;
        string stage = sqsEvent.Records[0].MessageAttributes["stage"].StringValue;

        // Initialize FM object
        var fm = new FoundationModel()
        {
            ModelId = modelId
        };

        string bot_role = "system";
        string bot_response;

        // Send request to Amazon Bedrock
        AmazonBedrockRuntimeClient bedrockClient = new();

        try
        {
            InvokeModelResponse response = await bedrockClient.InvokeModelAsync(fm.ModelRequest(new FoundationModelProperties()
            {
                Text = newMessage,
                Width = width,
                Height = height,
                CfgScale = cfgScale,
                Sampler = sampler,
                StylePreset = stylePreset
            }));

            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                string base64imageString = fm.RetrieveAiResponse(response.Body);

                if (base64imageString.StartsWith("ERROR"))
                {
                    bot_response = "ERROR: Could not parse response from Amazon Bedrock endpoint";
                }
                else
                {
                    // Save the image into ephemeral storage, then upload it to a S3 bucket
                    // Generate and return the CloudFront URL of the image
                    string fileName = sessionID + "_" + getCurrentEpoch() + ".png";
                    string filePath = "/tmp/" + fileName;

                    File.WriteAllBytes(filePath, Convert.FromBase64String(base64imageString));

                    var fileTransferUtility = new TransferUtility(new AmazonS3Client());
                    var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME");
                    var bucketPrefix = "images/";

                    var fileTransferRequest = new TransferUtilityUploadRequest()
                    {
                        FilePath = filePath,
                        BucketName = bucketName,
                        Key = bucketPrefix + fileName,
                        StorageClass = "REDUCED_REDUNDANCY"
                    };
                    fileTransferUtility.Upload(fileTransferRequest);

                    var distributionDomain = Environment.GetEnvironmentVariable("CF_DISTRIBUTION_DOMAIN");
                    bot_role = "assistant";
                    bot_response = "https://" + distributionDomain + "/images/" + fileName;
                }
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

        // Send Bedrock response to API Gateway websocket endpoint
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