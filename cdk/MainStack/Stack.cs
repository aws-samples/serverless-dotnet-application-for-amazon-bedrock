// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.CDK;
using Amazon.CDK.AwsBedrock;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AwsApigatewayv2Integrations;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.OpenSearchServerless;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SQS;
using Constructs;
using System;
using System.Collections.Generic;
using static Amazon.CDK.AwsBedrock.CfnKnowledgeBase;
using static Amazon.CDK.AwsBedrock.CfnDataSource;
using Function = Amazon.CDK.AWS.Lambda.Function;
using FunctionProps = Amazon.CDK.AWS.Lambda.FunctionProps;

namespace BedrockDotnetDemoStack;

public class BedrockDotnetDemoStackProps : StackProps {}

/// <summary>
/// AWS Serverless Bedrock-DotNet Demo
/// </summary>
public class BedrockDotnetDemoStack : Stack
{
    public BedrockDotnetDemoStack(Construct scope, string id, BedrockDotnetDemoStackProps props = null) : base(scope, id, props)
    {
        ///
        /// Parameters
        ///
        var CognitoDomainPrefix = new CfnParameter(this, "CognitoDomainPrefix", new CfnParameterProps
        {
            Type = "String",
            Description = "This field denotes the part of the domain before .auth.[region].amazoncognito.com This must be unique within the AWS region.",
            MinLength = 1,
            AllowedPattern = "^[a-z0-9](?:[a-z0-9\\-]{0,61}[a-z0-9])?$",
            Default = "bedrock-" + new Random().Next(10000, 99999).ToString()
        });

        var LogRetentionDays = new CfnParameter(this, "LogRetentionDays", new CfnParameterProps
        {
            Type = "Number",
            Description = "Number of days to retain Cloudwatch Logs for each Lambda function",
            Default = 1
        });


        ///
        /// Lambda build/bundle options
        ///
        var buildOption = new BundlingOptions()
        {
            Image = Runtime.DOTNET_8.BundlingImage,
            User = "root",
            OutputType = BundlingOutput.ARCHIVED,
            Command = new string[]{
               "/bin/sh",
                "-c",
                " dotnet tool install -g Amazon.Lambda.Tools"+
                " && dotnet build"+
                " && dotnet lambda package --output-package /asset-output/function.zip"
            }
        };


        ///
        /// Resources
        ///

        // Frontend resources
        var frontendLambdaRole = new Role(this, "FrontendLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for FrontendLambda functions",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
        });

        var frontendLambda = new Function(this, "FrontendLambda", new FunctionProps
        {
            Description = "Hosts and serves Bedrock-DotNet demo frontend web contents",
            Code = Code.FromAsset("./solution/bedrock_frontend/", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Info"},
            },
            Handler = "bedrock_frontend::bedrock_frontend.LambdaEntryPoint::FunctionHandlerAsync",
            MemorySize = 256,
            Role = Role.FromRoleArn(this, "frontendLambdaRole", frontendLambdaRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(60),
        });

        var frontendLambdaLogs = new CfnLogGroup(this, "FrontendLambdaLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{frontendLambda.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var frontendApi = new HttpApi(this, "FrontendAPI", new HttpApiProps
        {
            ApiName = "Bedrock-DotNet-Frontend",
            Description = "This API endpoint serves the Bedrock-DotNet demo frontend pages",
            CreateDefaultStage = false,
            DefaultIntegration = new HttpLambdaIntegration("frontendLambda", frontendLambda, new HttpLambdaIntegrationProps
            {
                PayloadFormatVersion = PayloadFormatVersion.VERSION_1_0
            }),
        });

        frontendApi.AddStage("FrontendDefaultStage", new HttpStageOptions
        {
            StageName = "$default",
            AutoDeploy = true
        });

        frontendLambda.AddPermission("FrontendLambdaPermission", new Permission
        {
            Action = "lambda:InvokeFunction",
            Principal = new ServicePrincipal("apigateway.amazonaws.com"),
            SourceArn = $"arn:{Partition}:execute-api:{Region}:{Account}:{frontendApi.ApiId}/*",
        });

        // Authentication resources
        var cognitoUserPool = new UserPool(this, "CognitoUserPool", new UserPoolProps
        {
            SignInAliases = new SignInAliases()
            {
                PreferredUsername = true,
                Username = true,
                Email = true
            },
            AutoVerify = new AutoVerifiedAttrs()
            {
                Email = true
            },
            PasswordPolicy = new PasswordPolicy()
            {
                MinLength = 6,
                RequireLowercase = false,
                RequireDigits = true,
                RequireSymbols = false,
                RequireUppercase = false,
                TempPasswordValidity = Duration.Days(30)
            },
            SignInCaseSensitive = false
        });

        var cognitoDomain = new UserPoolDomain(this, "CognitoDomain", new UserPoolDomainProps
        {
            UserPool = cognitoUserPool,
            CognitoDomain = new CognitoDomainOptions()
            {
                DomainPrefix = CognitoDomainPrefix.ValueAsString
            }
        });

        var cognitoAppClient = new UserPoolClient(this, "CognitoAppClient", new UserPoolClientProps
        {
            UserPool = cognitoUserPool,
            UserPoolClientName = "app-client",
            ReadAttributes = new ClientAttributes().WithStandardAttributes(new StandardAttributesMask()
            {
                Fullname = true,
                Email = true,
                EmailVerified = true
            }),
            WriteAttributes = new ClientAttributes().WithStandardAttributes(new StandardAttributesMask()
            {
                Fullname = true,
                Email = true
            }),
            AuthFlows = new AuthFlow()
            {
                UserPassword = true

            },
            SupportedIdentityProviders = new[]
            {
                UserPoolClientIdentityProvider.COGNITO
            },
            OAuth = new OAuthSettings()
            {
                CallbackUrls = new[] { $"{frontendApi.ApiEndpoint}/signin-oidc" },
                LogoutUrls = new[] { frontendApi.ApiEndpoint },
                Flows = new OAuthFlows()
                {
                    AuthorizationCodeGrant = true,
                    ImplicitCodeGrant = true
                },
                Scopes = new[]
                {
                    OAuthScope.OPENID,
                    OAuthScope.PROFILE,
                    OAuthScope.EMAIL
                }
            },
            GenerateSecret = false
        });

        var cognitoTestUser = new CfnUserPoolUser(this, "CognitoTestUser", new CfnUserPoolUserProps
        {
            UserAttributes = new[]
            {
                new CfnUserPoolUser.AttributeTypeProperty
                {
                    Name = "name",
                    Value = "John Smith",
                },
            },
            Username = "user1",
            UserPoolId = cognitoUserPool.UserPoolId,
        });

        // Backend- API
        var backendApi = new WebSocketApi(this, "BackendAPI", new WebSocketApiProps
        {
            ApiName = "Bedrock-DotNet-Demo-Backend",
            Description = "This API endpoint serves the backend",
            RouteSelectionExpression = "$request.body.action"
        });

        var backendApiStage = new WebSocketStage(this, "BackendApiStage", new WebSocketStageProps
        {
            WebSocketApi = backendApi,
            AutoDeploy = true,
            StageName = "demo",
        });

        // Backend - Text chat resources
        var bedrockTextDdb = new TableV2(this, "BedrockTextDDB", new TablePropsV2
        {
            Billing = Billing.OnDemand(),
            TableClass = TableClass.STANDARD_INFREQUENT_ACCESS,
            TimeToLiveAttribute = "TTL",
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute()
            {
                Name = "SessionId",
                Type = AttributeType.NUMBER
            },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute()
            {
                Name = "TTL",
                Type = AttributeType.NUMBER
            }

        });

        var bedrockTextSqsQueue = new Queue(this, "BedrockTextSqsQueue", new QueueProps
        {
            QueueName = "Bedrock-DotNet-Text.fifo",
            Fifo = true,
            RetentionPeriod = Duration.Hours(1),
            VisibilityTimeout = Duration.Minutes(5)
        });

        var bedrockTextLambdaRole = new Role(this, "BedrockTextLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for BedrockTextLambda function",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "ExecuteApiPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "execute-api:ManageConnections" },
                                Resources = new[] { $"arn:{Partition}:execute-api:{Region}:{Account}:{backendApi.ApiId}/{backendApiStage.StageName}/POST/@connections/*" }
                            })
                        }
                    })
                },
                { "BedrockPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "bedrock:InvokeModel" },
                                Resources = new[] { "*" }
                            })
                        }
                    })
                },
                { "SqsPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "sqs:GetQueueAttributes", "sqs:DeleteMessage", "sqs:ReceiveMessage" },
                                Resources = new[] { bedrockTextSqsQueue.QueueArn }
                            })
                        }
                    })
                },
                { "DdbPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "dynamodb:Query", "dynamodb:PutItem" },
                                Resources = new[] { bedrockTextDdb.TableArn }
                            })
                        }
                    })
                }
            }
        });

        var bedrockTextLambda = new Function(this, "BedrockTextLambda", new FunctionProps
        {
            Description = "Bedrock-DotNet demo backend function to handle text based chat",
            Code = Code.FromAsset("./solution/bedrock_text/", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Information"},
                { "DDB_TABLE_NAME", bedrockTextDdb.TableName},
            },
            Handler = "bedrock_text::bedrock_text.Function::FunctionHandler",
            MemorySize = 256,
            Role = Role.FromRoleArn(this, "bedrockTextLambdaRole", bedrockTextLambdaRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(300),
        });

        var bedrockTextLambdaLogs = new CfnLogGroup(this, "BedrockTextLambdaLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{bedrockTextLambda.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var bedrockTextLambdaSourceMapping = new EventSourceMapping(this, "BedrockTextLambdaSourceMapping", new EventSourceMappingProps
        {
            BatchSize = 1,
            Enabled = true,
            EventSourceArn = bedrockTextSqsQueue.QueueArn,
            Target = bedrockTextLambda
        });

        // Backend - Knowledgebase resources
        var aossEncryptionPolicy = new CfnSecurityPolicy(this, "AossEncryptionPolicy", new CfnSecurityPolicyProps
        {
            Description = "Custom encryption policy created for Amazon Bedrock Knowledge Base service to allow a created IAM role to have permissions on Amazon Open Search collections and indexes.",
            Name = "enc-policy-for-bedrock-kb",
            Policy = "{\"AWSOwnedKey\":true,\"Rules\":[{\"ResourceType\":\"collection\",\"Resource\":[\"collection/aoss-collection-for-bedrock-kb\"]}]}",
            Type = "encryption",
        });

        var aossNetworkPolicy = new CfnSecurityPolicy(this, "AossNetworkPolicy", new CfnSecurityPolicyProps
        {
            Description = "Custom network policy created for Amazon Bedrock Knowledge Base service to allow a created IAM role to have permissions on Amazon Open Search collections and indexes.",
            Name = "net-policy-for-bedrock-kb",
            Policy = "[{\"AllowFromPublic\":true,\"Rules\":[{\"ResourceType\":\"dashboard\",\"Resource\":[\"collection/aoss-collection-for-bedrock-kb\"]},{\"ResourceType\":\"collection\",\"Resource\":[\"collection/aoss-collection-for-bedrock-kb\"]}]}]",
            Type = "network",
        });

        var aossCollection = new CfnCollection(this, "AossCollection", new CfnCollectionProps
        {
            Description = "Default collection created for Amazon Bedrock Knowledge base.",
            Name = "aoss-collection-for-bedrock-kb",
            Type = "VECTORSEARCH",
        });
        aossCollection.Node.AddDependency(aossEncryptionPolicy);

        var customResourceAossIndexLambdaRole = new Role(this, "CustomResourceAossIndexLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for CustomResourceAossIndexLambda function",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "AossApiPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "aoss:APIAccessAll" },
                                Resources = new[] { aossCollection.AttrArn }
                            })
                        }
                    })
                }
            }
        });

        var customResourceAossIndexLambda = new Function(this, "CustomResourceAossIndexLambda", new FunctionProps
        {
            Description = "Function for the Cloudformation custome resource to create Bedrock KnowledgeBase",
            Code = Code.FromAsset("./cdk/CustomResourceAossIndex/", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Debug"},
                { "AOSS_COLLECTION_ENDPOINT", aossCollection.AttrCollectionEndpoint},
            },
            Handler = "CustomResourceAossIndex::CustomResourceAossIndex.Function::FunctionHandler",
            MemorySize = 256,
            Role = Role.FromRoleArn(this, "customResourceAossIndexLambdaRole", customResourceAossIndexLambdaRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(300),
        });


        var customResourceAossIndexLambdaLogs = new CfnLogGroup(this, "CustomResourceAossIndexLambdaLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{customResourceAossIndexLambda.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var bedrockRagDataBucket = new Bucket(this, "BedrockRagDataBucket", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            RemovalPolicy = RemovalPolicy.RETAIN_ON_UPDATE_OR_DELETE
        });

        var bedrockExecutionRoleForKnowledgeBase = new Role(this, "BedrockExecutionRoleForKnowledgeBase", new RoleProps
        {
            AssumedBy = new ServicePrincipal("bedrock.amazonaws.com"),
            Description = "Bedrock Knowledge Base access",
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "FoundationModelPolicyForKnowledgeBase", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "bedrock:InvokeModel" },
                                Resources = new[] { $"arn:aws:bedrock:{Region}::foundation-model/amazon.titan-embed-text-v1" }
                            })
                        }
                    })
                },
                { "AossPolicyForKnowledgeBase", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "aoss:APIAccessAll" },
                                Resources = new[] { aossCollection.AttrArn }
                            })
                        }
                    })
                },
                { "S3PolicyForKnowledgeBase", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "s3:ListBucket", "s3:GetObject" },
                                Resources = new[] { bedrockRagDataBucket.BucketArn, $"{bedrockRagDataBucket.BucketArn}/*" }
                            })
                        }
                    })
                }
            },
            Path = "/service-role/"
        });

        var aossDataAccessPolicy = new CfnAccessPolicy(this, "AossDataAccessPolicy", new CfnAccessPolicyProps
        {
            Description = "Custom data access policy created for Amazon Bedrock Knowledge Base service to allow a created IAM role to have permissions on Amazon Open Search collections and indexes.",
            Name = "data-policy-for-bedrock-kb",
            Policy = "[{\"Rules\":[{\"Resource\":[\"collection/aoss-collection-for-bedrock-kb\"],\"Permission\":[\"aoss:DescribeCollectionItems\",\"aoss:CreateCollectionItems\",\"aoss:UpdateCollectionItems\"],\"ResourceType\":\"collection\"},{\"Resource\":[\"index/aoss-collection-for-bedrock-kb/*\"],\"Permission\":[\"aoss:UpdateIndex\",\"aoss:DescribeIndex\",\"aoss:ReadDocument\",\"aoss:WriteDocument\",\"aoss:CreateIndex\"],\"ResourceType\":\"index\"}],\"Principal\":[\"" + bedrockExecutionRoleForKnowledgeBase.RoleArn + "\", \"" + customResourceAossIndexLambdaRole.RoleArn + "\"],\"Description\":\"\"}]",
            Type = "data",
        });

        var aossDefaultIndex = new CfnCustomResource(this, "AossDefaultIndex", new CfnCustomResourceProps
        {
            ServiceToken = customResourceAossIndexLambda.FunctionArn,
        });
        aossDefaultIndex.Node.AddDependency(aossDataAccessPolicy);
        aossDefaultIndex.Node.AddDependency(customResourceAossIndexLambdaLogs);

        var bedrockKnowledgeBase = new CfnKnowledgeBase(this, "BedrockKnowledgeBase", new CfnKnowledgeBaseProps
        {
            Name = "bedrock-dotnet-demo-kb",
            KnowledgeBaseConfiguration = new KnowledgeBaseConfigurationProperty
            {
                Type = "VECTOR",
                VectorKnowledgeBaseConfiguration = new VectorKnowledgeBaseConfigurationProperty
                {
                    EmbeddingModelArn = $"arn:aws:bedrock:{Region}::foundation-model/amazon.titan-embed-text-v1"
                }
            },
            RoleArn = bedrockExecutionRoleForKnowledgeBase.RoleArn,
            StorageConfiguration = new StorageConfigurationProperty
            {
                Type = "OPENSEARCH_SERVERLESS",
                OpensearchServerlessConfiguration = new OpenSearchServerlessConfigurationProperty
                {
                    CollectionArn = aossCollection.AttrArn,
                    VectorIndexName = "bedrock-dotnet-demo-kb-default-index",
                    FieldMapping = new OpenSearchServerlessFieldMappingProperty
                    {
                        MetadataField = "AMAZON_BEDROCK_METADATA",
                        TextField = "AMAZON_BEDROCK_TEXT_CHUNK",
                        VectorField = "bedrock-dotnet-demo-kb-default-vector"
                    }
                }
            }
        });
        bedrockKnowledgeBase.Node.AddDependency(aossDefaultIndex);

        var bedrockKnowledgeBaseDataSource = new CfnDataSource(this, "bedrockKnowledgeBaseDataSource", new CfnDataSourceProps
        {
            Name = "bedrock-dotnet-demo-kb-data-source",
            KnowledgeBaseId = bedrockKnowledgeBase.Ref,
            DataSourceConfiguration = new DataSourceConfigurationProperty
            {
                Type = "S3",
                S3Configuration = new S3DataSourceConfigurationProperty
                {
                    BucketArn = bedrockRagDataBucket.BucketArn
                }
            }
        });

        var customResourceExampleDataLambdaRole = new Role(this, "CustomResourceExampleDataLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for CustomResourceExampleDataLambda function",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "BedrockApiPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "bedrock:StartIngestionJob", "bedrock:GetIngestionJob" },
                                Resources = new[] { bedrockKnowledgeBase.AttrKnowledgeBaseArn }
                            })
                        }
                    })
                },
                { "S3Permission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "s3:PutObject", "s3:ListBucket" },
                                Resources = new[] { $"{bedrockRagDataBucket.BucketArn}/*", bedrockRagDataBucket.BucketArn, }
                            })
                        }
                    })
                }
            }
        });

        var customResourceExampleDataLambda = new Function(this, "CustomResourceExampleDataLambda", new FunctionProps
        {
            Description = "Function for the Cloudformation custome resource to create Bedrock KnowledgeBase",
            Code = Code.FromAsset("./cdk/CustomResourceExampleData/", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Debug"},
                { "DATA_BUCKET", bedrockRagDataBucket.BucketName},
                { "KNOWLEDGEBASE_ID", bedrockKnowledgeBase.AttrKnowledgeBaseId},
                { "DATASOURCE_ID", bedrockKnowledgeBaseDataSource.AttrDataSourceId},
            },
            Handler = "CustomResourceExampleData::CustomResourceExampleData.Function::FunctionHandler",
            MemorySize = 256,
            Role = Role.FromRoleArn(this, "customResourceExampleDataLambdaRole", customResourceExampleDataLambdaRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(300),
        });

        var customResourceExampleDataLambdaLogs = new CfnLogGroup(this, "CustomResourceExampleDataLambdaLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{customResourceExampleDataLambda.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var ragExampleData = new CfnCustomResource(this, "RagExampleData", new CfnCustomResourceProps
        {
            ServiceToken = customResourceExampleDataLambda.FunctionArn,
        });
        ragExampleData.Node.AddDependency(customResourceExampleDataLambdaLogs);

        // Backend - RAG Q/A resources
        var bedrockRagSqsQueue = new Queue(this, "BedrockRagSqsQueue", new QueueProps
        {
            QueueName = "Bedrock-DotNet-RAG.fifo",
            Fifo = true,
            RetentionPeriod = Duration.Hours(1),
            VisibilityTimeout = Duration.Minutes(5)
        });

        var bedrockRagLambdaRole = new Role(this, "BedrockRagLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for BedrockRagLambda function",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "ExecuteApiPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "execute-api:ManageConnections" },
                                Resources = new[] { $"arn:{Partition}:execute-api:{Region}:{Account}:{backendApi.ApiId}/{backendApiStage.StageName}/POST/@connections/*" }
                            })
                        }
                    })
                },
                { "BedrockPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "bedrock:InvokeModel", "bedrock:RetrieveAndGenerate", "bedrock:Retrieve" },
                                Resources = new[] { "*" }
                            })
                        }
                    })
                },
                { "SqsPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "sqs:GetQueueAttributes", "sqs:DeleteMessage", "sqs:ReceiveMessage" },
                                Resources = new[] { bedrockRagSqsQueue.QueueArn }
                            })
                        }
                    })
                }
            }
        });

        var bedrockRagLambda = new Function(this, "BedrockRagLambda", new FunctionProps
        {
            Description = "Bedrock-DotNet demo backend function to handle RAG based chat/Q&A",
            Code = Code.FromAsset("./solution/bedrock_rag/", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Information"},
                { "KNOWLEDGE_BASE_ID", bedrockKnowledgeBase.AttrKnowledgeBaseId},
            },
            Handler = "bedrock_rag::bedrock_rag.Function::FunctionHandler",
            MemorySize = 256,
            Role = Role.FromRoleArn(this, "bedrockRagLambdaRole", bedrockRagLambdaRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(300),
        });

        var bedrockRagLambdaLogs = new CfnLogGroup(this, "BedrockRagLambdaLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{bedrockRagLambda.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var bedrockRagLambdaSourceMapping = new EventSourceMapping(this, "BedrockRagLambdaSourceMapping", new EventSourceMappingProps
        {
            BatchSize = 1,
            Enabled = true,
            EventSourceArn = bedrockRagSqsQueue.QueueArn,
            Target = bedrockRagLambda
        });

        // Backend - Image generator resources
        var bedrockImageS3Bucket = new Bucket(this, "BedrockImageS3Bucket", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            LifecycleRules = new LifecycleRule[]
            {
                new LifecycleRule
                {
                    Id = "ClearOldImages",
                    Expiration = Duration.Days(1),
                    Prefix = "images/",
                    Enabled = true
                }
            },
            RemovalPolicy = RemovalPolicy.RETAIN_ON_UPDATE_OR_DELETE
        });

        var bedrockImageCfDistributionOac = new CfnOriginAccessControl(this, "BedrockImageCfDistributionOAC", new CfnOriginAccessControlProps
        {
            OriginAccessControlConfig = new CfnOriginAccessControl.OriginAccessControlConfigProperty
            {
                Name = bedrockImageS3Bucket.BucketName,
                OriginAccessControlOriginType = "s3",
                SigningBehavior = "always",
                SigningProtocol = "sigv4",
            },
        });

        var bedrockImageCfDistribution = new CfnDistribution(this, "BedrockImageCfDistribution", new CfnDistributionProps
        {
            DistributionConfig = new CfnDistribution.DistributionConfigProperty
            {
                Origins = new[]
                {
                    new CfnDistribution.OriginProperty
                    {
                        Id = "S3Origin",
                        DomainName = bedrockImageS3Bucket.BucketRegionalDomainName,
                        S3OriginConfig = new CfnDistribution.S3OriginConfigProperty
                        {
                            OriginAccessIdentity = "",
                        },
                        OriginAccessControlId = bedrockImageCfDistributionOac.AttrId,
                    },
                },
                Enabled = true,
                DefaultCacheBehavior = new CfnDistribution.DefaultCacheBehaviorProperty
                {
                    CachePolicyId = "658327ea-f89d-4fab-a63d-7e88639e58f6",
                    Compress = true,
                    TargetOriginId = "S3Origin",
                    ViewerProtocolPolicy = "redirect-to-https",
                },
                HttpVersion = "http2",
                PriceClass = "PriceClass_100",
            },
        });

        bedrockImageS3Bucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = new[] { "s3:*" },
            Resources = new[] { $"{bedrockImageS3Bucket.BucketArn}/*", bedrockImageS3Bucket.BucketArn },
            Principals = new[] { new AnyPrincipal() },
            Conditions = new Dictionary<string, object>
                {
                    { "Bool", new Dictionary<string, object>
                        {
                            { "aws:SecureTransport", false}
                        }
                    }
                }
        }
        ));

        bedrockImageS3Bucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { $"{bedrockImageS3Bucket.BucketArn}/*" },
            Principals = new[] { new ServicePrincipal("cloudfront.amazonaws.com") },
            Conditions = new Dictionary<string, object>
                    {
                        { "StringEquals", new Dictionary<string, object>
                            {
                                { "AWS:SourceArn", $"arn:aws:cloudfront::{Account}:distribution/{bedrockImageCfDistribution.Ref}"},
                            }
                        },
                    }
        }
        ));

        var bedrockImageSqsQueue = new Queue(this, "BedrockImageSqsQueue", new QueueProps
        {
            QueueName = "Bedrock-DotNet-Image.fifo",
            Fifo = true,
            RetentionPeriod = Duration.Hours(1),
            VisibilityTimeout = Duration.Minutes(5)
        });

        var bedrockImageLambdaRole = new Role(this, "BedrockImageLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for BedrockImageLambda function",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "ExecuteApiPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "execute-api:ManageConnections" },
                                Resources = new[] { $"arn:{Partition}:execute-api:{Region}:{Account}:{backendApi.ApiId}/{backendApiStage.StageName}/POST/@connections/*" }
                            })
                        }
                    })
                },
                { "S3Permission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "s3:PutObject", "s3:ListBucket" },
                                Resources = new[] { $"{bedrockImageS3Bucket.BucketArn}/*", bedrockImageS3Bucket.BucketArn }
                            })
                        }
                    })
                },
                { "BedrockPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "bedrock:InvokeModel" },
                                Resources = new[] { "*" }
                            })
                        }
                    })
                },
                { "SqsPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "sqs:GetQueueAttributes", "sqs:DeleteMessage", "sqs:ReceiveMessage" },
                                Resources = new[] { bedrockImageSqsQueue.QueueArn }
                            })
                        }
                    })
                }
            }
        });

        var bedrockImageLambda = new Function(this, "BedrockImageLambda", new FunctionProps
        {
            Description = "Bedrock-DotNet demo backend function to handle image generation",
            Code = Code.FromAsset("./solution/bedrock_image/", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Information"},
                { "S3_BUCKET_NAME", bedrockImageS3Bucket.BucketName},
                { "CF_DISTRIBUTION_DOMAIN", bedrockImageCfDistribution.AttrDomainName},
            },
            Handler = "bedrock_image::bedrock_image.Function::FunctionHandler",
            MemorySize = 256,
            Role = Role.FromRoleArn(this, "bedrockImageLambdaRole", bedrockImageLambdaRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(300),
        });

        var bedrockImageLambdaLogs = new CfnLogGroup(this, "BedrockImageLambdaLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{bedrockImageLambda.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var bedrockImageLambdaSourceMapping = new EventSourceMapping(this, "BedrockImageLambdaSourceMapping", new EventSourceMappingProps
        {
            BatchSize = 1,
            Enabled = true,
            EventSourceArn = bedrockImageSqsQueue.QueueArn,
            Target = bedrockImageLambda
        });

        // Backend API - Authorizer
        var backendLambdaAuthorizerRole = new Role(this, "BackendLambdaAuthorizerRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Lambda execution role for BackendLambdaAuthorizer functions",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
        });

        var backendLambdaAuthorizer = new Function(this, "BackendLambdaAuthorizer", new FunctionProps
        {
            Description = "Lambda Authorizer for Bedrock-DotNet demo backend API Gateway",
            Code = Code.FromAsset("./solution/bedrock_authorizer", new Amazon.CDK.AWS.S3.Assets.AssetOptions
            {
                Bundling = buildOption
            }),
            Environment = new Dictionary<string, string>
            {
                { "AWS_LAMBDA_HANDLER_LOG_LEVEL", "Info"},
                { "COGNITO_USER_POOL_ID", cognitoUserPool.UserPoolId},
                { "COGNITO_APP_CLIENT_ID", cognitoAppClient.UserPoolClientId}
            },
            Handler = "bedrock_authorizer::bedrock_authorizer.Function::FunctionHandler",
            MemorySize = 128,
            Role = Role.FromRoleArn(this, "backendLambdaAuthorizerRole", backendLambdaAuthorizerRole.RoleArn),
            Runtime = Runtime.DOTNET_8,
            Timeout = Duration.Seconds(60),
        });

        var backendLambdaAuthorizerLogs = new CfnLogGroup(this, "BackendLambdaAuthorizerLogs", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/lambda/{backendLambdaAuthorizer.FunctionName}",
            RetentionInDays = LogRetentionDays.ValueAsNumber,
        });

        var backendApiAuthorizer = new CfnAuthorizer(this, "BackendApiAuthorizer", new CfnAuthorizerProps
        {
            Name = "Bedrock-DotNet-Demo-Backend-Authorizer",
            ApiId = backendApi.ApiId,
            AuthorizerType = "REQUEST",
            AuthorizerUri = $"arn:{Partition}:apigateway:{Region}:lambda:path/2015-03-31/functions/{backendLambdaAuthorizer.FunctionArn}/invocations",
            IdentitySource = new[]
            {
                "route.request.querystring.token",
            },
        });

        backendLambdaAuthorizer.AddPermission("BackendLambdaAuthorizerPermission", new Permission
        {
            Action = "lambda:InvokeFunction",
            Principal = new ServicePrincipal("apigateway.amazonaws.com"),
            SourceArn = $"arn:{Partition}:execute-api:{Region}:{Account}:{backendApi.ApiId}/authorizers/{backendApiAuthorizer.Ref}",
        });

        // Backend API - Integrations & Routes
        var backendApiIntegrationRole = new Role(this, "BackendApiIntegrationRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("apigateway.amazonaws.com"),
            Description = "API GW role to trigger SQS",
            ManagedPolicies = new[] {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonAPIGatewayPushToCloudWatchLogs")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>()
            {
                { "TriggerSqsPermission", new PolicyDocument(new PolicyDocumentProps
                    {
                        Statements = new[]
                        {
                            new PolicyStatement(new PolicyStatementProps
                            {
                                Effect = Effect.ALLOW,
                                Actions = new[] { "sqs:SendMessage" },
                                Resources = new[] {
                                    bedrockTextSqsQueue.QueueArn,
                                    bedrockRagSqsQueue.QueueArn,
                                    bedrockImageSqsQueue.QueueArn,
                                }
                            })
                        }
                    })
                }
            }
        });

        backendApi.AddRoute("sendprompt_image", new WebSocketRouteOptions
        {
            Integration = new WebSocketAwsIntegration("BackendApiIntegrationImage", new WebSocketAwsIntegrationProps
            {
                CredentialsRole = backendApiIntegrationRole,
                IntegrationMethod = "POST",
                IntegrationUri = $"arn:{Partition}:apigateway:{Region}:sqs:path/{Account}/{bedrockImageSqsQueue.QueueName}",
                PassthroughBehavior = PassthroughBehavior.NEVER,
                RequestParameters = new Dictionary<string, string>
                {
                    { "integration.request.header.Content-Type", "'application/x-www-form-urlencoded'"},
                },
                RequestTemplates = new Dictionary<string, string>
                {
                    { "application/json", "Action=SendMessage&MessageGroupId=$input.path('$.MessageGroupId')&MessageDeduplicationId=$context.requestId&MessageAttribute.1.Name=connectionId&MessageAttribute.1.Value.StringValue=$context.connectionId&MessageAttribute.1.Value.DataType=String&MessageAttribute.2.Name=requestId&MessageAttribute.2.Value.StringValue=$context.requestId&MessageAttribute.2.Value.DataType=String&MessageAttribute.3.Name=domainName&MessageAttribute.3.Value.StringValue=$context.domainName&MessageAttribute.3.Value.DataType=String&MessageAttribute.4.Name=stage&MessageAttribute.4.Value.StringValue=$context.stage&MessageAttribute.4.Value.DataType=String&MessageBody=$input.json('$')"},
                },
            })
        });

        backendApi.AddRoute("sendprompt_rag", new WebSocketRouteOptions
        {
            Integration = new WebSocketAwsIntegration("BackendApiIntegrationRag", new WebSocketAwsIntegrationProps
            {
                CredentialsRole = backendApiIntegrationRole,
                IntegrationMethod = "POST",
                IntegrationUri = $"arn:{Partition}:apigateway:{Region}:sqs:path/{Account}/{bedrockRagSqsQueue.QueueName}",
                PassthroughBehavior = PassthroughBehavior.NEVER,
                RequestParameters = new Dictionary<string, string>
            {
                { "integration.request.header.Content-Type", "'application/x-www-form-urlencoded'"},
            },
                RequestTemplates = new Dictionary<string, string>
            {
                { "application/json", "Action=SendMessage&MessageGroupId=$input.path('$.MessageGroupId')&MessageDeduplicationId=$context.requestId&MessageAttribute.1.Name=connectionId&MessageAttribute.1.Value.StringValue=$context.connectionId&MessageAttribute.1.Value.DataType=String&MessageAttribute.2.Name=requestId&MessageAttribute.2.Value.StringValue=$context.requestId&MessageAttribute.2.Value.DataType=String&MessageAttribute.3.Name=domainName&MessageAttribute.3.Value.StringValue=$context.domainName&MessageAttribute.3.Value.DataType=String&MessageAttribute.4.Name=stage&MessageAttribute.4.Value.StringValue=$context.stage&MessageAttribute.4.Value.DataType=String&MessageBody=$input.json('$')"},
            },
            })
        });

        backendApi.AddRoute("sendprompt_text", new WebSocketRouteOptions
        {
            Integration = new WebSocketAwsIntegration("BackendApiIntegrationText", new WebSocketAwsIntegrationProps
            {
                CredentialsRole = backendApiIntegrationRole,
                IntegrationMethod = "POST",
                IntegrationUri = $"arn:{Partition}:apigateway:{Region}:sqs:path/{Account}/{bedrockTextSqsQueue.QueueName}",
                PassthroughBehavior = PassthroughBehavior.NEVER,
                RequestParameters = new Dictionary<string, string>
            {
                { "integration.request.header.Content-Type", "'application/x-www-form-urlencoded'"},
            },
                RequestTemplates = new Dictionary<string, string>
            {
                { "application/json", "Action=SendMessage&MessageGroupId=$input.path('$.MessageGroupId')&MessageDeduplicationId=$context.requestId&MessageAttribute.1.Name=connectionId&MessageAttribute.1.Value.StringValue=$context.connectionId&MessageAttribute.1.Value.DataType=String&MessageAttribute.2.Name=requestId&MessageAttribute.2.Value.StringValue=$context.requestId&MessageAttribute.2.Value.DataType=String&MessageAttribute.3.Name=domainName&MessageAttribute.3.Value.StringValue=$context.domainName&MessageAttribute.3.Value.DataType=String&MessageAttribute.4.Name=stage&MessageAttribute.4.Value.StringValue=$context.stage&MessageAttribute.4.Value.DataType=String&MessageBody=$input.json('$')"},
            },
            })
        });

        var backendApiConnect = new CfnRoute(this, "BackendApiConnect", new CfnRouteProps
        {
            ApiId = backendApi.ApiId,
            RouteKey = "$connect",
            AuthorizationType = "CUSTOM",
            AuthorizerId = backendApiAuthorizer.Ref
        });

        // Frontend Lambda - Add ENV variables
        frontendLambda.AddEnvironment("BACKEND_ENDPOINT", backendApi.ApiEndpoint);
        frontendLambda.AddEnvironment("BACKEND_STAGE", backendApiStage.StageName);
        frontendLambda.AddEnvironment("COGNITO_BASE_URL", frontendApi.ApiEndpoint);
        frontendLambda.AddEnvironment("COGNITO_CLIENT_ID", cognitoAppClient.UserPoolClientId);
        frontendLambda.AddEnvironment("COGNITO_LOGOUT_URL", $"https://{cognitoDomain.DomainName}.auth.{Region}.amazoncognito.com/logout");
        frontendLambda.AddEnvironment("COGNITO_METADATA_ADDRESS", $"https://cognito-idp.{Region}.amazonaws.com/{cognitoUserPool.UserPoolId}/.well-known/openid-configuration");

        ///
        /// Outputs
        ///
        new CfnOutput(this, "WebURL", new CfnOutputProps
        {
            Value = frontendApi.ApiEndpoint,
            Description = "Web endpoint for the Bedrock-DotNet demo"
        });

        new CfnOutput(this, "CognitoUserPoolId", new CfnOutputProps
        {
            Value = cognitoUserPool.UserPoolId,
            Description = "Cognito User Pool ID"
        });
    }
}
