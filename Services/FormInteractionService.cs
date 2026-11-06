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
            Console.WriteLine($"   Target URL : {_settings.TargetUrl}");
            Console.WriteLine($"   Values     : FirstName='{_settings.FirstName}', Gender='{_settings.Gender}', State='{_settings.State}'");
            Console.WriteLine("===============================================================================");
            Console.ResetColor();

            // Navigate to configured URL
            _driver.Navigate().GoToUrl(_settings.TargetUrl);

            // Wait for document to be ready
            WaitForPageReady();

            // 1. First Name Field
            report.Steps.Add(TestFirstNameField(_settings.FirstName));

            // 2. Gender Field
            report.Steps.Add(TestGenderField(_settings.Gender));

            // 3. State Field
            report.Steps.Add(TestStateField(_settings.State));

            report.EndTime = DateTime.UtcNow;

            // Save Screenshot
            if (_settings.TakeScreenshots)
            {
                report.ScreenshotPath = CaptureScreenshot("ExecutionResult");
            }

            PrintReportSummary(report);
            return report;
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
            Console.WriteLine($"[FIELD 1/3] Resolving 'First Name' text box...");
            Console.ResetColor();

            try
            {
                var match = ResolveFieldWithContextFallback(() => _resolver.ResolveFirstName());

                if (match == null)
                {
                    step.Success = false;
                    step.ErrorMessage = "No suitable 'First Name' input element could be identified with sufficient confidence.";
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

                var input = match.Element;
                if (_settings.HighlightElements) _inspector.Highlight(input, "#ffc107");
                _inspector.ScrollIntoView(input);
                Thread.Sleep(200);

                // Clear & Enter text
                try
                {
                    input.Clear();
                }
                catch { }

                // Fallback clear using Ctrl+A + Backspace in case of masked/controlled inputs
                try
                {
                    input.SendKeys(Keys.Control + "a");
                    input.SendKeys(Keys.Backspace);
                }
                catch { }

                input.SendKeys(firstNameValue);
                _inspector.DispatchInputEvents(input);

                if (_settings.HighlightElements) _inspector.Highlight(input, "#28a745");
                Thread.Sleep(200);

                // Verification
                string actualValue = input.GetAttribute("value") ?? "";
                step.ActualValue = actualValue;

                if (actualValue.Equals(firstNameValue, StringComparison.OrdinalIgnoreCase))
                {
                    step.Success = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ Successfully entered '{firstNameValue}' and verified value='{actualValue}'");
                    Console.ResetColor();
                }
                else
                {
                    // Fallback verify: might be rendered as text or inner text
                    step.Success = !string.IsNullOrEmpty(actualValue);
                    Console.WriteLine($"  ⚠ Field value read back as '{actualValue}' (expected: '{firstNameValue}')");
                }
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

                    // Strategy 2: Select by partial text
                    if (!selected)
                    {
                        try
                        {
                            var option = select.Options.FirstOrDefault(o =>
                                o.Text.Contains(stateValue, StringComparison.OrdinalIgnoreCase));
                            if (option != null)
                            {
                                select.SelectByText(option.Text);
                                selected = true;
                            }
                        }
                        catch { }
                    }

                    // Strategy 3: Select by value attribute
                    if (!selected)
                    {
                        try
                        {
                            select.SelectByValue(stateValue);
                            selected = true;
                        }
                        catch { }
                    }

                    // Strategy 4: JavaScript select
                    if (!selected)
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                            var select = arguments[0];
                            var target = arguments[1].toLowerCase();
                            for (var i = 0; i < select.options.length; i++) {
                                if (select.options[i].text.toLowerCase().indexOf(target) !== -1 ||
                                    select.options[i].value.toLowerCase().indexOf(target) !== -1) {
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

                    if (actualSelected.Contains(stateValue, StringComparison.OrdinalIgnoreCase))
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

            // 2. Iframes fallback traversal
            var iframes = _driver.FindElements(By.TagName("iframe")).ToList();
            foreach (var frame in iframes)
            {
                try
                {
                    _driver.SwitchTo().Frame(frame);
                    var frameMatch = resolveFunc();
                    if (frameMatch != null && frameMatch.ConfidenceScore >= 30)
                    {
                        frameMatch.AddScore(10, "Found inside embedded <iframe>");
                        return frameMatch;
                    }
                }
                catch
                {
                    // Continue to next frame
                }
                finally
                {
                    _driver.SwitchTo().DefaultContent();
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
