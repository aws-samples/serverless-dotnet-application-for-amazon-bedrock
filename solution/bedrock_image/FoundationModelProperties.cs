// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

namespace bedrock_image;
class FoundationModelProperties
{
    public string Text { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float CfgScale { get; set; }
    public string? Sampler { get; set; }
    public string? StylePreset { get; set; }
}