using Microsoft.Playwright;

var outDir = @"C:\Users\AHMEDSAEED\OneDrive - AMEK\Desktop\shiftflow_asset\srs_work\screenshots";
Directory.CreateDirectory(outDir);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    ViewportSize = new ViewportSize { Width = 1400, Height = 900 }
});
var page = await context.NewPageAsync();

async Task EnsureEnglish()
{
    var toggle = await page.QuerySelectorAsync("form[action*='/Language/SetLanguage'] button");
    if (toggle != null)
    {
        var text = (await toggle.InnerTextAsync()).Trim();
        if (text.Contains("English"))
        {
            await toggle.EvalOnSelectorAsync("xpath=ancestor::form", "f => f.submit()");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }
}

async Task Login(string email, string password)
{
    await page.GotoAsync("http://localhost:5080/Account/Login");
    await EnsureEnglish();
    await page.FillAsync("#Email", email);
    await page.FillAsync("#Password", password);
    await page.ClickAsync("form[action*='/Account/Login'] button[type=submit]");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}

async Task Shoot(string name)
{
    await page.WaitForTimeoutAsync(400);
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(outDir, name), FullPage = true });
    Console.WriteLine($"Saved {name}");
}

async Task Logout()
{
    await page.GotoAsync("http://localhost:5080/");
    await page.EvaluateAsync(@"() => {
        const f = document.querySelector(""form[action*='/Account/Logout']"");
        if (f) f.submit();
    }");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}

// 1. Login page (screenshot before logging in)
await page.GotoAsync("http://localhost:5080/Account/Login");
await EnsureEnglish();
await Shoot("01_login.png");

// Now actually log in as admin
await Login("admin@shiftflow.com", "Admin@123456");

// 2. ShiftMaker calendar/schedule view
await page.GotoAsync("http://localhost:5080/ShiftMaker");
await Shoot("02_shiftmaker.png");

// 3. ShiftOps Today (roster/tasks)
await page.GotoAsync("http://localhost:5080/ShiftOps/Today");
await Shoot("03_shiftops_today.png");

// 4. Change Requests
await page.GotoAsync("http://localhost:5080/ChangeRequests");
await Shoot("04_change_requests.png");

// 5. Executive Dashboard
await page.GotoAsync("http://localhost:5080/");
await Shoot("05_dashboard.png");

// 6. AI Assistant
await page.GotoAsync("http://localhost:5080/AiAssistant");
await Shoot("06_ai_assistant.png");

// 7. Assets Index
await page.GotoAsync("http://localhost:5080/Assets");
await Shoot("07_assets_index.png");

async Task<string?> FirstRowUrl()
{
    return await page.EvaluateAsync<string?>(@"() => {
        const tr = document.querySelector('tr.cursor-pointer[onclick]');
        if (!tr) return null;
        const m = tr.getAttribute('onclick').match(/'([^']+)'/);
        return m ? m[1] : null;
    }");
}

// 8. WorkOrders Details (stage pipeline) - find first work order row
await page.GotoAsync("http://localhost:5080/WorkOrders");
await page.WaitForTimeoutAsync(500);
var woHref = await FirstRowUrl();
Console.WriteLine($"WorkOrders first row url: {woHref}");
if (woHref != null)
{
    await page.GotoAsync(woHref.StartsWith("http") ? woHref : "http://localhost:5080" + woHref);
    await Shoot("08_workorder_details.png");
}
else
{
    await Shoot("08_workorders_index.png");
}

// 9. Contracts Details for PM-TEST-001 (or first PM contract)
await page.GotoAsync("http://localhost:5080/Contracts");
await page.WaitForTimeoutAsync(500);
var pmHref = await page.EvaluateAsync<string?>(@"() => {
    const rows = Array.from(document.querySelectorAll('tr.cursor-pointer[onclick]'));
    const target = rows.find(r => r.textContent.includes('PM-TEST-001')) || rows[0];
    if (!target) return null;
    const m = target.getAttribute('onclick').match(/'([^']+)'/);
    return m ? m[1] : null;
}");
Console.WriteLine($"Contracts row url: {pmHref}");
if (pmHref != null)
{
    await page.GotoAsync(pmHref.StartsWith("http") ? pmHref : "http://localhost:5080" + pmHref);
    await Shoot("09_contract_pm_schedule.png");
}

// Logout admin, login as vendor for VendorPortal screenshot
await Logout();
Console.WriteLine($"After logout, url: {page.Url}");
await Login("vendor@gulfhvac.kw", "Vendor@123456");

// 10. VendorPortal Index
await page.GotoAsync("http://localhost:5080/VendorPortal");
await Shoot("10_vendor_portal.png");

Console.WriteLine("All screenshots captured.");
