using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

// ---------- Configuration (CLI > env var > default) ----------
string? Arg(string name)
{
    var a = Environment.GetCommandLineArgs().ToList();
    int i = a.IndexOf(name);
    return i >= 0 && i + 1 < a.Count ? a[i + 1] : null;
}
bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

var url       = Arg("--url") ?? Environment.GetEnvironmentVariable("TARGET_URL") ?? "https://app.cloudqa.io/home/AutomationPracticeForm";
var firstName = Arg("--first-name") ?? "Priya";
var gender    = Arg("--gender") ?? "Female";
var state     = Arg("--state"); // null => first real option

var opts = new ChromeOptions();
if (Flag("--headless")) opts.AddArgument("--headless=new");
opts.AddArgument("--window-size=1400,1000");

using var driver = new ChromeDriver(opts);
var finder = new SmartFinder(driver);
var results = new List<(string Name, bool Ok, string Detail)>();

void Run(string name, Action a)
{
    try { a(); results.Add((name, true, "ok")); }
    catch (Exception e) { results.Add((name, false, e.Message)); }
}

driver.Navigate().GoToUrl(url);
finder.WaitForPage();

Run("First Name (text box)", () =>
{
    var c = finder.Best("text",
        pos: new[] { "first name", "fname", "given name", "forename" },
        neg: new[] { "last", "surname", "lname", "middle", "full", "user", "company" });
    c.El.Clear();
    c.El.SendKeys(firstName);
    var actual = c.El.GetAttribute("value");
    if (actual != firstName) throw new Exception($"Expected '{firstName}' but field has '{actual}'");
});

Run("Gender (radio)", () =>
{
    var c = finder.BestRadio(gender);
    try { c.El.Click(); }
    catch
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            "var l=arguments[0].labels&&arguments[0].labels[0];(l||arguments[0]).click();", c.El);
    }
    if (!c.El.Selected) throw new Exception($"Radio '{gender}' not selected after click");
});

Run("State (dropdown)", () =>
{
    var c = finder.Best("select",
        pos: new[] { "state", "province", "region" },
        neg: new[] { "country", "city", "zip", "postal" });
    var sel = new SelectElement(c.El);
    var options = sel.Options.Select(o => o.Text.Trim()).ToList();
    string pick;
    if (state != null)
    {
        pick = options.FirstOrDefault(o => SmartFinder.Norm(o) == SmartFinder.Norm(state))
            ?? options.FirstOrDefault(o => SmartFinder.Norm(o).Contains(SmartFinder.Norm(state)))
            ?? throw new Exception($"Option '{state}' not in dropdown");
    }
    else
    {
        pick = options.FirstOrDefault(o => o.Length > 0 && !Regex.IsMatch(o, @"select|choose|^-+", RegexOptions.IgnoreCase))
            ?? throw new Exception("No selectable options");
    }
    sel.SelectByText(pick);
    var got = sel.SelectedOption.Text.Trim();
    if (got != pick) throw new Exception($"Expected '{pick}', got '{got}'");
});

Console.WriteLine($"\nURL: {url}");
foreach (var r in results) Console.WriteLine($"[{(r.Ok ? "PASS" : "FAIL")}] {r.Name} - {r.Detail}");
bool all = results.All(r => r.Ok);
Console.WriteLine(all ? "\nOVERALL: PASS" : "\nOVERALL: FAIL");
driver.Quit();
return all ? 0 : 1;
