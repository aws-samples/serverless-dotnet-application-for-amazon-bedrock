// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using System;
using Amazon.Lambda.Core;
using Newtonsoft.Json;

namespace CustomResourceAossIndex;

public class CustomResourceResponse
{
    public string Status { get; set; }
    public string RequestId { get; set; }
    public string StackId { get; set; }
    public string PhysicalResourceId { get; set; }
    public string LogicalResourceId { get; set; }
    public string Reason { get; set; }
    public object Data { get; set; }

    public static async Task<string> CompleteCustomResourceResponse(string responseURL, CustomResourceResponse responseBody)
    {
        try
        {
            using (var client = new HttpClient())
            {
                var jsonContent = new StringContent(JsonConvert.SerializeObject(responseBody));
                jsonContent.Headers.Remove("Content-Type");
                var postResponse = await client.PutAsync(responseURL, jsonContent);
                postResponse.EnsureSuccessStatusCode();
            }

            return "Successfully sent to CloudFormation";
        }
        catch (Exception ex)
        {
            return "Failed to sent to CloudFormation: " + ex.Message;
        }
    }
}

