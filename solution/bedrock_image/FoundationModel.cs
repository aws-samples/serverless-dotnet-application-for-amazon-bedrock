// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Amazon.BedrockRuntime.Model;
using System.Text.Json.Nodes;
using Amazon.Util;

namespace bedrock_image;

class FoundationModel
{
    public required string ModelId { get; set; }

    private string GeneratePayload(FoundationModelProperties fmp)
    {
        var payload = new JsonObject();

        switch (ModelId)
        {
            case "amazon.titan-image-generator-v1":
                payload = new JsonObject()
                    {
                        { "taskType", "TEXT_IMAGE" },
                        { "textToImageParams", new JsonObject()
                            {
                                { "text", fmp.Text }
                            }
                        },
                        { "imageGenerationConfig", new JsonObject()
                            {
                                { "quality", "standard" },
                                { "cfgScale", fmp.CfgScale },
                                { "height", fmp.Height },
                                { "width", fmp.Width }
                            }
                        }
                    };
                break;

            case "stability.stable-diffusion-xl-v1":
                payload = new JsonObject()
                    {
                        { "text_prompts", new JsonArray(
                            new JsonObject()
                            {
                                { "text", fmp.Text }
                            }
                        )},
                        { "height", fmp.Height },
                        { "width", fmp.Width },
                        { "cfg_scale", fmp.CfgScale }
                    };

                if(fmp.Sampler != null)
                {
                    payload.Add("sampler", fmp.Sampler);
                }

                if (fmp.StylePreset != null)
                {
                    payload.Add("style_preset", fmp.StylePreset);
                }
                break;
        }

        return payload.ToJsonString();
    }

    public InvokeModelRequest ModelRequest(FoundationModelProperties fmp)
    {
        string payload = GeneratePayload(fmp);

        return new InvokeModelRequest()
        {
            ModelId = ModelId,
            Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload),
            ContentType = "application/json",
            Accept = "application/json"

        };
    }

    public string RetrieveAiResponse(MemoryStream body)
    {
        try
        {
            switch (ModelId)
            {
                case "amazon.titan-image-generator-v1":
                    return JsonNode.ParseAsync(body).Result?["images"]?.AsArray()[0]?.GetValue<string?>() ?? "ERROR";

                case "stability.stable-diffusion-xl-v1":
                    return JsonNode.ParseAsync(body).Result?["artifacts"]?.AsArray()[0]?["base64"]?.GetValue<string?>() ?? "ERROR";

                default:
                    return "ERROR: Invalid ModelId";
            }
        }
        catch(Exception e)
        {
            return "ERROR: Unable to parse response from the AI. " + e.Message;
        }
    }
}
