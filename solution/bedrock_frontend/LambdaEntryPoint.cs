// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.Lambda.AspNetCoreServer;

namespace bedrock_frontend;

public class LambdaEntryPoint : APIGatewayProxyFunction
{
    /// The builder has configuration, logging and Amazon API Gateway already configured. The startup class
    /// needs to be configured in this method using the UseStartup<>() method.
    protected override void Init(IWebHostBuilder builder)
    {
        builder.UseStartup<Startup>();

        // Registering MIME types that are to be treated as binary
        RegisterResponseContentEncodingForContentType("image/webp", ResponseContentEncoding.Base64);
    }

    /// Use this override to customize the services registered with the IHostBuilder. 
    protected override void Init(IHostBuilder builder)
    {
    }
}
