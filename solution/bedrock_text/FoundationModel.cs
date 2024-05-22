// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace bedrock_text;

class FoundationModel
{
    public required string ModelId { get; set; }

    public string GetUserRoleName()
    {
        switch (ModelId)
        {
            case "amazon.titan-text-express-v1":
                return "User: ";
            case "anthropic.claude-v2:1":
                return "\n\nHuman: ";
            case "ai21.j2-mid-v1":
                return "\n";
            case "meta.llama2-13b-chat-v1":
                return "\n";
            default:
                return "";
        }
    }

    public string GetBotRoleName()
    {
        switch (ModelId)
        {
            case "amazon.titan-text-express-v1":
                return "Bot: ";
            case "anthropic.claude-v2:1":
                return "\n\nAssistant: ";
            case "ai21.j2-mid-v1":
                return "\n";
            case "meta.llama2-13b-chat-v1":
                return "";
            default:
                return "";
        }
    }

    public string GeneratePayload(string message, float _temperature, float top_p, int max_token)
    {
        switch (ModelId)
        {
            case "amazon.titan-text-express-v1":
                return new JsonObject()
                    {
                        { "inputText", message },
                        { "textGenerationConfig", new JsonObject()
                            {
                                { "maxTokenCount", max_token },
                                { "temperature", _temperature },
                                { "topP", top_p }
                            }
                        }
                    }.ToJsonString();
            case "anthropic.claude-v2:1":
                return new JsonObject()
                    {
                        { "prompt", message },
                        { "temperature", _temperature },
                        { "top_p", top_p },
                        { "max_tokens_to_sample", max_token }
                    }.ToJsonString();
            case "ai21.j2-mid-v1":
                return new JsonObject()
                    {
                        { "prompt", message },
                        { "temperature", _temperature },
                        { "topP", top_p },
                        { "maxTokens", max_token }
                    }.ToJsonString();
            case "meta.llama2-13b-chat-v1":
                return new JsonObject()
                    {
                        { "prompt", message },
                        { "temperature", _temperature },
                        { "top_p", top_p },
                        { "max_gen_len", max_token }
                    }.ToJsonString();
            default:
                return "";
        }
    }

    public string RetrieveAiResponse(MemoryStream body)
    {
        switch (ModelId)
        {
            // Amazon Titan Text G1
            case "amazon.titan-text-express-v1":
                return JsonNode.ParseAsync(body)
                    .Result?["results"]?
                    .AsArray()[0]?["outputText"]?.GetValue<string?>() ?? "";

            // Anthropic Claude 2
            case "anthropic.claude-v2:1":
                return JsonNode.ParseAsync(body)
                    .Result?["completion"]?
                    .GetValue<string>() ?? "";

            // AI21 Labs Jurassic-2
            case "ai21.j2-mid-v1":
                return JsonNode.ParseAsync(body)
                    .Result?["completions"]?
                    .AsArray()[0]?["data"]?
                    .AsObject()["text"]?.GetValue<string>() ?? "";

            // Meta Llama 2 Chat
            case "meta.llama2-13b-chat-v1":
                return JsonNode.ParseAsync(body)
                    .Result?["generation"]?
                    .GetValue<string>() ?? "";

            // Default - empty result
            default:
                return "";
        }
    }
}
