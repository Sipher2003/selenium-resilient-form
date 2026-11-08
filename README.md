# Resilient Form Automation (C# + Selenium)

Fills **First Name** (text box), **Gender** (radio) and **State** (dropdown) on a form
without relying on fixed XPath/CSS selectors, so it keeps working if the page changes.

## How it finds fields
Every candidate control is scored on what it *means*, not where it is:

| Signal | Weight |
|---|---|
| `<label for>` / wrapping label | 10 |
| `aria-label` / `aria-labelledby` | 9 |
| Nearby container text | 8 |
| Placeholder | 7 |
| Preceding sibling text | 6 |
| `name`, `id` | 4 |
| `title` | 3 |

- camelCase / snake_case are normalised (`first_name`, `firstName` -> "first name").
- Look-alike fields (Last Name, Full Name, Country, City) get **negative** scores.
- Radio: matches the option text with whole-word matching ("male" never matches "female")
  and adds a bonus when the group context mentions *gender* / *sex*. If the input is hidden by
  custom CSS, the label is clicked instead.
- Dropdown: scores `<select>` by label/name ("state"), picks the option by normalised text.
- Each action is verified (value, checked state, selected option). Exit code 0 = pass, 1 = fail.

## Run
Requires .NET 8 SDK and Chrome.

```bash
dotnet restore
dotnet run -- --url "https://app.cloudqa.io/home/AutomationPracticeForm"
dotnet run -- --url "<modified page>" --first-name Priya --gender Male --state Texas --headless
# or
TARGET_URL="file:///path/to/modified.html" dotnet run -- --headless
```

URL precedence: `--url` > `TARGET_URL` env var > default.

## Testing robustness
Save the page as HTML and make variants: reorder fields, rename all ids/names/classes, add
extra wrapper divs, add a "Last Name" field, add another Male/Female radio group. Run against each.

## Known limits / extensions
iframes and shadow DOM (loop over frames and re-run `Gather`); custom `div` dropdowns
(`role=combobox` / `role=option`).

> If `SelectElement` or `WebDriverWait` don't resolve with your Selenium version, also add
> `dotnet add package Selenium.Support`.
