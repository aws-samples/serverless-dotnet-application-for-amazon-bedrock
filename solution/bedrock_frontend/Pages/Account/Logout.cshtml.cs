// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace bedrock_frontend.Pages.Account;

public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnGet()
    {
        if (User.Identity is not null && User.Identity.IsAuthenticated)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

            return new EmptyResult();
        }
        else
        {
            return Redirect("/");
        }
    }
}
