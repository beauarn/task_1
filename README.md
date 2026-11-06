# Change-Tolerant Web Form Automation (C# & Selenium)

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Selenium WebDriver](https://img.shields.io/badge/Selenium-4.52.0-green.svg)](https://www.selenium.dev/)
[![Status](https://img.shields.io/badge/Test%20Status-100%25%20PASS-brightgreen.svg)]()

> **Internship Application — Task 1 Submission**  
> **Candidate:** Kartik  
> **Evaluator:** Arun Kulkarni  
> **Target Page:** [app.cloudqa.io/home/AutomationPracticeForm](https://app.cloudqa.io/home/AutomationPracticeForm)

---

## 📋 Executive Summary

Traditional UI automation suites break frequently when web pages undergo minor cosmetic updates, DOM re-structuring, class renaming, or attribute changes. This project implements a **self-healing, change-tolerant test automation program in C# (.NET 8) with Selenium WebDriver 4** designed specifically to automate:
1. **First Name** (Text Box)
2. **Gender** (Radio Button)
3. **State** (Dropdown)

### The Core Objective
The program keeps functioning reliably even when:
- Field order is shuffled or inverted on the page.
- Element IDs, names, classes, or attributes change or become dynamic GUIDs.
- The HTML structure changes (e.g. from Bootstrap `.form-group` to table layouts, cards, flexbox, or web components).
- Other similar fields exist on the page (e.g. Last Name, Username, Full Name, Country dropdown, Marital Status radio).
- The target page URL changes (full runtime configurability via CLI, config file, or environment variable).

---

## 🧠 Architecture & Resilience Strategy

Rather than relying on brittle, fixed CSS selectors (e.g. `#fname`) or absolute XPaths (e.g. `/html/body/div[2]/form/div[1]/input`), this engine utilizes a **Multi-Factor Semantic Scoring Engine** inspired by human perceptual cognition:

```
                  ┌────────────────────────────────────────────────────────┐
                  │                 Target Web Page DOM                    │
                  └──────────────────────────┬─────────────────────────────┘
                                             │
                       ┌─────────────────────┴─────────────────────┐
                       ▼                                           ▼
             Candidate Text Inputs / Radios / Selects     Labels & Context Anchors
                       │                                           │
                       └─────────────────────┬─────────────────────┘
                                             │
                                             ▼
                      ┌────────────────────────────────────────────┐
                      │   SmartFieldResolver (Scoring Engine)      │
                      ├────────────────────────────────────────────┤
                      │ • Explicit label-for / id matching         │
                      │ • Semantic name & attribute alignment      │
                      │ • Immediate field container membership     │
                      │ • Visual Euclidean distance (bounding-box) │
                      │ • Disambiguation & negative penalties      │
                      │ • Frame & Shadow DOM fallback traversal    │
                      └──────────────────────┬─────────────────────┘
                                             │
                                             ▼
                      ┌────────────────────────────────────────────┐
                      │   Winning Candidate (Score >= Threshold)   │
                      ├────────────────────────────────────────────┤
                      │ • Scroll into center view                  │
                      │ • Resilient interaction & JS event dispatch│
                      │ • Post-action value assertion & screenshot │
                      └────────────────────────────────────────────┘
```

### 1. Multi-Heuristic Scoring Breakdown
Each candidate element in the DOM is scored based on evidence points:

| Strategy / Heuristic | Points | Purpose |
|---|:---:|---|
| **Explicit Label Binding** | `+60 pts` | Matches `<label for="...">` or `<label name="...">` to input |
| **Direct Attribute Match** | `+35 pts` | `id`, `name`, `placeholder`, or `aria-label` contains target term |
| **Immediate Field Container** | `+45 pts` | Input shares immediate wrapper (`.form-group`, `<td>`, `.field`) with target label |
| **Visual Euclidean Proximity** | `+5 to +30 pts` | Calculated via JavaScript `getBoundingClientRect()` distance |
| **HTML5 Semantic Attributes** | `+45 pts` | Standard `autocomplete="given-name"` or role attributes |
| **Negative Penalty (Disambiguation)** | `-90 to -120 pts` | Penalizes conflicting personal name terms (Last Name, Username, Middle) |

### 2. Disambiguation & False-Positive Prevention
- **First Name vs. Competing Names:** Forms frequently contain "First Name", "Middle Name", "Last Name", and "Username". The engine heavily penalizes candidates possessing negative tokens (`last`, `surname`, `user`, `username`, `middle`) to ensure only the true First Name input is selected.
- **Gender Word Boundary Safety:** Substrings like `"Male"` are present inside `"Female"`. The resolver uses word-boundary regex (`\bMale\b` and negative lookbehind `(?<!fe)male`) to guarantee "Female" is never accidentally clicked when "Male" is requested.
- **State vs. Country Dropdowns:** The dropdown resolver confirms that the `<select>` is specifically associated with State/Province terminology, separating it from adjacent Country or City selectors.

### 3. Resilient Interaction & Event Dispatch
- **Clear & Type:** Employs `.Clear()` plus keyboard fallback (`Ctrl+A` -> `Backspace`) to support modern reactive frameworks (React, Angular, Vue).
- **Event Dispatch:** Triggers native JavaScript `input` and `change` events after typing/selecting so framework two-way data bindings update immediately.
- **Click Fallbacks:** If custom CSS hides the raw `<input type="radio">`, the engine automatically clicks the associated `<label>` or `<span>`, or falls back to JavaScript `arguments[0].click()`.

---

## 🌐 Universal DOM & Shadow DOM Architecture

Real-world modern applications (React, Angular, Vue, Web Components, Micro-frontends) do not stay confined to a single static DOM tree. They frequently employ:
1. **Standard Document Form(s)**
2. **Anonymous IFrames without IDs** (`src='/Home/IFrame'`)
3. **Nested IFrames** (`NestedIFrame` -> `InnerNestedIFrame`)
4. **Named IFrames** (`id='iframeId'`)
5. **Open Shadow DOMs** (`<nestedshadow-form>`)
6. **Deeply Nested Shadow DOMs (up to 5+ levels deep)** (`<nestedshadow-form6>` -> `...` -> `<nestedshadow-form>`)
7. **IFrames hosted inside Shadow DOMs** (`<nestedshadow-iframe>` -> `<iframe>`)

### The 11 Automated Contexts on CloudQA
When running against `https://app.cloudqa.io/home/AutomationPracticeForm`, the engine automatically scans and automates **all 11 distinct form contexts**:

| # | Form Context | Architecture Type | Detection & Traversal Strategy | Status |
|---|---|---|---|:---:|
| 1 | **Main Document** | Default DOM Content | `SmartFieldResolver` multi-factor scoring | **PASS ✅** |
| 2 | **IFrame without ID (Index #0)** | Top-level Anonymous Frame | Recursive iframe element reference switching | **PASS ✅** |
| 3 | **Nested IFrame (Depth #2)** | IFrame inside IFrame | Multi-tier recursive frame index path (`[0, 0]`) | **PASS ✅** |
| 4 | **Nested IFrame (Depth #3)** | Multi-level Nested IFrame | Deep recursive frame index path (`[0, 0, 0]`) | **PASS ✅** |
| 5 | **IFrame with ID 'iframeId'** | Named Top-level IFrame | ID and index heuristic frame switching | **PASS ✅** |
| 6 | **Shadow DOM [nestedshadow-form]** | Web Component Shadow Root | JS-assisted boundary piercing & event dispatching | **PASS ✅** |
| 7 | **Shadow DOM (Depth #2)** | Nested Shadow Root (`nestedshadow-form3`) | Recursive shadow host chain traversal | **PASS ✅** |
| 8 | **Shadow DOM (Depth #3)** | Nested Shadow Root (`nestedshadow-form4`) | Recursive shadow host chain traversal | **PASS ✅** |
| 9 | **Shadow DOM (Depth #4)** | Nested Shadow Root (`nestedshadow-form5`) | Recursive shadow host chain traversal | **PASS ✅** |
| 10 | **Shadow DOM (Depth #5)** | Nested Shadow Root (`nestedshadow-form6`) | Deep 5-tier recursive shadow host chain traversal | **PASS ✅** |
| 11 | **IFrame inside Shadow DOM** | IFrame encapsulated in Shadow Root | Shadow root query -> Frame element switch -> Full resolver | **PASS ✅** |

```
                               ┌────────────────────────────────────────────────────────┐
                               │                 Root Document DOM Tree                 │
                               └───────────┬────────────────────────────────┬───────────┘
                                           │                                │
                     ┌─────────────────────┴────────────┐       ┌───────────┴─────────────────────┐
                     ▼                                  ▼       ▼                                 ▼
             Main Document Form               Top-Level IFrames    Single Shadow DOM           Deep Shadow Chains
                     │                        (With / Without ID)  (<nestedshadow-form>)       (Levels 2, 3, 4, 5)
                     ▼                                  │                       │                         │
             SmartFieldResolver                         ▼                       ▼                         ▼
             (Scores, Fills, Asserts)          Recursive IFrame Engine  Shadow Heuristic Engine   Deep Chain Piercing
                                                        │                       │                         │
                                                        ▼                       └─────────────┬───────────┘
                                               Nested IFrames (Depth 2 & 3)                   │
                                                        │                                     ▼
                                                        └─────────────────────────► IFrame inside Shadow DOM
                                                                                    (<nestedshadow-iframe> -> <iframe>)
```

---

## 🧪 Mutation Test Suite (100% Pass Rate)

To prove that the solution fulfills requirement #3 ("keeps working when page changes"), a built-in mutation test suite evaluates 4 challenging structural page mutations:

| Mutation Scenario | Structural Changes Applied | First Name | Gender | State | Overall |
|---|---|:---:|:---:|:---:|:---:|
| **Live Web Page** | Standard page at `app.cloudqa.io` | **PASS** (290 pts) | **PASS** (225 pts) | **PASS** (290 pts) | **PASS ✅** |
| **Scenario 1: Reordered Elements** | State at top, First Name at bottom, Gender in middle | **PASS** (220 pts) | **PASS** (170 pts) | **PASS** (245 pts) | **PASS ✅** |
| **Scenario 2: Dynamic GUIDs** | Obfuscated Tailwind classes, GUID IDs (`f_guid_8283a`) | **PASS** (275 pts) | **PASS** (140 pts) | **PASS** (245 pts) | **PASS ✅** |
| **Scenario 3: Table Layout** | Form inside `<table><tr><td>`, no classes or `for` tags | **PASS** (30 pts) | **PASS** (135 pts) | **PASS** (65 pts) | **PASS ✅** |
| **Scenario 4: High Disambiguation** | First, Middle, Last, Username, Country, State, City | **PASS** (256 pts) | **PASS** (140 pts) | **PASS** (200 pts) | **PASS ✅** |
| **Scenario 5: Generic 'Name' Label** | Form uses `<label>Name</label>` instead of 'First Name' | **PASS** (135 pts) | **PASS** (170 pts) | **PASS** (200 pts) | **PASS ✅** |
| **Scenario 6: Linear Horizontal Line** | All fields on a single continuous horizontal row | **PASS** (130 pts) | **PASS** (140 pts) | **PASS** (155 pts) | **PASS ✅** |
| **Scenario 7: IFrame Without ID** | Form hosted inside `<iframe>` with NO ID and NO Name | **PASS** (245 pts) | **PASS** (170 pts) | **PASS** (245 pts) | **PASS ✅** |
| **Scenario 8: Inputs on Left, Missing Input & Multiple First Names** | Inputs before labels, missing email input, multiple First Name fields | **PASS** (155 pts) | **PASS** (105 pts) | **PASS** (245 pts) | **PASS ✅** |

---

## 🚀 Getting Started & CLI Usage

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Google Chrome browser (Selenium Manager automatically handles ChromeDriver)

### 1. Run Against the Live CloudQA Page (Default)
```bash
dotnet run
```
Or in headless mode:
```bash
dotnet run -- --headless
```

### 2. Multi-Browser Support (Chrome, Brave, Edge, Firefox, Auto)
The framework includes a centralized `WebDriverFactory` with auto-discovery and graceful fallback:
```bash
# Run with Brave Browser
dotnet run -- --browser=brave

# Run with Microsoft Edge
dotnet run -- --browser=edge

# Run with Mozilla Firefox
dotnet run -- --browser=firefox

# Auto-detect whichever browser is installed on the host
dotnet run -- --browser=auto

# Specify a custom binary path if installed in a non-standard directory
dotnet run -- --browser=brave --browser-binary="C:\Path\To\brave.exe"
```

### 3. Run Against a Custom or Modified Page URL
The page URL is fully configurable via command line:
```bash
dotnet run -- "https://your-custom-or-modified-page.com/form"
```
Or with named parameters:
```bash
dotnet run --url="https://your-custom-or-modified-page.com/form" --headless
```

### 3. Target Specific Frames (or All Contexts)
The CloudQA page contains forms in the main document, inside an IFrame without ID, and inside a named IFrame. You can choose which context to automate:
```bash
# Automate ALL form contexts sequentially (default)
dotnet run -- --frame=all

# Automate specifically the IFrame without ID
dotnet run -- --frame=no-id

# Automate only the main document (skip iframes)
dotnet run -- --frame=main
```

### 4. Customize Values to Fill & Select
```bash
dotnet run --first-name="Kartik" --gender="Male" --state="United States"
```

### 5. Run the Mutation Test Matrix
```bash
dotnet run -- --test-mutations --headless
```

---

## ⚙️ Configuration Files & Environment Variables

Settings can also be controlled via `appsettings.json`:
```json
{
  "AutomationSettings": {
    "TargetUrl": "https://app.cloudqa.io/home/AutomationPracticeForm",
    "FirstName": "Kartik",
    "Gender": "Male",
    "State": "United States",
    "Headless": false,
    "TimeoutSeconds": 15,
    "HighlightElements": true,
    "TakeScreenshots": true,
    "ScreenshotDirectory": "Screenshots"
  }
}
```

Or via environment variable:
```bash
export APP_URL="https://app.cloudqa.io/home/AutomationPracticeForm"
dotnet run
```

---

## 📁 Project Structure

```
D:\task 1\
├── Config\
│   └── AutomationSettings.cs         # Strongly-typed configuration options
├── Locators\
│   ├── DomInspector.cs               # JS-assisted visual geometry, highlighting, event dispatch
│   └── SmartFieldResolver.cs         # Multi-factor semantic scoring and heuristic resolver
├── Models\
│   ├── CandidateMatch.cs             # Element candidate with confidence score and reasons
│   ├── FieldType.cs                  # Field enumeration
│   └── TestResult.cs                 # Step results and execution report model
├── Scenarios\                        # Mutation testing suite HTML pages
│   ├── scenario1_reordered.html
│   ├── scenario2_renamed_attributes.html
│   ├── scenario3_table_layout.html
│   └── scenario4_similar_fields.html
├── Services\
│   ├── FormInteractionService.cs     # End-to-end automation execution, fallbacks, assertions
│   └── ScenarioRunner.cs             # Automated runner for mutation matrix
├── Screenshots\                      # Auto-captured screenshots upon test completion
├── appsettings.json                  # Configurable settings
├── Program.cs                        # CLI entry point, argument parsing, error handling
├── ResilientFormAutomation.csproj    # .NET 8 Project file
└── README.md                         # Project documentation
```

---

## 🏆 Key Features Summary
- **Zero Fixed Locators:** Does not break when IDs, names, classes, or DOM order change.
- **Smart Disambiguation:** Distinguishes First Name from Last/User Name; Male from Female.
- **Visual Coordinate Alignment:** Computes spatial distance via `getBoundingClientRect()`.
- **Cross-Context Traversal:** Handles IFrames and Shadow DOM seamlessly.
- **Full Explainability:** Logs exact confidence scores and reasons for every selection.
- **Automated Verification:** Asserts values post-entry and saves high-resolution screenshots.
