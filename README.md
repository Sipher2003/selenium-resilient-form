# Resilient Form Automation (C# + Selenium)

Automates three fields on https://app.cloudqa.io/home/AutomationPracticeForm and keeps working when the page
changes:

- **First Name** (text box)
- **Gender** (radio button)
- **State** (dropdown)

The program finds each field by what it **means** (its label, nearby text, placeholder and so on), not by a fixed
XPath or CSS selector, and the page URL is configurable.

## Run

Requires the .NET 8 SDK and Google Chrome (Selenium downloads the matching driver automatically).

```bash
dotnet restore
dotnet run -- --url "https://app.cloudqa.io/home/AutomationPracticeForm"
```

Example output:

```
URL: https://app.cloudqa.io/home/AutomationPracticeForm
[PASS] First Name (text box) - ok
[PASS] Gender (radio) - ok
[PASS] State (dropdown) - ok

OVERALL: PASS
```

The exit code is `0` if every check passes and `1` otherwise.

### Options

| Option | Purpose | Default |
|---|---|---|
| `--url <url>` | page to test (http(s) URL or `file:///` path) | CloudQA practice form |
| `--first-name <text>` | value typed into First Name | `Priya` |
| `--gender <text>` | radio option to select (e.g. `Male`, `Female`) | `Female` |
| `--state <text>` | dropdown option to select | first real option |
| `--headless` | run without a visible browser | off |
| `--keep-open` | keep the browser open after the run (press Enter to close) | off |

```bash
dotnet run -- --url "https://app.cloudqa.io/home/AutomationPracticeForm" --first-name Rahul --gender Male --state Texas --keep-open
```

## How the program meets the requirements

### Requirement 3: keeps working when the page changes

Each field is located by scoring every candidate control on the page against what a human would read, so nothing
depends on position, ids, classes or DOM depth.

**1. Collect candidates and their clues.** One JavaScript call gathers every candidate control (visible text
inputs, visible dropdowns, and all radio buttons) together with these clues:

| Clue | Weight | Source |
|---|---|---|
| Label text | 10 | `<label for>`, wrapping `<label>`, `aria-labelledby` |
| `aria-label` | 9 | attribute |
| Nearby text | 8 | short text in the closest container that holds only this control |
| Placeholder | 7 | attribute |
| Preceding text | 6 | short text in the sibling just before the control |
| `name`, `id` | 4 | attributes |
| `title` | 3 | attribute |

**2. Score against synonyms.** Strong clues (a visible label) count more than weak ones (an `id`).

- First Name matches `first name`, `fname`, `given name`, `forename`.
- State matches `state`, `province`, `region`.
- Text is normalised before matching: camelCase and separators are split, so `first_name`, `firstName` and
  `First-Name` all become "first name". Short compact values such as `txtfirstname` also match.
- The highest score wins, and a field is only accepted if its score is above zero.

**3. Penalise look-alike fields.** Negative terms subtract double their weight, so similar fields lose.

- First Name rejects `last`, `surname`, `lname`, `middle`, `full`, `user`, `company`.
- State rejects `country`, `city`, `zip`, `postal`.

**4. Radio buttons match the option, not the position.**

- The option text ("Male", "Female") is read from the label, wrapping element or adjacent text, and compared with
  whole-word matching, so `male` never matches inside `female`.
- Radios get a bonus when their **group** context mentions `gender` or `sex`, which separates the gender question
  from any other radio group on the page.
- If a custom-styled radio can't be clicked directly, the program clicks its label instead.

**5. The dropdown option is matched by text and verified.** The option is chosen by normalised text (exact first,
then contains), set through script with `input` and `change` events fired, and read back to confirm.

**6. Late-rendering pages.** The program waits for `document.readyState` to be complete and polls for text fields
and dropdowns for up to 10 seconds.

How this covers each change the task lists:

| Change to the page | Why the program still works |
|---|---|
| Fields move to a different order | Position is never used; fields are chosen by score. |
| `id`, `name` or `class` change | These are only weak clues (`id`/`name` weigh 4); labels, nearby text and placeholders carry the score. |
| HTML structure changes slightly | Label text comes from several routes (`for`, wrapping label, `aria-labelledby`, nearby container, preceding sibling), and no DOM depth or path is assumed. |
| Other fields look similar | Negative terms penalise Last Name, Full Name, Country and City; the radio group bonus separates gender from other radio groups; whole-word matching separates Male from Female. |

### Requirement 4: no fixed selector, configurable URL

- **No fixed XPath or CSS selector for the target fields.** No locator contains an id, class, name, index or
  absolute path. The only queries are generic element types (all `input`, `select` and `input[type=radio]`
  elements), which are then ranked by the scoring above.
- **The page URL is configurable.** Priority is `--url`, then the `TARGET_URL` environment variable, then the
  CloudQA default.

```bash
# command line
dotnet run -- --url "https://example.com/modified-form.html"

# environment variable (PowerShell: $env:TARGET_URL="https://...")
TARGET_URL="file:///path/to/modified-form.html" dotnet run -- --headless
```

## Verification built in

Every action is checked after it runs, and the result is reported per field:

- First Name: the field's value is read back and must equal the typed text.
- Gender: the radio must report as selected after the click.
- State: the selected option's text is read back and must equal the chosen option.

If a field can't be found, the error lists the top candidates and their clues, which makes it easy to adjust the
synonyms.

## Testing against a modified page

1. Save the page (Ctrl+S) or edit it live with Chrome DevTools.
2. Make variants: reorder the fields, rename every `id`, `name` and `class`, wrap inputs in extra `div`s, add a
   "Last Name" input, and add a second Male/Female radio group.
3. Run against each variant and confirm all three checks pass:

```bash
dotnet run -- --url "file:///D:/path/to/variant.html" --keep-open
```

## Customising synonyms

Synonyms and look-alike exclusions are plain arrays in `Program.cs` (the `pos` and `neg` arguments of
`finder.Best(...)`). To support another wording or language, add the word to `pos`.

## Limitations

- Fields inside iframes or shadow DOM are not searched yet (loop over frames and re-run the candidate collection).
- Dropdowns built from `div`s instead of a native `<select>` are not supported (would need `role=combobox` and
  `role=option` handling).
- Labels in a language that isn't covered by the synonym lists need those words added.

## Project layout

```
ResilientForm.csproj   project file (net8.0, Selenium.WebDriver)
Program.cs             configuration, the three field checks, reporting and exit code
SmartFinder.cs         candidate collection, scoring and matching
```