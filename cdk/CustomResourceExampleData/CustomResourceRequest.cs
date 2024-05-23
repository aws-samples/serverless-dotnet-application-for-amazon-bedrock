// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using System;
using System.Text.Json;

namespace CustomResourceExampleData;

public class CustomResourceRequest
{
    public string RequestType { get; set; }
    public string RequestId { get; set; }
    public string StackId { get; set; }
    public string ResponseURL { get; set; }
    public string ResourceType { get; set; }
    public string LogicalResourceId { get; set; }
    public string? PhysicalResourceId { get; set; }
    public JsonDocument? ResourceProperties { get; set; }
    public JsonDocument? OldResourceProperties { get; set; }

    public CustomResourceRequest()
	{
	}
}