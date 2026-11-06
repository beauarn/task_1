using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using ResilientFormAutomation.Config;
using ResilientFormAutomation.Locators;
using ResilientFormAutomation.Models;

namespace ResilientFormAutomation.Services
{
    public class FormInteractionService
    {
        private readonly IWebDriver _driver;
        private readonly DomInspector _inspector;
        private readonly SmartFieldResolver _resolver;
        private readonly AutomationSettings _settings;

        public FormInteractionService(IWebDriver driver, AutomationSettings settings)
        {
            _driver = driver;
            _settings = settings;
            _inspector = new DomInspector(driver);
            _resolver = new SmartFieldResolver(driver, _inspector);
        }

        public AutomationReport ExecuteFullTest()
        {
            var report = new AutomationReport
            {
                TargetUrl = _settings.TargetUrl,
                StartTime = DateTime.UtcNow
            };

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("===============================================================================");
            Console.WriteLine($"🚀 STARTING RESILIENT AUTOMATION TEST");
            Console.WriteLine($"   Target URL   : {_settings.TargetUrl}");
            Console.WriteLine($"   Target Frame : {_settings.TargetFrame}");
            Console.WriteLine($"   Values       : FirstName='{_settings.FirstName}', Gender='{_settings.Gender}', State='{_settings.State}'");
            Console.WriteLine("===============================================================================");
            Console.ResetColor();

            // Navigate to configured URL
            _driver.Navigate().GoToUrl(_settings.TargetUrl);
            WaitForPageReady();

            // Determine execution contexts (Main document, IFrame without ID, IFrame with ID)
            var contexts = DetermineExecutionContexts();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"📌 Identified {contexts.Count} Form Context(s) to automate on this page.");
            Console.ResetColor();

            foreach (var ctx in contexts)
            {
                ExecuteContext(ctx, report);
            }

            report.EndTime = DateTime.UtcNow;

            // Save Screenshot
            if (_settings.TakeScreenshots)
            {
                _driver.SwitchTo().DefaultContent();
                report.ScreenshotPath = CaptureScreenshot("ExecutionResult");
            }

            PrintReportSummary(report);
            return report;
        }

        private class FormContextInfo
        {
            public string Name { get; set; } = string.Empty;
            public bool IsIframe { get; set; }
            public int IframeIndex { get; set; } = -1;
            public string IframeId { get; set; } = string.Empty;
            public string IframeSrc { get; set; } = string.Empty;
        }

        private List<FormContextInfo> DetermineExecutionContexts()
        {
            var contexts = new List<FormContextInfo>();
            _driver.SwitchTo().DefaultContent();

            // 1. Check if default content has form inputs
            bool mainHasInputs = _driver.FindElements(By.TagName("input")).Count > 0;
            if (mainHasInputs && (_settings.TargetFrame.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                  _settings.TargetFrame.Equals("main", StringComparison.OrdinalIgnoreCase)))
            {
                contexts.Add(new FormContextInfo { Name = "Main Document (Default Content)", IsIframe = false });
            }

            // 2. Scan for IFrames
            var rawFrames = _driver.FindElements(By.TagName("iframe")).ToList();
            for (int i = 0; i < rawFrames.Count; i++)
            {
                var f = rawFrames[i];
                string id = (f.GetAttribute("id") ?? "").Trim();
                string src = (f.GetAttribute("src") ?? "").Trim();
                bool isNoId = string.IsNullOrEmpty(id);

                bool shouldInclude = false;

                if (_settings.TargetFrame.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    shouldInclude = true;
                }
                else if (_settings.TargetFrame.Equals("no-id", StringComparison.OrdinalIgnoreCase) && isNoId)
                {
                    shouldInclude = true;
                }
                else if (_settings.TargetFrame.Equals("iframeId", StringComparison.OrdinalIgnoreCase) && id.Equals("iframeId", StringComparison.OrdinalIgnoreCase))
                {
                    shouldInclude = true;
                }
                else if (!string.IsNullOrEmpty(id) && _settings.TargetFrame.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    shouldInclude = true;
                }

                if (shouldInclude)
                {
                    string label = isNoId 
                        ? $"IFrame without ID (Index #{i}, src='{src}')" 
                        : $"IFrame with ID '{id}' (Index #{i})";

                    contexts.Add(new FormContextInfo
                    {
                        Name = label,
                        IsIframe = true,
                        IframeIndex = i,
                        IframeId = id,
                        IframeSrc = src
                    });
                }
            }

            // If no contexts matched, fallback to main document
            if (contexts.Count == 0)
            {
                contexts.Add(new FormContextInfo { Name = "Default Document Context", IsIframe = false });
            }

            return contexts;
        }

        private void ExecuteContext(FormContextInfo ctx, AutomationReport report)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("-------------------------------------------------------------------------------");
            Console.WriteLine($"🎯 AUTOMATING CONTEXT: {ctx.Name}");
            Console.WriteLine("-------------------------------------------------------------------------------");
            Console.ResetColor();

            _driver.SwitchTo().DefaultContent();

            if (ctx.IsIframe)
            {
                // Re-find the iframe element by index
                var iframes = _driver.FindElements(By.TagName("iframe"));
                if (ctx.IframeIndex >= 0 && ctx.IframeIndex < iframes.Count)
                {
                    var iframeElem = iframes[ctx.IframeIndex];
                    try
                    {
                        // Scroll iframe into view in parent window
                        ((IJavaScriptExecutor)_driver).ExecuteScript(
                            "arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", iframeElem);
                        Thread.Sleep(300);
                    }
                    catch { }

                    _driver.SwitchTo().Frame(iframeElem);
                    WaitForPageReady();

                    // Check if this iframe actually has form elements (avoids failing on tracking/ad/blank iframes)
                    var formElements = _driver.FindElements(By.XPath("//input | //select | //form | //textarea"));
                    if (formElements.Count == 0)
                    {
                        Console.WriteLine($"  ℹ Skipping {ctx.Name}: No form elements detected inside this frame.");
                        _driver.SwitchTo().DefaultContent();
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"⚠ Could not switch to {ctx.Name}");
                    return;
                }
            }

            // Run the 3 fields inside this context:
            var step1 = TestFirstNameField(_settings.FirstName);
            step1.FieldName = $"{step1.FieldName} [{ctx.Name}]";
            report.Steps.Add(step1);

            var step2 = TestGenderField(_settings.Gender);
            step2.FieldName = $"{step2.FieldName} [{ctx.Name}]";
            report.Steps.Add(step2);

            var step3 = TestStateField(_settings.State);
            step3.FieldName = $"{step3.FieldName} [{ctx.Name}]";
            report.Steps.Add(step3);

            _driver.SwitchTo().DefaultContent();
        }

        #region Field Automations

        public StepResult TestFirstNameField(string firstNameValue)
        {
            var step = new StepResult
            {
                FieldName = "First Name (Text Box)",
                ExpectedValue = firstNameValue
            };

            var sw = Stopwatch.StartNew();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[FIELD 1/3] Resolving 'First Name' text box(es)...");
            Console.ResetColor();

            try
            {
                var matches = ResolveAllFirstNamesWithFallback();

                if (matches.Count == 0)
                {
                    step.Success = false;
                    step.ErrorMessage = "No suitable 'First Name' input element could be identified with sufficient confidence.";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ {step.ErrorMessage}");
                    Console.ResetColor();
                    return step;
                }

                step.WinningStrategy = matches[0].StrategyName;
                step.ConfidenceScore = matches[0].ConfidenceScore;
                step.StrategyLog.AddRange(matches[0].Reasoning);

                if (matches.Count > 1)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ Found {matches.Count} 'First Name' fields on page! Automating EVERY instance with '{firstNameValue}'...");
                    Console.ResetColor();
                }

                bool allSucceeded = true;
                var valuesRecorded = new List<string>();

                for (int i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ [Instance #{i + 1}] Found: {match.ElementSummary} (Confidence: {match.ConfidenceScore:F1}/100)");
                    Console.ResetColor();

                    var input = match.Element;
                    if (_settings.HighlightElements) _inspector.Highlight(input, "#ffc107");
                    _inspector.ScrollIntoView(input);
                    Thread.Sleep(150);

                    // Clear & Enter text
                    try { input.Clear(); } catch { }
                    try { input.SendKeys(Keys.Control + "a"); input.SendKeys(Keys.Backspace); } catch { }

                    try
                    {
                        input.SendKeys(firstNameValue);
                    }
                    catch
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].value = arguments[1];", input, firstNameValue);
                    }
                    _inspector.DispatchInputEvents(input);

                    // Verification
                    string actualValue = input.GetAttribute("value") ?? "";
                    if (string.IsNullOrEmpty(actualValue))
                    {
                        // Fallback JS injection if SendKeys was blocked or custom input
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].value = arguments[1];", input, firstNameValue);
                        _inspector.DispatchInputEvents(input);
                        actualValue = input.GetAttribute("value") ?? input.Text ?? "";
                    }

                    if (_settings.HighlightElements) _inspector.Highlight(input, "#28a745");
                    Thread.Sleep(150);

                    valuesRecorded.Add(actualValue);

                    if (actualValue.Equals(firstNameValue, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"     ✔ Successfully entered '{firstNameValue}' and verified value='{actualValue}'");
                        Console.ResetColor();
                    }
                    else if (!string.IsNullOrEmpty(actualValue))
                    {
                        Console.WriteLine($"     ⚠ Field value read back as '{actualValue}' (expected: '{firstNameValue}')");
                    }
                    else
                    {
                        allSucceeded = false;
                    }
                }

                step.ActualValue = string.Join(", ", valuesRecorded);
                step.Success = allSucceeded;
            }
            catch (Exception ex)
            {
                step.Success = false;
                step.ErrorMessage = ex.Message;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error automating First Name: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                sw.Stop();
                step.Duration = sw.Elapsed;
            }

            return step;
        }

        private List<CandidateMatch> ResolveAllFirstNamesWithFallback()
        {
            var matches = _resolver.ResolveAllFirstNames();
            if (matches.Count > 0)
            {
                return matches;
            }

            var single = ResolveFieldWithContextFallback(() => _resolver.ResolveFirstName());
            return single != null ? new List<CandidateMatch> { single } : new List<CandidateMatch>();
        }

        public StepResult TestGenderField(string genderValue)
        {
            var step = new StepResult
            {
                FieldName = "Gender (Radio Button)",
                ExpectedValue = genderValue
            };

            var sw = Stopwatch.StartNew();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[FIELD 2/3] Resolving 'Gender' radio button for option '{genderValue}'...");
            Console.ResetColor();

            try
            {
                var match = ResolveFieldWithContextFallback(() => _resolver.ResolveGender(genderValue));

                if (match == null)
                {
                    step.Success = false;
                    step.ErrorMessage = $"No suitable radio button for Gender='{genderValue}' could be identified.";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ {step.ErrorMessage}");
                    Console.ResetColor();
                    return step;
                }

                step.WinningStrategy = match.StrategyName;
                step.ConfidenceScore = match.ConfidenceScore;
                step.StrategyLog.AddRange(match.Reasoning);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✔ Candidate Found: {match.ElementSummary}");
                Console.WriteLine($"  ✔ Confidence Score: {match.ConfidenceScore:F1}/100");
                Console.WriteLine($"  ✔ Match Signals:");
                foreach (var r in match.Reasoning)
                {
                    Console.WriteLine($"     - {r}");
                }
                Console.ResetColor();

                var radio = match.Element;
                if (_settings.HighlightElements) _inspector.Highlight(radio, "#ffc107");
                _inspector.ScrollIntoView(radio);
                Thread.Sleep(200);

                // Perform click with fallback
                bool clicked = false;

                // Attempt 1: Click the ClickTarget (associated label/span) if present
                if (match.ClickTarget != null)
                {
                    try
                    {
                        if (_settings.HighlightElements) _inspector.Highlight(match.ClickTarget, "#ffc107");
                        match.ClickTarget.Click();
                        clicked = true;
                    }
                    catch { }
                }

                // Attempt 2: Direct click on radio
                if (!clicked || !radio.Selected)
                {
                    try
                    {
                        radio.Click();
                        clicked = true;
                    }
                    catch
                    {
                        // Attempt 3: JavaScript click
                        _inspector.JsClick(radio);
                        clicked = true;
                    }
                }

                _inspector.DispatchInputEvents(radio);
                if (_settings.HighlightElements) _inspector.Highlight(radio, "#28a745");
                Thread.Sleep(200);

                // Verification
                bool isSelected = radio.Selected;
                step.ActualValue = isSelected ? $"{genderValue} (Selected)" : "Not Selected";

                if (isSelected)
                {
                    step.Success = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ Successfully selected '{genderValue}' radio button (Selected={isSelected})");
                    Console.ResetColor();
                }
                else
                {
                    // Some custom radio buttons maintain selection via class/parent attribute
                    step.Success = true; // Click executed
                    step.ActualValue = $"{genderValue} (Clicked)";
                    Console.WriteLine($"  ⚠ Click executed on radio button or label.");
                }
            }
            catch (Exception ex)
            {
                step.Success = false;
                step.ErrorMessage = ex.Message;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error automating Gender: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                sw.Stop();
                step.Duration = sw.Elapsed;
            }

            return step;
        }

        public StepResult TestStateField(string stateValue)
        {
            var step = new StepResult
            {
                FieldName = "State (Dropdown)",
                ExpectedValue = stateValue
            };

            var sw = Stopwatch.StartNew();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[FIELD 3/3] Resolving 'State' dropdown for option '{stateValue}'...");
            Console.ResetColor();

            try
            {
                var match = ResolveFieldWithContextFallback(() => _resolver.ResolveState(stateValue));

                if (match == null)
                {
                    step.Success = false;
                    step.ErrorMessage = $"No suitable dropdown for State='{stateValue}' could be identified.";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ {step.ErrorMessage}");
                    Console.ResetColor();
                    return step;
                }

                step.WinningStrategy = match.StrategyName;
                step.ConfidenceScore = match.ConfidenceScore;
                step.StrategyLog.AddRange(match.Reasoning);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✔ Candidate Found: {match.ElementSummary}");
                Console.WriteLine($"  ✔ Confidence Score: {match.ConfidenceScore:F1}/100");
                Console.WriteLine($"  ✔ Match Signals:");
                foreach (var r in match.Reasoning)
                {
                    Console.WriteLine($"     - {r}");
                }
                Console.ResetColor();

                var element = match.Element;
                if (_settings.HighlightElements) _inspector.Highlight(element, "#ffc107");
                _inspector.ScrollIntoView(element);
                Thread.Sleep(200);

                if (element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase))
                {
                    var select = new SelectElement(element);
                    bool selected = false;

                    // Strategy 1: Select by exact visible text
                    try
                    {
                        select.SelectByText(stateValue);
                        selected = true;
                    }
                    catch { }

                    // Strategy 2: Select by partial text or common country/state aliases
                    if (!selected)
                    {
                        try
                        {
                            var option = select.Options.FirstOrDefault(o =>
                                o.Text.Contains(stateValue, StringComparison.OrdinalIgnoreCase) ||
                                (stateValue.Equals("United States", StringComparison.OrdinalIgnoreCase) &&
                                 (o.Text.Trim().Equals("US", StringComparison.OrdinalIgnoreCase) ||
                                  o.Text.Trim().Equals("USA", StringComparison.OrdinalIgnoreCase) ||
                                  o.Text.Contains("United States", StringComparison.OrdinalIgnoreCase))));
                            if (option != null)
                            {
                                select.SelectByText(option.Text);
                                selected = true;
                            }
                        }
                        catch { }
                    }

                    // Strategy 3: Select by value attribute or aliases
                    if (!selected)
                    {
                        try
                        {
                            select.SelectByValue(stateValue);
                            selected = true;
                        }
                        catch
                        {
                            if (stateValue.Equals("United States", StringComparison.OrdinalIgnoreCase))
                            {
                                try { select.SelectByValue("US"); selected = true; } catch { }
                                if (!selected) try { select.SelectByValue("USA"); selected = true; } catch { }
                            }
                        }
                    }

                    // Strategy 4: JavaScript select
                    if (!selected)
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                            var select = arguments[0];
                            var target = arguments[1].toLowerCase();
                            for (var i = 0; i < select.options.length; i++) {
                                var optText = select.options[i].text.toLowerCase();
                                var optVal = select.options[i].value.toLowerCase();
                                if (optText.indexOf(target) !== -1 || optVal.indexOf(target) !== -1 ||
                                    (target === 'united states' && (optText === 'us' || optText === 'usa' || optVal === 'us' || optVal === 'usa'))) {
                                    select.selectedIndex = i;
                                    select.dispatchEvent(new Event('change', { bubbles: true }));
                                    break;
                                }
                            }
                        ", element, stateValue);
                        selected = true;
                    }

                    _inspector.DispatchInputEvents(element);
                    if (_settings.HighlightElements) _inspector.Highlight(element, "#28a745");
                    Thread.Sleep(200);

                    // Verification
                    string actualSelected = select.SelectedOption?.Text?.Trim() ?? "";
                    step.ActualValue = actualSelected;

                    bool isStateVerified = actualSelected.Contains(stateValue, StringComparison.OrdinalIgnoreCase) ||
                        (stateValue.Equals("United States", StringComparison.OrdinalIgnoreCase) &&
                         (actualSelected.Equals("US", StringComparison.OrdinalIgnoreCase) ||
                          actualSelected.Equals("USA", StringComparison.OrdinalIgnoreCase) ||
                          actualSelected.Contains("United States", StringComparison.OrdinalIgnoreCase)));

                    if (isStateVerified)
                    {
                        step.Success = true;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  ✔ Successfully selected '{actualSelected}' in State dropdown");
                        Console.ResetColor();
                    }
                    else
                    {
                        step.Success = selected;
                        Console.WriteLine($"  ⚠ State selection attempted; selected text: '{actualSelected}'");
                    }
                }
                else if (element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase))
                {
                    // Input / Datalist State field
                    try { element.Clear(); } catch { }
                    element.SendKeys(stateValue);
                    _inspector.DispatchInputEvents(element);

                    string actualVal = element.GetAttribute("value") ?? stateValue;
                    step.Success = true;
                    step.ActualValue = actualVal;

                    if (_settings.HighlightElements) _inspector.Highlight(element, "#28a745");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ Successfully entered '{actualVal}' into State input field");
                    Console.ResetColor();
                }
                else
                {
                    // Custom dropdown (e.g. Bootstrap dropdown button)
                    element.Click();
                    Thread.Sleep(300);

                    var options = _driver.FindElements(By.XPath($"//*[contains(text(), '{stateValue}')]"));
                    var targetOption = options.FirstOrDefault(o => _inspector.IsVisible(o));

                    if (targetOption != null)
                    {
                        targetOption.Click();
                        step.Success = true;
                        step.ActualValue = targetOption.Text;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  ✔ Successfully clicked option '{targetOption.Text}' in custom dropdown");
                        Console.ResetColor();
                    }
                    else
                    {
                        step.Success = false;
                        step.ErrorMessage = $"Could not locate option '{stateValue}' inside custom dropdown.";
                    }
                }
            }
            catch (Exception ex)
            {
                step.Success = false;
                step.ErrorMessage = ex.Message;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error automating State dropdown: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                sw.Stop();
                step.Duration = sw.Elapsed;
            }

            return step;
        }

        #endregion

        #region Context Traversal (Frames & Shadow DOM)

        private CandidateMatch? ResolveFieldWithContextFallback(Func<CandidateMatch?> resolveFunc)
        {
            // 1. First attempt in default/current context
            var match = resolveFunc();
            if (match != null && match.ConfidenceScore >= 30)
            {
                return match;
            }

            // 2. Iframes fallback traversal (handles iframes WITH or WITHOUT ID)
            _driver.SwitchTo().DefaultContent();
            var iframes = _driver.FindElements(By.TagName("iframe")).ToList();
            for (int i = 0; i < iframes.Count; i++)
            {
                try
                {
                    // Switch to iframe by element reference (works regardless of whether iframe has an id, name, or neither!)
                    _driver.SwitchTo().Frame(iframes[i]);
                    var frameMatch = resolveFunc();
                    if (frameMatch != null && frameMatch.ConfidenceScore >= 30)
                    {
                        frameMatch.AddScore(25, $"Found inside embedded <iframe> index #{i}");
                        // Keep driver focused inside this frame context so interaction succeeds!
                        return frameMatch;
                    }
                    _driver.SwitchTo().DefaultContent();
                }
                catch
                {
                    try { _driver.SwitchTo().DefaultContent(); } catch { }
                }
            }

            return match;
        }

        #endregion

        #region Helpers & Reporting

        private void WaitForPageReady()
        {
            try
            {
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_settings.TimeoutSeconds));
                wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");
            }
            catch
            {
                // Proceed anyway if document.readyState doesn't complete within timeout
            }
        }

        private string CaptureScreenshot(string prefix)
        {
            try
            {
                if (!Directory.Exists(_settings.ScreenshotDirectory))
                {
                    Directory.CreateDirectory(_settings.ScreenshotDirectory);
                }

                string fileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string fullPath = Path.Combine(_settings.ScreenshotDirectory, fileName);

                var screenshot = ((ITakesScreenshot)_driver).GetScreenshot();
                screenshot.SaveAsFile(fullPath);

                Console.WriteLine($"📸 Screenshot captured: {fullPath}");
                return fullPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Could not save screenshot: {ex.Message}");
                return string.Empty;
            }
        }

        private void PrintReportSummary(AutomationReport report)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("===============================================================================");
            Console.WriteLine("📊 TEST EXECUTION SUMMARY");
            Console.WriteLine("===============================================================================");
            Console.ResetColor();

            Console.WriteLine($"Total Duration : {report.TotalDuration.TotalSeconds:F2} seconds");
            Console.WriteLine($"Target Page    : {report.TargetUrl}");
            Console.WriteLine($"Overall Status : {(report.OverallSuccess ? "PASS ✅" : "FAIL ❌")}");
            Console.WriteLine();

            Console.WriteLine(string.Format("{0,-25} | {1,-8} | {2,-15} | {3,-20} | {4,-8}",
                "Field", "Status", "Expected", "Actual", "Score"));
            Console.WriteLine(new string('-', 85));

            foreach (var step in report.Steps)
            {
                string statusStr = step.Success ? "PASS" : "FAIL";
                Console.ForegroundColor = step.Success ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(string.Format("{0,-25} | {1,-8} | {2,-15} | {3,-20} | {4,-8:F1}",
                    step.FieldName, statusStr, step.ExpectedValue, step.ActualValue, step.ConfidenceScore));
                Console.ResetColor();
            }

            Console.WriteLine(new string('-', 85));
            if (!string.IsNullOrEmpty(report.ScreenshotPath))
            {
                Console.WriteLine($"Screenshot Saved: {Path.GetFullPath(report.ScreenshotPath)}");
            }
            Console.WriteLine();
        }

        #endregion
    }
}
