// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Transfer;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CustomResourceExampleData;

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
                await UploadExamplesToDataBucket();
                await SyncDataSource();
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

    private static async Task UploadExamplesToDataBucket()
    {
        // Upload an example PDF file to the data bucket
        string data_bucket = Environment.GetEnvironmentVariable("DATA_BUCKET");
        string kb_dir = "/tmp/kb";

        if (!Directory.Exists(kb_dir))
        {
            Directory.CreateDirectory(kb_dir);
        }

        IList<string> urls = new List<string>
        {
            "https://s2.q4cdn.com/299287126/files/doc_financials/2024/ar/Amazon-com-Inc-2023-Annual-Report.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2024/ar/Amazon-com-Inc-2023-Shareholder-Letter.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2023/ar/Amazon-2022-Annual-Report.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2023/ar/2022-Shareholder-Letter.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2022/ar/Amazon-2021-Annual-Report.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2022/ar/2021-Shareholder-Letter.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2021/ar/Amazon-2020-Annual-Report.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2021/ar/Amazon-2020-Shareholder-Letter-and-1997-Shareholder-Letter.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2020/ar/2019-Annual-Report.pdf",
            "https://s2.q4cdn.com/299287126/files/doc_financials/2020/ar/2019-Shareholder-Letter.pdf"
        };

        var tasks = urls.Select(t => {
            var fileName = t.Substring(t.LastIndexOf('/'));
            return DownloadFile(t, kb_dir + fileName);
        }).ToArray();

        await Task.WhenAll(tasks);

        // Upload to S3
        TransferUtility fileTransferUtility = new(new AmazonS3Client());

        await fileTransferUtility.UploadDirectoryAsync(new TransferUtilityUploadDirectoryRequest
        {
            Directory = kb_dir,
            BucketName = data_bucket,
            StorageClass = "REDUCED_REDUNDANCY",
            UploadFilesConcurrently = true
        });
    }

    private static async Task DownloadFile(string url, string localFilePath)
    {
        using (HttpClient _httpClient = new())
        {
            byte[] fileBytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localFilePath, fileBytes);
        }
    }

    private static async Task SyncDataSource()
    {
        string knowledgebase_id = Environment.GetEnvironmentVariable("KNOWLEDGEBASE_ID");
        string datasource_id = Environment.GetEnvironmentVariable("DATASOURCE_ID");

        AmazonBedrockAgentClient BedrockClient = new();

        StartIngestionJobResponse start_response = await BedrockClient.StartIngestionJobAsync(new()
        {
            KnowledgeBaseId = knowledgebase_id,
            DataSourceId = datasource_id,
            ClientToken = Guid.NewGuid().ToString()
        });

        string job_id = start_response.IngestionJob.IngestionJobId;

        while (true)
        {
            GetIngestionJobResponse get_response = await BedrockClient.GetIngestionJobAsync(new()
            {
                IngestionJobId = job_id,
                KnowledgeBaseId = knowledgebase_id,
                DataSourceId = datasource_id,
            });

            string status = get_response.IngestionJob.Status;

            if (status == "STARTING" || status == "IN_PROGRESS")
            {
                // Syncing has just started or in progress. Wait 10 seconds before recheck
                Thread.Sleep(10000);
            }
            else
            {
                // It's not in progress anymore. It succeeded or failed. Either way we break out of the loop
                break;
            }
        }
    }
}

