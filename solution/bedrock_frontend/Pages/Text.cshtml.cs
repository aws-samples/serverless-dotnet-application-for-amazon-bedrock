// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace bedrock_frontend.Pages;

[Authorize]
public class TextModel : PageModel
{
    public void OnGet()
    {
        ViewData["backend_endpoint"] = Environment.GetEnvironmentVariable("BACKEND_ENDPOINT");
        ViewData["backend_stage"] = Environment.GetEnvironmentVariable("BACKEND_STAGE") != null ? "/" + Environment.GetEnvironmentVariable("BACKEND_STAGE") : "";
        ViewData["id_token"] = HttpContext.GetTokenAsync("id_token").Result ?? "";
        ViewData["ws_action"] = "sendprompt_text";
    }
}
