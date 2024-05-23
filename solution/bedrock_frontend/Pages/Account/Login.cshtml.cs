// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace bedrock_frontend.Pages.Account;

[Authorize]
public class LoginModel : PageModel
{
    public IActionResult OnGet([FromQuery] string? ReturnUrl)
    {
        if (ReturnUrl is null)
        {
            ReturnUrl = "/";
        }

        return Redirect(ReturnUrl);
    }
}
