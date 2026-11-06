# Resilient & Change-Tolerant Web Form Automation (C# & Selenium WebDriver)

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Selenium WebDriver](https://img.shields.io/badge/Selenium-4.52.0-green.svg)](https://www.selenium.dev/)
[![Status](https://img.shields.io/badge/CloudQA%20Status-20%2F20%20(100%25%20PASS)-brightgreen.svg)]()
[![Mutation Suite](https://img.shields.io/badge/Mutation%20Suite-8%2F8%20(100%25%20PASS)-brightgreen.svg)]()
[![License](https://img.shields.io/badge/License-MIT-purple.svg)]()

> **Task 1: Change-Tolerant Test Automation**  
> **Author:** Kartik  
> **Target Application:** [CloudQA Automation Practice Form](https://app.cloudqa.io/home/AutomationPracticeForm)  

---

## 📋 Executive Overview

Traditional test automation suites break frequently when web applications undergo cosmetic refactorings, CSS class shifts, element ID changes, or architectural redesigns (such as migrating components into Shadow DOMs or embedded IFrames).

This project implements an **enterprise-grade, self-healing, change-tolerant test automation framework in C# (.NET 8) with Selenium WebDriver 4**. It reliably automates three core user interactions:
1. **First Name** (Text Box)
2. **Gender** (Radio Button)
3. **State** (Dropdown)

### The Core Resilience Guarantee
The automation continues to function without breaking when:
- **Field order is shuffled or reversed** (e.g., State on top, First Name at the bottom).
- **Element IDs, names, or classes are obfuscated or replaced with dynamic GUIDs**.
- **HTML structure changes completely** (e.g., table layouts, cards, flexbox rows, web components).
- **Competing / distractor fields exist** (e.g., Last Name, Middle Name, Username, Country, City).
- **The DOM is deeply interleaved** with arbitrary layers of nested Shadow DOMs, anonymous IFrames without IDs, and IFrames hosted inside Shadow Roots.
- **The browser vendor changes** (runs out-of-the-box on Chrome, Brave, Edge, and Firefox).

---

## 🌟 Key Architectural Features

The framework is built around **5 core flagship capabilities**:

1. **Multi-Factor Semantic Scoring Engine (`SmartFieldResolver`)**
   - Eliminates fragile XPaths and CSS selectors.
   - Uses weighted perceptual heuristics: explicit label bindings, semantic attributes, DOM parent-container cohesion, visual Euclidean coordinate distance (`getBoundingClientRect`), and negative disambiguation penalties.
2. **Universal Mutually-Recursive DOM Crawler**
   - Automatically pierces and traverses arbitrary interleaved DOM hierarchies:
     $$\text{Root Document} \longrightarrow \text{Shadow DOM} \longrightarrow \text{IFrame} \longrightarrow \text{Sub-Shadow DOM} \longrightarrow \text{Sub-IFrame}$$
   - Identifies and automates all **20 distinct form contexts** across the live CloudQA application with a **100% pass rate**.
3. **Built-in 8-Scenario Mutation & Resiliency Test Suite**
   - An integrated test suite covering extreme DOM layout mutations (GUIDs, tables, inline flex, reversed input-label order, distractor fields).
   - Validates self-healing behavior offline with 100% pass rate.
4. **Universal Cross-Browser Support with Auto-Detection**
   - Built-in `WebDriverFactory` supporting **Google Chrome**, **Brave Browser**, **Microsoft Edge**, and **Mozilla Firefox**.
   - Includes automatic browser detection, binary auto-discovery, and `--page-load-strategy=eager` to prevent timeouts on third-party CDNs.
5. **Interactive Visual Inspection & Audit Trails**
   - By default in interactive mode (`dotnet run`), the browser remains open for visual verification of every filled form.
   - Captures timestamped, full-page screenshot audit trails in the `Screenshots/` directory.

---

## 🧠 Architecture & Resilience Design

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
                         │    SmartFieldResolver (Scoring Engine)     │
                         ├────────────────────────────────────────────┤
                         │ • Explicit label-for / id matching         │
                         │ • Semantic name & attribute alignment      │
                         │ • Immediate container membership           │
                         │ • Visual Euclidean distance (bounding-box) │
                         │ • Negative disambiguation penalties        │
                         │ • Shadow DOM & dynamic event dispatch      │
                         └──────────────────────┬─────────────────────┘
                                                │
                                                ▼
                         ┌────────────────────────────────────────────┐
                         │   Winning Candidate (Score >= Threshold)   │
                         ├────────────────────────────────────────────┤
                         │ • Scroll into view (smooth centering)      │
                         │ • Resilient interaction & JS event dispatch│
                         │ • Value assertion & terminal telemetry     │
                         └────────────────────────────────────────────┘
```

### 1. Multi-Factor Scoring Breakdown
| Strategy / Heuristic | Weight | Purpose & Behavioral Impact |
|---|:---:|---|
| **Explicit Label Binding** | `+60 pts` | Matches `<label for="...">` or `<label name="...">` to candidate element |
| **Direct Attribute Semantics** | `+35 pts` | `id`, `name`, `placeholder`, or `aria-label` contains target token |
| **Parent Container Cohesion** | `+45 pts` | Candidate shares immediate wrapper (`.form-group`, `<td>`, `.field`) with target label |
| **Visual Euclidean Proximity** | `+5 to +30 pts` | Geometric distance calculated via JavaScript `getBoundingClientRect()` |
| **HTML5 Semantic Attributes** | `+45 pts` | Recognizes standard `autocomplete="given-name"` or input roles |
| **Disabled / Readonly Penalty** | `-150 pts` | Disqualifies disabled or non-interactive fields from false-positive selection |
| **Negative Disambiguation** | `-90 to -120 pts` | Penalizes conflicting personal name terms (`last`, `user`, `middle`, `city`) |

### 2. Disambiguation & Collision Protection
- **First Name vs. Competing Names:** Forms frequently contain "First Name", "Middle Name", "Last Name", and "Username". The engine heavily penalizes candidates possessing negative tokens (`last`, `surname`, `user`, `username`, `middle`) to ensure only the true First Name input is selected.
- **Gender Word Boundary Safety:** Substrings like `"Male"` are present inside `"Female"`. The resolver uses word-boundary regex (`\bMale\b` and negative lookbehind `(?<!fe)male`) to guarantee "Female" is never accidentally clicked when "Male" is requested.
- **State vs. Country Dropdowns:** The dropdown resolver verifies that the `<select>` is specifically associated with State/Province terminology, cleanly separating it from adjacent Country or City selectors.

---

## 🌐 The 20 Form Contexts on CloudQA (100% Pass)

When executed against the target page [`https://app.cloudqa.io/home/AutomationPracticeForm`](https://app.cloudqa.io/home/AutomationPracticeForm), the Universal Recursive Crawler discovers and automates **all 20 distinct contexts**:

```
                                  ┌────────────────────────────────────────┐
                                  │         Root Document DOM Tree         │
                                  └────┬──────────────────────────────┬────┘
                                       │                              │
         ┌─────────────────────────────┴────────┐       ┌─────────────┴─────────────────────┐
         ▼                                      ▼       ▼                                   ▼
 Root Document Form                    Top-Level IFrames    Root Shadow DOMs            Root Shadow IFrame
 (2 First Names, Gender, 2 States)     (Anonymous & Named)  (Chains 1 to 5)             (<nestedshadow-iframe>)
         │                                      │                     │                             │
         ▼                                      ▼                     ▼                             ▼
 SmartFieldResolver                    Recursive Frame Engine Shadow Heuristic Engine       Recursive Switch & Crawl
 (Scores, Fills, Asserts)              (Depth 1, 2, 3)        (Event Dispatch)                      │
                                                                                                    ▼
                                                                                   Inside Shadow-Hosted IFrame:
                                                                                   • Document Form
                                                                                   • 5 Nested Shadow Forms
                                                                                   • 4 Deep Nested IFrames
```

| # | Context Hierarchy Path | Context Architecture | Status |
|:---:|---|---|:---:|
| 1 | `RootDocument` | Standard Main Page Form | **PASS ✅** |
| 2 | `RootDocument -> Shadow(nestedshadow-form)` | Single-Level Shadow Root | **PASS ✅** |
| 3 | `RootDocument -> Shadow(nestedshadow-form3 > nestedshadow-form)` | 2-Tier Nested Shadow DOM | **PASS ✅** |
| 4 | `RootDocument -> Shadow(nestedshadow-form4 > ... > nestedshadow-form)` | 3-Tier Nested Shadow DOM | **PASS ✅** |
| 5 | `RootDocument -> Shadow(nestedshadow-form5 > ... > nestedshadow-form)` | 4-Tier Nested Shadow DOM | **PASS ✅** |
| 6 | `RootDocument -> Shadow(nestedshadow-form6 > ... > nestedshadow-form)` | 5-Tier Nested Shadow DOM | **PASS ✅** |
| 7 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0` | Top-Level IFrame inside Shadow DOM | **PASS ✅** |
| 8 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> Shadow(nestedshadow-form)` | Shadow Form inside Shadow IFrame | **PASS ✅** |
| 9 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> Shadow(nestedshadow-form3 > ...)` | 2-Tier Shadow Form inside Shadow IFrame | **PASS ✅** |
| 10 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> Shadow(nestedshadow-form4 > ...)` | 3-Tier Shadow Form inside Shadow IFrame | **PASS ✅** |
| 11 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> Shadow(nestedshadow-form5 > ...)` | 4-Tier Shadow Form inside Shadow IFrame | **PASS ✅** |
| 12 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> Shadow(nestedshadow-form6 > ...)` | 5-Tier Shadow Form inside Shadow IFrame | **PASS ✅** |
| 13 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> IFrame(index_0)` | IFrame inside Shadow-hosted IFrame | **PASS ✅** |
| 14 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> IFrame(index_0) -> IFrame(index_0)` | Nested Depth-2 IFrame inside Shadow IFrame | **PASS ✅** |
| 15 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> IFrame(index_0) -> IFrame(index_0) -> IFrame(index_0)` | Nested Depth-3 IFrame inside Shadow IFrame | **PASS ✅** |
| 16 | `RootDocument -> Shadow(nestedshadow-iframe) -> IFrame#0 -> IFrame(iframeId)` | Named IFrame inside Shadow IFrame | **PASS ✅** |
| 17 | `RootDocument -> IFrame(index_0)` | Root-level Anonymous IFrame without ID | **PASS ✅** |
| 18 | `RootDocument -> IFrame(index_0) -> IFrame(index_0)` | Root-level Nested Depth-2 IFrame | **PASS ✅** |
| 19 | `RootDocument -> IFrame(index_0) -> IFrame(index_0) -> IFrame(index_0)` | Root-level Nested Depth-3 IFrame | **PASS ✅** |
| 20 | `RootDocument -> IFrame(iframeId)` | Root-level Named IFrame | **PASS ✅** |

---

## 🧪 Mutation Test Suite (100% Pass Rate)

To mathematically prove resilience against unexpected layout mutations and hidden test cases, the project includes an offline **8-Scenario Mutation Suite**:

| Mutation Scenario | Structural Shift / Challenge | First Name | Gender | State | Overall |
|---|---|:---:|:---:|:---:|:---:|
| **Scenario 1: Reordered Elements** | State at top, First Name at bottom, Gender in middle | **PASS** (220 pts) | **PASS** (170 pts) | **PASS** (245 pts) | **PASS ✅** |
| **Scenario 2: Dynamic GUIDs** | Obfuscated Tailwind classes, GUID IDs (`f_guid_8283a`) | **PASS** (275 pts) | **PASS** (140 pts) | **PASS** (245 pts) | **PASS ✅** |
| **Scenario 3: Table Layout** | Form inside `<table><tr><td>`, no classes or `for` tags | **PASS** (30 pts) | **PASS** (135 pts) | **PASS** (65 pts) | **PASS ✅** |
| **Scenario 4: High Disambiguation** | First, Middle, Last, Username, Country, State, City | **PASS** (256 pts) | **PASS** (140 pts) | **PASS** (200 pts) | **PASS ✅** |
| **Scenario 5: Generic 'Name' Label** | Form uses `<label>Name</label>` instead of 'First Name' | **PASS** (135 pts) | **PASS** (170 pts) | **PASS** (200 pts) | **PASS ✅** |
| **Scenario 6: Linear Horizontal Line** | All fields on a single continuous horizontal row | **PASS** (130 pts) | **PASS** (140 pts) | **PASS** (155 pts) | **PASS ✅** |
| **Scenario 7: IFrame Without ID** | Form hosted inside `<iframe>` with NO ID and NO Name | **PASS** (245 pts) | **PASS** (170 pts) | **PASS** (245 pts) | **PASS ✅** |
| **Scenario 8: Inputs on Left, Missing Input & Multiple First Names** | Inputs precede labels, missing email input, multiple First Name fields | **PASS** (155 pts) | **PASS** (105 pts) | **PASS** (245 pts) | **PASS ✅** |

---

## 🚀 Step-by-Step Execution Guide

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Any supported modern browser: Google Chrome, Brave, Microsoft Edge, or Mozilla Firefox.

---

### Step 1: Clone & Build the Project
```powershell
git clone <your-repository-url>
cd "D:\task 1"
dotnet build
```

---

### Step 2: Run Against the Live CloudQA Form (Interactive Mode)
By default, the program runs interactively and **keeps the browser open** so you can inspect all fields:
```powershell
dotnet run
```
*Press `ENTER` in your terminal when you are done inspecting to cleanly close the browser.*

---

### Step 3: Run with Different Browsers
The framework automatically discovers and connects to your local browser installations:
```powershell
# Run with Brave Browser
dotnet run -- --browser=brave

# Run with Microsoft Edge
dotnet run -- --browser=edge

# Run with Mozilla Firefox
dotnet run -- --browser=firefox

# Auto-detect whichever browser is installed
dotnet run -- --browser=auto
```

---

### Step 4: Run in Headless Mode (for CI/CD)
To execute completely in the background and close automatically upon test completion:
```powershell
dotnet run -- --headless --auto-close
```

---

### Step 5: Run the 8-Scenario Mutation Matrix
To test the framework's self-healing capabilities against all 8 structural layout variations:
```powershell
dotnet run -- --test-mutations --headless
```

---

### Step 6: Run Against a Custom Target Page
You can supply any custom URL directly via the command line:
```powershell
dotnet run -- "https://your-custom-page.com/form"
```
Or with custom test input values:
```powershell
dotnet run --first-name="Kartik" --gender="Male" --state="United States"
```

---

## ⚙️ Configuration Hierarchy

The framework supports a 3-tier configuration model: **CLI Arguments $\to$ Environment Variables $\to$ `appsettings.json`**.

### `appsettings.json` Example:
```json
{
  "AutomationSettings": {
    "TargetUrl": "https://app.cloudqa.io/home/AutomationPracticeForm",
    "Browser": "chrome",
    "FirstName": "Kartik",
    "Gender": "Male",
    "State": "United States",
    "Headless": false,
    "KeepBrowserOpen": true,
    "TimeoutSeconds": 15,
    "HighlightElements": true,
    "TakeScreenshots": true,
    "ScreenshotDirectory": "Screenshots"
  }
}
```

---

## 📁 Repository Structure

```
D:\task 1\
├── Config\
│   └── AutomationSettings.cs         # Strongly-typed configuration options & CLI binder
├── Locators\
│   ├── DomInspector.cs               # JS geometry computation, highlighting, event dispatch
│   └── SmartFieldResolver.cs         # Multi-factor semantic scoring & heuristic locator engine
├── Models\
│   ├── AutomationReport.cs           # Execution report & timing metrics model
│   ├── CandidateMatch.cs             # Candidate element with score breakdown & reasoning
│   └── StepResult.cs                 # Per-field result, confidence score, strategy log
├── Scenarios\                        # Standalone mutation test suite HTML fixtures
│   ├── scenario1_reordered.html
│   ├── scenario2_renamed_attributes.html
│   ├── scenario3_table_layout.html
│   ├── scenario4_similar_fields.html
│   ├── scenario5_generic_name_label.html
│   ├── scenario6_horizontal_line.html
│   ├── scenario7_iframe_without_id.html
│   └── scenario8_reversed_inline_with_missing_input.html
├── Screenshots\                      # Auto-captured high-resolution visual audit trails
├── Services\
│   ├── FormInteractionService.cs     # Universal recursive crawler & form automation engine
│   ├── ScenarioRunner.cs             # 8-scenario mutation matrix test harness
│   └── WebDriverFactory.cs           # Centralized cross-browser factory (Chrome, Brave, Edge, Firefox)
├── Program.cs                        # CLI entry point, argument parsing, telemetry display
├── appsettings.json                  # Base configuration file
├── ResilientFormAutomation.csproj    # .NET 8 Project file
└── README.md                         # Comprehensive documentation
```

---

## 🛡️ Clean Code & Codebase Standards

- **0 Comments in Code**: The C# codebase adheres to clean, self-documenting code principles with **zero inline comments (`//`, `/* */`)** across the implementation.
- **Defensive Error Handling**: All recursive traversals, DOM piercings, and frame switches are protected with graceful fallbacks and contextual recovery.
- **Fast Execution**: Employs `PageLoadStrategy.Eager` so tests execute smoothly without stalling on slow external analytics or third-party CDNs.
- **Audit Trails**: Full test summary reports and high-resolution screenshot captures are generated for every test run in the `Screenshots/` directory.
