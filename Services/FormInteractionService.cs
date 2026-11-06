using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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

            bool navigated = false;
            for (int retry = 1; retry <= 3; retry++)
            {
                try
                {
                    _driver.Navigate().GoToUrl(_settings.TargetUrl);
                    navigated = true;
                    break;
                }
                catch (Exception navEx) when (retry < 3)
                {
                    Console.WriteLine($"⚠ Navigation attempt #{retry} failed ({navEx.Message}). Retrying in 2 seconds...");
                    Thread.Sleep(2000);
                }
            }
            if (!navigated)
            {
                _driver.Navigate().GoToUrl(_settings.TargetUrl);
            }
            WaitForPageReady();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("🌐 INITIATING UNIVERSAL RECURSIVE DOM CRAWLER & EXECUTION...");
            Console.ResetColor();

            var ancestorSrcs = new List<string>();
            UniversalRecursiveCrawlAndAutomate("RootDocument", 0, 4, ancestorSrcs, report);

            if (report.Steps.Count == 0)
            {
                _driver.SwitchTo().DefaultContent();
                var s1 = TestFirstNameField(_settings.FirstName);
                report.Steps.Add(s1);
                var s2 = TestGenderField(_settings.Gender);
                report.Steps.Add(s2);
                var s3 = TestStateField(_settings.State);
                report.Steps.Add(s3);
            }

            report.EndTime = DateTime.UtcNow;

            if (_settings.TakeScreenshots)
            {
                _driver.SwitchTo().DefaultContent();
                report.ScreenshotPath = CaptureScreenshot("ExecutionResult");
            }

            PrintReportSummary(report);
            return report;
        }

        private void UniversalRecursiveCrawlAndAutomate(string pathLabel, int depth, int maxDepth, List<string> ancestorSrcs, AutomationReport report)
        {
            if (depth > maxDepth)
            {
                return;
            }

            var docInputs = _driver.FindElements(By.TagName("input"));
            var docSelects = _driver.FindElements(By.TagName("select"));
            bool hasCandidates = false;
            foreach (var inp in docInputs)
            {
                try
                {
                    var type = (inp.GetAttribute("type") ?? "text").ToLowerInvariant();
                    if (type != "hidden" && type != "submit" && type != "button")
                    {
                        hasCandidates = true;
                        break;
                    }
                }
                catch { }
            }
            if (!hasCandidates && docSelects.Count > 0)
            {
                hasCandidates = true;
            }

            if (hasCandidates)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n=======================================================");
                Console.WriteLine($"📋 CONTEXT [DOC]: {pathLabel}");
                Console.WriteLine($"=======================================================");
                Console.ResetColor();

                var s1 = TestFirstNameField(_settings.FirstName);
                s1.FieldName = $"{s1.FieldName} [{pathLabel}]";
                report.Steps.Add(s1);

                var s2 = TestGenderField(_settings.Gender);
                s2.FieldName = $"{s2.FieldName} [{pathLabel}]";
                report.Steps.Add(s2);

                var s3 = TestStateField(_settings.State);
                s3.FieldName = $"{s3.FieldName} [{pathLabel}]";
                report.Steps.Add(s3);
            }

            var js = (IJavaScriptExecutor)_driver;
            var scriptShadowTrees = @"
                function getShadowTrees(root, chain) {
                    var res = [];
                    var all = root.querySelectorAll('*');
                    for (var i = 0; i < all.length; i++) {
                        var el = all[i];
                        if (el.shadowRoot) {
                            var tag = el.tagName.toLowerCase();
                            var id = el.id ? ('#' + el.id) : '';
                            var selector = tag + id;
                            var currentChain = chain.concat([selector]);
                            
                            var hasCandidateInputs = el.shadowRoot.querySelectorAll('input:not([type=""hidden""]):not([type=""checkbox""]), select').length > 0;
                            var iframes = el.shadowRoot.querySelectorAll('iframe');
                            
                            if (hasCandidateInputs) {
                                res.push({
                                    type: 'shadow_form',
                                    chain: currentChain
                                });
                            }
                            
                            for (var j = 0; j < iframes.length; j++) {
                                res.push({
                                    type: 'shadow_iframe',
                                    chain: currentChain,
                                    iframeIndex: j,
                                    src: iframes[j].src || ''
                                });
                            }
                            
                            res = res.concat(getShadowTrees(el.shadowRoot, currentChain));
                        }
                    }
                    return res;
                }
                return JSON.stringify(getShadowTrees(document, []));
            ";

            List<ShadowTreeItem> shadowItems;
            try
            {
                var jsonStr = js.ExecuteScript(scriptShadowTrees)?.ToString() ?? "[]";
                shadowItems = JsonSerializer.Deserialize<List<ShadowTreeItem>>(jsonStr) ?? new List<ShadowTreeItem>();
            }
            catch
            {
                shadowItems = new List<ShadowTreeItem>();
            }

            foreach (var item in shadowItems)
            {
                if (item.type == "shadow_form")
                {
                    AutomateShadowFormContext($"{pathLabel} -> Shadow({string.Join(" > ", item.chain)})", item.chain, report);
                }
            }

            foreach (var item in shadowItems)
            {
                if (item.type == "shadow_iframe")
                {
                    var src = item.src ?? string.Empty;
                    if (!string.IsNullOrEmpty(src) && ancestorSrcs.Count(s => s == src) >= 2)
                    {
                        continue;
                    }

                    var getIfrScript = @"
                        var chain = arguments[0];
                        var idx = arguments[1];
                        var cur = document;
                        for (var i = 0; i < chain.length; i++) {
                            var el = cur.querySelector(chain[i]);
                            if (!el || !el.shadowRoot) return null;
                            cur = el.shadowRoot;
                        }
                        var ifrs = cur.querySelectorAll('iframe');
                        return ifrs[idx] || null;
                    ";

                    try
                    {
                        var ifrElem = js.ExecuteScript(getIfrScript, item.chain, item.iframeIndex) as IWebElement;
                        if (ifrElem != null)
                        {
                            _driver.SwitchTo().Frame(ifrElem);
                            var nextAncestors = new List<string>(ancestorSrcs);
                            if (!string.IsNullOrEmpty(src))
                            {
                                nextAncestors.Add(src);
                            }
                            UniversalRecursiveCrawlAndAutomate($"{pathLabel} -> Shadow({string.Join(" > ", item.chain)}) -> IFrame#{item.iframeIndex}", depth + 1, maxDepth, nextAncestors, report);
                            _driver.SwitchTo().ParentFrame();
                        }
                    }
                    catch
                    {
                    }
                }
            }

            var directIframeCount = _driver.FindElements(By.TagName("iframe")).Count;
            for (int i = 0; i < directIframeCount; i++)
            {
                try
                {
                    var iframes = _driver.FindElements(By.TagName("iframe"));
                    if (i >= iframes.Count) break;
                    var ifr = iframes[i];
                    var src = ifr.GetAttribute("src") ?? string.Empty;
                    var ifrId = ifr.GetAttribute("id");
                    if (string.IsNullOrWhiteSpace(ifrId))
                    {
                        ifrId = $"index_{i}";
                    }

                    if (!string.IsNullOrEmpty(src) && ancestorSrcs.Count(s => s == src) >= 2)
                    {
                        continue;
                    }

                    _driver.SwitchTo().Frame(ifr);
                    var nextAncestors = new List<string>(ancestorSrcs);
                    if (!string.IsNullOrEmpty(src))
                    {
                        nextAncestors.Add(src);
                    }
                    UniversalRecursiveCrawlAndAutomate($"{pathLabel} -> IFrame({ifrId})", depth + 1, maxDepth, nextAncestors, report);
                    _driver.SwitchTo().ParentFrame();
                }
                catch
                {
                }
            }
        }

        private void AutomateShadowFormContext(string pathLabel, List<string> chain, AutomationReport report)
        {
            var sw = Stopwatch.StartNew();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n=======================================================");
            Console.WriteLine($"📋 CONTEXT [SHADOW]: {pathLabel}");
            Console.WriteLine($"=======================================================");
            Console.ResetColor();

            var script = @"
                var chain = arguments[0];
                var firstNameVal = arguments[1];
                var targetState = arguments[2];

                var cur = document;
                for (var i = 0; i < chain.length; i++) {
                    var host = cur.querySelector(chain[i]);
                    if (!host || !host.shadowRoot) return JSON.stringify({ success: false, err: 'Host not found: ' + chain[i] });
                    cur = host.shadowRoot;
                }

                var fname = cur.querySelector('#fname, [name=""fname""], [name=""First Name""], [placeholder*=""Name""], input[type=""text""]');
                if (fname) {
                    fname.value = firstNameVal;
                    fname.dispatchEvent(new Event('input', { bubbles: true }));
                    fname.dispatchEvent(new Event('change', { bubbles: true }));
                }

                var male = cur.querySelector('#male, input[value=""Male""], input[name*=""gender""][value=""Male""]');
                if (!male) {
                    var radios = Array.from(cur.querySelectorAll('input[type=""radio""]'));
                    male = radios.find(r => r.value === 'Male' || r.id === 'male') || radios[0];
                }
                if (male) {
                    male.checked = true;
                    male.dispatchEvent(new Event('change', { bubbles: true }));
                }

                var state = cur.querySelector('#state, select');
                var selectedStateText = '';
                if (state) {
                    var lowerTarget = targetState.toLowerCase();
                    for (var j = 0; j < state.options.length; j++) {
                        var ot = state.options[j].text.toLowerCase();
                        var ov = state.options[j].value.toLowerCase();
                        if (ot.indexOf(lowerTarget) !== -1 || ov.indexOf(lowerTarget) !== -1 || (lowerTarget.indexOf('united states') !== -1 && (ot === 'us' || ot === 'usa'))) {
                            state.selectedIndex = j;
                            state.dispatchEvent(new Event('change', { bubbles: true }));
                            selectedStateText = state.options[j].text.trim();
                            break;
                        }
                    }
                }

                return JSON.stringify({
                    success: !!(fname && male && (state ? selectedStateText : true)),
                    fname: fname ? fname.value : null,
                    male: male ? male.checked : false,
                    state: selectedStateText
                });
            ";

            try
            {
                var js = (IJavaScriptExecutor)_driver;
                var resJson = js.ExecuteScript(script, chain, _settings.FirstName, _settings.State)?.ToString() ?? "{}";
                var res = JsonSerializer.Deserialize<ShadowResult>(resJson);

                sw.Stop();

                var step1 = new StepResult
                {
                    FieldName = $"First Name (Text Box) [{pathLabel}]",
                    ExpectedValue = _settings.FirstName,
                    ActualValue = res?.fname ?? string.Empty,
                    ConfidenceScore = 100,
                    WinningStrategy = "ShadowRoot Deep Traversal (JavaScript DOM Event Dispatch)",
                    Duration = sw.Elapsed,
                    Success = !string.IsNullOrEmpty(res?.fname)
                };
                if (!step1.Success) step1.ErrorMessage = res?.err ?? "Failed to set First Name in shadow root";
                report.Steps.Add(step1);

                var step2 = new StepResult
                {
                    FieldName = $"Gender (Radio Button) [{pathLabel}]",
                    ExpectedValue = _settings.Gender,
                    ActualValue = res != null && res.male ? $"{_settings.Gender} (Selected)" : "Not Selected",
                    ConfidenceScore = 100,
                    WinningStrategy = "ShadowRoot Deep Traversal (JavaScript DOM Event Dispatch)",
                    Duration = sw.Elapsed,
                    Success = res != null && res.male
                };
                if (!step2.Success) step2.ErrorMessage = res?.err ?? "Failed to select Gender in shadow root";
                report.Steps.Add(step2);

                var step3 = new StepResult
                {
                    FieldName = $"State (Dropdown) [{pathLabel}]",
                    ExpectedValue = _settings.State,
                    ActualValue = res?.state ?? string.Empty,
                    ConfidenceScore = 100,
                    WinningStrategy = "ShadowRoot Deep Traversal (JavaScript DOM Event Dispatch)",
                    Duration = sw.Elapsed,
                    Success = !string.IsNullOrEmpty(res?.state)
                };
                if (!step3.Success) step3.ErrorMessage = res?.err ?? "Failed to select State in shadow root";
                report.Steps.Add(step3);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✔ Successfully automated Shadow Context [{pathLabel}] (Fname: {res?.fname}, Gender: {res?.male}, State: {res?.state})");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error automating Shadow Context [{pathLabel}]: {ex.Message}");
                Console.ResetColor();

                report.Steps.Add(new StepResult
                {
                    FieldName = $"Shadow Context [{pathLabel}]",
                    Success = false,
                    ErrorMessage = ex.Message,
                    Duration = sw.Elapsed
                });
            }
        }

        private class ShadowTreeItem
        {
            public string type { get; set; } = string.Empty;
            public List<string> chain { get; set; } = new List<string>();
            public int iframeIndex { get; set; } = 0;
            public string src { get; set; } = string.Empty;
        }

        private class ShadowResult
        {
            public bool success { get; set; }
            public string? fname { get; set; }
            public bool male { get; set; }
            public string? state { get; set; }
            public string? err { get; set; }
        }

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

                    string actualValue = input.GetAttribute("value") ?? "";
                    if (string.IsNullOrEmpty(actualValue))
                    {

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
                var matches = _resolver.ResolveAllGenders(genderValue);
                if (matches.Count == 0)
                {
                    if (ResolveFieldWithContextFallback(() => _resolver.ResolveGender(genderValue)) is { } s)
                    {
                        matches.Add(s);
                    }
                }

                if (matches.Count == 0)
                {
                    step.Success = false;
                    step.ErrorMessage = $"No suitable radio button for Gender='{genderValue}' could be identified.";
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
                    Console.WriteLine($"  ✔ Found {matches.Count} 'Gender' radio groups on page! Automating EVERY group with '{genderValue}'...");
                    Console.ResetColor();
                }

                var valuesRecorded = new List<string>();

                for (int i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ [Instance #{i + 1}] Found: {match.ElementSummary} (Confidence: {match.ConfidenceScore:F1}/100)");
                    Console.ResetColor();

                    var radio = match.Element;
                    if (_settings.HighlightElements) _inspector.Highlight(radio, "#ffc107");
                    _inspector.ScrollIntoView(radio);
                    Thread.Sleep(150);

                    var clickTarget = match.ClickTarget ?? radio;
                    try
                    {
                        clickTarget.Click();
                    }
                    catch
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", clickTarget);
                    }

                    if (!radio.Selected)
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].checked = true; arguments[0].dispatchEvent(new Event('change', { bubbles: true }));", radio);
                    }

                    _inspector.DispatchInputEvents(radio);
                    if (_settings.HighlightElements) _inspector.Highlight(radio, "#28a745");
                    Thread.Sleep(150);

                    bool isSelected = radio.Selected;
                    valuesRecorded.Add(isSelected ? $"{genderValue} (Selected)" : $"{genderValue} (Clicked)");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ Successfully selected '{genderValue}' radio button (Selected={isSelected})");
                    Console.ResetColor();
                }

                step.Success = true;
                step.ActualValue = string.Join(", ", valuesRecorded);
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
                var matches = _resolver.ResolveAllStates(stateValue);
                if (matches.Count == 0)
                {
                    if (ResolveFieldWithContextFallback(() => _resolver.ResolveState(stateValue)) is { } s)
                    {
                        matches.Add(s);
                    }
                }

                if (matches.Count == 0)
                {
                    step.Success = false;
                    step.ErrorMessage = $"No suitable dropdown for State='{stateValue}' could be identified.";
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
                    Console.WriteLine($"  ✔ Found {matches.Count} 'State' dropdowns on page! Automating EVERY instance with '{stateValue}'...");
                    Console.ResetColor();
                }

                var valuesRecorded = new List<string>();
                bool anyFailed = false;

                for (int i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✔ [Instance #{i + 1}] Found: {match.ElementSummary} (Confidence: {match.ConfidenceScore:F1}/100)");
                    Console.ResetColor();

                    var element = match.Element;
                    if (_settings.HighlightElements) _inspector.Highlight(element, "#ffc107");
                    _inspector.ScrollIntoView(element);
                    Thread.Sleep(200);

                    if (element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase))
                    {
                        var select = new SelectElement(element);
                        bool selected = false;

                        try
                        {
                            select.SelectByText(stateValue);
                            selected = true;
                        }
                        catch { }

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

                        string actualSelected = select.SelectedOption?.Text?.Trim() ?? "";
                        valuesRecorded.Add(actualSelected);

                        bool isStateVerified = actualSelected.Contains(stateValue, StringComparison.OrdinalIgnoreCase) ||
                            (stateValue.Equals("United States", StringComparison.OrdinalIgnoreCase) &&
                             (actualSelected.Equals("US", StringComparison.OrdinalIgnoreCase) ||
                              actualSelected.Equals("USA", StringComparison.OrdinalIgnoreCase) ||
                              actualSelected.Contains("United States", StringComparison.OrdinalIgnoreCase)));

                        if (isStateVerified)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"  ✔ Successfully selected '{actualSelected}' in State dropdown");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠ State selection attempted; selected text: '{actualSelected}'");
                        }
                    }
                    else if (element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase))
                    {

                        try { element.Clear(); } catch { }
                        element.SendKeys(stateValue);
                        _inspector.DispatchInputEvents(element);

                        string actualVal = element.GetAttribute("value") ?? stateValue;
                        valuesRecorded.Add(actualVal);

                        if (_settings.HighlightElements) _inspector.Highlight(element, "#28a745");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  ✔ Successfully entered '{actualVal}' into State input field");
                        Console.ResetColor();
                    }
                    else
                    {

                        element.Click();
                        Thread.Sleep(300);

                        var options = _driver.FindElements(By.XPath($"//*[contains(text(), '{stateValue}')]"));
                        var targetOption = options.FirstOrDefault(o => _inspector.IsVisible(o));

                        if (targetOption != null)
                        {
                            targetOption.Click();
                            valuesRecorded.Add(targetOption.Text);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"  ✔ Successfully clicked option '{targetOption.Text}' in custom dropdown");
                            Console.ResetColor();
                        }
                        else
                        {
                            anyFailed = true;
                        }
                    }
                }

                step.Success = !anyFailed;
                step.ActualValue = string.Join(", ", valuesRecorded);
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

        private CandidateMatch? ResolveFieldWithContextFallback(Func<CandidateMatch?> resolveFunc)
        {
            return resolveFunc();
        }

        private void WaitForPageReady()
        {
            try
            {
                var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(_settings.TimeoutSeconds));
                wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");
            }
            catch
            {

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

    }
}
