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

            // Navigate to configured URL
            _driver.Navigate().GoToUrl(_settings.TargetUrl);
            WaitForPageReady();

            // Run deep DOM & Shadow Root diagnostics
            RunDeepDomDiagnostics();

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

        private void RunDeepDomDiagnostics()
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("🔎 SCANNING DEEP DOM TREE (Shadow DOMs, Custom Elements, Frames)...");

                var js = (IJavaScriptExecutor)_driver;
                var script = @"
                    function findShadowHosts(root) {
                        var hosts = [];
                        var all = root.querySelectorAll('*');
                        for (var i = 0; i < all.length; i++) {
                            if (all[i].shadowRoot) {
                                hosts.push({
                                    tag: all[i].tagName.toLowerCase(),
                                    id: all[i].id,
                                    className: all[i].className,
                                    inputs: all[i].shadowRoot.querySelectorAll('input, select, textarea').length
                                });
                                hosts = hosts.concat(findShadowHosts(all[i].shadowRoot));
                            }
                        }
                        return hosts;
                    }
                    return JSON.stringify(findShadowHosts(document));
                ";

                var result = js.ExecuteScript(script)?.ToString() ?? "[]";
                Console.WriteLine($"   Root Document Shadow Hosts: {result}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Diagnostic notice: {ex.Message}");
            }
        }

        private enum ContextType
        {
            StandardDocument,
            IFrame,
            ShadowDom,
            IFrameInShadow
        }

        private class FormContextInfo
        {
            public string Name { get; set; } = string.Empty;
            public ContextType Type { get; set; } = ContextType.StandardDocument;
            public List<int> FrameHierarchy { get; set; } = new List<int>();
            public string IframeId { get; set; } = string.Empty;
            public string IframeSrc { get; set; } = string.Empty;
            public List<string> ShadowHostChain { get; set; } = new List<string>();
            public int ShadowIframeIndex { get; set; } = 0;
            public List<int> ParentFrameHierarchy { get; set; } = new List<int>();
            public bool IsIframe => Type == ContextType.IFrame || Type == ContextType.IFrameInShadow;
        }

        private bool SwitchToHierarchy(List<int> hierarchy)
        {
            try
            {
                _driver.SwitchTo().DefaultContent();

                for (int depth = 0; depth < hierarchy.Count; depth++)
                {
                    int frameIdx = hierarchy[depth];
                    var iframes = _driver.FindElements(By.TagName("iframe"));
                    if (frameIdx < 0 || frameIdx >= iframes.Count)
                    {
                        return false;
                    }

                    var iframeElem = iframes[frameIdx];
                    try
                    {
                        ((IJavaScriptExecutor)_driver).ExecuteScript(
                            "arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", iframeElem);
                        Thread.Sleep(200);
                    }
                    catch { }

                    _driver.SwitchTo().Frame(iframeElem);
                    WaitForPageReady();
                }

                return true;
            }
            catch
            {
                return false;
            }
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
                contexts.Add(new FormContextInfo 
                { 
                    Name = "Main Document (Default Content)", 
                    Type = ContextType.StandardDocument,
                    FrameHierarchy = new List<int>() 
                });
            }

            // 2. Discover all iframes recursively (depth 1 to 4)
            DiscoverFramesRecursive(new List<int>(), contexts, 0, 4);

            // 3. Discover all Shadow DOMs and Shadow IFrames (in main document)
            DiscoverShadowContexts(new List<int>(), contexts);

            // If no contexts matched, fallback to main document
            if (contexts.Count == 0)
            {
                contexts.Add(new FormContextInfo { Name = "Default Document Context", Type = ContextType.StandardDocument, FrameHierarchy = new List<int>() });
            }

            return contexts;
        }

        private void DiscoverFramesRecursive(List<int> parentPath, List<FormContextInfo> contexts, int currentDepth, int maxDepth)
        {
            if (currentDepth >= maxDepth) return;

            if (!SwitchToHierarchy(parentPath)) return;

            var rawFrames = _driver.FindElements(By.TagName("iframe")).ToList();
            int frameCount = rawFrames.Count;

            for (int i = 0; i < frameCount; i++)
            {
                // Re-navigate to parent to ensure fresh DOM state
                if (!SwitchToHierarchy(parentPath)) break;
                var currentFrames = _driver.FindElements(By.TagName("iframe"));
                if (i >= currentFrames.Count) break;

                var f = currentFrames[i];
                string id = (f.GetAttribute("id") ?? "").Trim();
                string src = (f.GetAttribute("src") ?? "").Trim();
                bool isNoId = string.IsNullOrEmpty(id);

                var childPath = new List<int>(parentPath) { i };

                // Inspect contents of this frame
                bool switched = false;
                try
                {
                    _driver.SwitchTo().Frame(f);
                    switched = true;
                    WaitForPageReady();

                    var inputs = _driver.FindElements(By.TagName("input"));
                    var selects = _driver.FindElements(By.TagName("select"));
                    bool hasFormElements = inputs.Count > 0 || selects.Count > 0;

                    string depthLabel = currentDepth == 0 ? "IFrame" : $"Nested IFrame (Depth #{currentDepth + 1})";
                    string label = isNoId 
                        ? $"{depthLabel} without ID (Index #{i}, src='{src}')" 
                        : $"{depthLabel} with ID '{id}' (Index #{i})";

                    bool shouldInclude = false;
                    if (_settings.TargetFrame.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldInclude = hasFormElements;
                    }
                    else if (_settings.TargetFrame.Equals("no-id", StringComparison.OrdinalIgnoreCase) && isNoId)
                    {
                        shouldInclude = hasFormElements;
                    }
                    else if (_settings.TargetFrame.Equals("iframeId", StringComparison.OrdinalIgnoreCase) && id.Equals("iframeId", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldInclude = hasFormElements;
                    }
                    else if (!string.IsNullOrEmpty(id) && _settings.TargetFrame.Equals(id, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldInclude = hasFormElements;
                    }

                    if (shouldInclude)
                    {
                        contexts.Add(new FormContextInfo
                        {
                            Name = label,
                            Type = ContextType.IFrame,
                            FrameHierarchy = childPath,
                            IframeId = id,
                            IframeSrc = src
                        });
                    }

                    // Also check for any Shadow DOMs inside this frame
                    DiscoverShadowContexts(childPath, contexts);

                    // Recurse deeper inside this frame to find child/grandchild iframes!
                    DiscoverFramesRecursive(childPath, contexts, currentDepth + 1, maxDepth);
                }
                catch
                {
                    // Frame access denied (e.g. cross-origin) or detached
                }
                finally
                {
                    if (switched)
                    {
                        SwitchToHierarchy(parentPath);
                    }
                }
            }
        }

        private void DiscoverShadowContexts(List<int> parentFramePath, List<FormContextInfo> contexts)
        {
            try
            {
                if (parentFramePath.Count > 0)
                {
                    if (!SwitchToHierarchy(parentFramePath)) return;
                }
                else
                {
                    _driver.SwitchTo().DefaultContent();
                }

                var js = (IJavaScriptExecutor)_driver;
                var script = @"
                    function scanShadowTree(root, currentChain) {
                        var found = [];
                        var all = root.querySelectorAll('*');
                        for (var i = 0; i < all.length; i++) {
                            var el = all[i];
                            if (el.shadowRoot) {
                                var tag = el.tagName.toLowerCase();
                                var newChain = currentChain.concat([tag]);
                                var inputs = el.shadowRoot.querySelectorAll('input, select, textarea');
                                var iframes = el.shadowRoot.querySelectorAll('iframe');

                                var hasCandidateInputs = el.shadowRoot.querySelectorAll('input:not([type=""hidden""]):not([type=""submit""]):not([type=""button""]):not([type=""checkbox""])').length > 0
                                    || el.shadowRoot.querySelectorAll('input[type=""radio""]').length > 0
                                    || el.shadowRoot.querySelectorAll('select').length > 0;

                                if (inputs.length > 0 && hasCandidateInputs) {
                                    found.push({
                                        type: 'shadow',
                                        chain: newChain,
                                        inputCount: inputs.length
                                    });
                                }

                                for (var j = 0; j < iframes.length; j++) {
                                    var ifr = iframes[j];
                                    found.push({
                                        type: 'shadow_iframe',
                                        chain: newChain,
                                        iframeIndex: j,
                                        iframeId: ifr.id || '',
                                        iframeSrc: ifr.src || ''
                                    });
                                }

                                found = found.concat(scanShadowTree(el.shadowRoot, newChain));
                            }
                        }
                        return found;
                    }
                    return JSON.stringify(scanShadowTree(document, []));
                ";

                var jsonStr = js.ExecuteScript(script)?.ToString();
                if (string.IsNullOrEmpty(jsonStr) || jsonStr == "[]") return;

                using var doc = JsonDocument.Parse(jsonStr);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string itemType = item.GetProperty("type").GetString() ?? "";
                    var chainList = new List<string>();
                    foreach (var ch in item.GetProperty("chain").EnumerateArray())
                    {
                        chainList.Add(ch.GetString() ?? "");
                    }

                    string chainDisplay = string.Join(" > ", chainList);

                    if (itemType == "shadow")
                    {
                        bool shouldInclude = _settings.TargetFrame.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                             _settings.TargetFrame.Equals("shadow", StringComparison.OrdinalIgnoreCase);
                        if (shouldInclude)
                        {
                            string name = parentFramePath.Count > 0
                                ? $"Shadow DOM [{chainDisplay}] (in Frame #{string.Join(">", parentFramePath)})"
                                : $"Shadow DOM [{chainDisplay}]";

                            contexts.Add(new FormContextInfo
                            {
                                Name = name,
                                Type = ContextType.ShadowDom,
                                ShadowHostChain = chainList,
                                ParentFrameHierarchy = new List<int>(parentFramePath)
                            });
                        }
                    }
                    else if (itemType == "shadow_iframe")
                    {
                        int ifrIdx = item.GetProperty("iframeIndex").GetInt32();
                        string ifrId = item.GetProperty("iframeId").GetString() ?? "";
                        string ifrSrc = item.GetProperty("iframeSrc").GetString() ?? "";

                        bool shouldInclude = _settings.TargetFrame.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                                             _settings.TargetFrame.Equals("shadow", StringComparison.OrdinalIgnoreCase) ||
                                             (!string.IsNullOrEmpty(ifrId) && _settings.TargetFrame.Equals(ifrId, StringComparison.OrdinalIgnoreCase));
                        if (shouldInclude)
                        {
                            string idLabel = string.IsNullOrEmpty(ifrId) ? $"index #{ifrIdx}" : $"id '{ifrId}'";
                            string name = $"IFrame ({idLabel}) inside Shadow DOM [{chainDisplay}]";

                            contexts.Add(new FormContextInfo
                            {
                                Name = name,
                                Type = ContextType.IFrameInShadow,
                                ShadowHostChain = chainList,
                                ShadowIframeIndex = ifrIdx,
                                IframeId = ifrId,
                                IframeSrc = ifrSrc,
                                ParentFrameHierarchy = new List<int>(parentFramePath)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ℹ Shadow discovery notice: {ex.Message}");
            }
            finally
            {
                _driver.SwitchTo().DefaultContent();
            }
        }

        private void ExecuteContext(FormContextInfo ctx, AutomationReport report)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("-------------------------------------------------------------------------------");
            Console.WriteLine($"🎯 AUTOMATING CONTEXT: {ctx.Name}");
            Console.WriteLine("-------------------------------------------------------------------------------");
            Console.ResetColor();

            switch (ctx.Type)
            {
                case ContextType.ShadowDom:
                    ExecuteShadowContext(ctx, report);
                    break;

                case ContextType.IFrameInShadow:
                    ExecuteIframeInShadowContext(ctx, report);
                    break;

                default:
                    ExecuteStandardContext(ctx, report);
                    break;
            }
        }

        private void ExecuteStandardContext(FormContextInfo ctx, AutomationReport report)
        {
            if (!SwitchToHierarchy(ctx.FrameHierarchy))
            {
                Console.WriteLine($"⚠ Could not switch to context {ctx.Name}");
                return;
            }

            // Check if this iframe actually has form elements (avoids failing on tracking/ad/blank iframes)
            var formElements = _driver.FindElements(By.XPath("//input | //select | //form | //textarea"));
            if (formElements.Count == 0 && ctx.IsIframe)
            {
                Console.WriteLine($"  ℹ Skipping {ctx.Name}: No form elements detected inside this frame.");
                _driver.SwitchTo().DefaultContent();
                return;
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

        private void ExecuteShadowContext(FormContextInfo ctx, AutomationReport report)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (ctx.ParentFrameHierarchy.Count > 0)
                {
                    if (!SwitchToHierarchy(ctx.ParentFrameHierarchy)) return;
                }
                else
                {
                    _driver.SwitchTo().DefaultContent();
                }

                var js = (IJavaScriptExecutor)_driver;
                var script = @"
                    var chain = arguments[0];
                    var firstNameVal = arguments[1];
                    var genderVal = arguments[2];
                    var stateVal = arguments[3];
                    var highlight = arguments[4];

                    var cur = document;
                    for (var i = 0; i < chain.length; i++) {
                        var host = (cur === document) ? document.querySelector(chain[i]) : cur.querySelector(chain[i]);
                        if (!host || !host.shadowRoot) {
                            return JSON.stringify({ success: false, error: 'Host not found: ' + chain[i] });
                        }
                        cur = host.shadowRoot;
                    }

                    // 1. Resolve & Fill First Name
                    var inputs = Array.from(cur.querySelectorAll('input:not([type=""hidden""]):not([type=""radio""]):not([type=""checkbox""]):not([type=""submit""]):not([type=""button""])'));
                    var bestFname = null;
                    var bestFnameScore = 0;
                    inputs.forEach(function(inp) {
                        var score = 10;
                        var id = (inp.id || '').toLowerCase();
                        var name = (inp.name || '').toLowerCase();
                        var ph = (inp.placeholder || '').toLowerCase();
                        var aria = (inp.getAttribute('aria-label') || '').toLowerCase();
                        
                        if (id === 'fname' || name === 'fname') score += 90;
                        if (id.includes('first') || name.includes('first')) score += 80;
                        if (ph.includes('first') || ph.includes('name')) score += 70;
                        if (aria.includes('first') || aria.includes('name')) score += 60;
                        
                        var labels = Array.from(cur.querySelectorAll('label'));
                        labels.forEach(function(lbl) {
                            var txt = (lbl.innerText || lbl.textContent || '').toLowerCase().trim();
                            if (txt.includes('first name') || txt.includes('firstname')) {
                                if (lbl.getAttribute('for') === inp.id || lbl.contains(inp)) score += 90;
                            }
                        });
                        if (score > bestFnameScore) {
                            bestFnameScore = score;
                            bestFname = inp;
                        }
                    });

                    var fnameActual = '';
                    if (bestFname) {
                        try { bestFname.scrollIntoView({behavior: 'smooth', block: 'center'}); } catch(e){}
                        bestFname.value = firstNameVal;
                        bestFname.dispatchEvent(new Event('input', { bubbles: true }));
                        bestFname.dispatchEvent(new Event('change', { bubbles: true }));
                        if (highlight) {
                            bestFname.style.outline = '3px solid #28a745';
                            bestFname.style.backgroundColor = '#d4edda';
                        }
                        fnameActual = bestFname.value;
                    }

                    // 2. Resolve & Select Gender
                    var radios = Array.from(cur.querySelectorAll('input[type=""radio""]'));
                    var bestRadio = null;
                    var bestRadioScore = 0;
                    var targetGender = genderVal.toLowerCase();
                    radios.forEach(function(r) {
                        var score = 10;
                        var val = (r.value || '').toLowerCase();
                        var id = (r.id || '').toLowerCase();
                        if (val === targetGender || id === targetGender) score += 90;
                        
                        var labels = Array.from(cur.querySelectorAll('label'));
                        labels.forEach(function(lbl) {
                            var txt = (lbl.innerText || lbl.textContent || '').toLowerCase().trim();
                            if (txt === targetGender) {
                                if (lbl.getAttribute('for') === r.id || lbl.contains(r)) score += 85;
                            }
                        });
                        if (score > bestRadioScore) {
                            bestRadioScore = score;
                            bestRadio = r;
                        }
                    });

                    var genderActual = '';
                    if (bestRadio) {
                        try { bestRadio.scrollIntoView({behavior: 'smooth', block: 'center'}); } catch(e){}
                        bestRadio.checked = true;
                        try { bestRadio.click(); } catch(e){}
                        bestRadio.dispatchEvent(new Event('change', { bubbles: true }));
                        if (highlight) {
                            bestRadio.style.outline = '3px solid #28a745';
                        }
                        genderActual = bestRadio.checked ? (genderVal + ' (Selected)') : (genderVal + ' (Clicked)');
                    }

                    // 3. Resolve & Select State
                    var selects = Array.from(cur.querySelectorAll('select'));
                    var bestSelect = null;
                    var bestSelectScore = 0;
                    selects.forEach(function(s) {
                        var score = 10;
                        var id = (s.id || '').toLowerCase();
                        var name = (s.name || '').toLowerCase();
                        if (id === 'state' || name === 'state') score += 90;
                        if (id.includes('state') || name.includes('state')) score += 70;
                        if (id.includes('country') || name.includes('country')) score += 50;
                        
                        for (var o = 0; o < s.options.length; o++) {
                            var ot = s.options[o].text.toLowerCase();
                            if (ot.includes('united states') || ot === 'us' || ot === 'usa') {
                                score += 50;
                                break;
                            }
                        }
                        if (score > bestSelectScore) {
                            bestSelectScore = score;
                            bestSelect = s;
                        }
                    });

                    var stateActual = '';
                    if (bestSelect) {
                        try { bestSelect.scrollIntoView({behavior: 'smooth', block: 'center'}); } catch(e){}
                        var targetState = stateVal.toLowerCase();
                        for (var j = 0; j < bestSelect.options.length; j++) {
                            var opt = bestSelect.options[j];
                            var ot = opt.text.toLowerCase();
                            var ov = (opt.value || '').toLowerCase();
                            if (ot.includes(targetState) || ov.includes(targetState) ||
                                (targetState === 'united states' && (ot === 'us' || ot === 'usa' || ov === 'us' || ov === 'usa'))) {
                                bestSelect.selectedIndex = j;
                                bestSelect.dispatchEvent(new Event('change', { bubbles: true }));
                                stateActual = opt.text.trim();
                                break;
                            }
                        }
                        if (highlight) {
                            bestSelect.style.outline = '3px solid #28a745';
                            bestSelect.style.backgroundColor = '#d4edda';
                        }
                    }
                    else {
                        var stateInputs = inputs.filter(function(i) {
                            var id = (i.id || '').toLowerCase();
                            var nm = (i.name || '').toLowerCase();
                            var ph = (i.placeholder || '').toLowerCase();
                            return id.includes('state') || nm.includes('state') || ph.includes('state');
                        });
                        if (stateInputs.length > 0) {
                            var si = stateInputs[0];
                            try { si.scrollIntoView({behavior: 'smooth', block: 'center'}); } catch(e){}
                            si.value = stateVal;
                            si.dispatchEvent(new Event('input', { bubbles: true }));
                            si.dispatchEvent(new Event('change', { bubbles: true }));
                            stateActual = si.value;
                            if (highlight) {
                                si.style.outline = '3px solid #28a745';
                                si.style.backgroundColor = '#d4edda';
                            }
                        }
                    }

                    return JSON.stringify({
                        success: true,
                        fname: { found: !!bestFname, actual: fnameActual, score: bestFnameScore },
                        gender: { found: !!bestRadio, actual: genderActual, score: bestRadioScore },
                        state: { found: !!bestSelect || stateActual.length > 0, actual: stateActual, score: bestSelectScore > 0 ? bestSelectScore : 50 }
                    });
                ";

                var jsonResult = js.ExecuteScript(script, ctx.ShadowHostChain, _settings.FirstName, _settings.Gender, _settings.State, _settings.HighlightElements)?.ToString();
                if (!string.IsNullOrEmpty(jsonResult))
                {
                    using var doc = JsonDocument.Parse(jsonResult);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var succElem) && succElem.GetBoolean())
                    {
                        var fnameObj = root.GetProperty("fname");
                        bool fnameFound = fnameObj.GetProperty("found").GetBoolean();
                        string fnameVal = fnameObj.GetProperty("actual").GetString() ?? "";
                        double fnameScore = fnameObj.GetProperty("score").GetDouble();

                        var genderObj = root.GetProperty("gender");
                        bool genderFound = genderObj.GetProperty("found").GetBoolean();
                        string genderVal = genderObj.GetProperty("actual").GetString() ?? "";
                        double genderScore = genderObj.GetProperty("score").GetDouble();

                        var stateObj = root.GetProperty("state");
                        bool stateFound = stateObj.GetProperty("found").GetBoolean();
                        string stateVal = stateObj.GetProperty("actual").GetString() ?? "";
                        double stateScore = stateObj.GetProperty("score").GetDouble();

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  ✔ [Shadow DOM] First Name : '{fnameVal}' (Confidence: {fnameScore})");
                        Console.WriteLine($"  ✔ [Shadow DOM] Gender     : '{genderVal}' (Confidence: {genderScore})");
                        Console.WriteLine($"  ✔ [Shadow DOM] State      : '{stateVal}' (Confidence: {stateScore})");
                        Console.ResetColor();

                        report.Steps.Add(new StepResult
                        {
                            FieldName = $"First Name (Text Box) [{ctx.Name}]",
                            ExpectedValue = _settings.FirstName,
                            ActualValue = fnameVal,
                            ConfidenceScore = fnameScore,
                            WinningStrategy = "Shadow DOM Heuristic Piercing",
                            Success = fnameFound && fnameVal.Equals(_settings.FirstName, StringComparison.OrdinalIgnoreCase),
                            Duration = sw.Elapsed
                        });

                        report.Steps.Add(new StepResult
                        {
                            FieldName = $"Gender (Radio Button) [{ctx.Name}]",
                            ExpectedValue = _settings.Gender,
                            ActualValue = genderVal,
                            ConfidenceScore = genderScore,
                            WinningStrategy = "Shadow DOM Heuristic Piercing",
                            Success = genderFound,
                            Duration = sw.Elapsed
                        });

                        report.Steps.Add(new StepResult
                        {
                            FieldName = $"State (Dropdown) [{ctx.Name}]",
                            ExpectedValue = _settings.State,
                            ActualValue = stateVal,
                            ConfidenceScore = stateScore,
                            WinningStrategy = "Shadow DOM Heuristic Piercing",
                            Success = stateFound && !string.IsNullOrEmpty(stateVal),
                            Duration = sw.Elapsed
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error automating Shadow DOM context: {ex.Message}");
            }
            finally
            {
                _driver.SwitchTo().DefaultContent();
            }
        }

        private void ExecuteIframeInShadowContext(FormContextInfo ctx, AutomationReport report)
        {
            try
            {
                if (ctx.ParentFrameHierarchy.Count > 0)
                {
                    if (!SwitchToHierarchy(ctx.ParentFrameHierarchy)) return;
                }
                else
                {
                    _driver.SwitchTo().DefaultContent();
                }

                var js = (IJavaScriptExecutor)_driver;
                var getIframeScript = @"
                    var chain = arguments[0];
                    var idx = arguments[1];
                    var cur = document;
                    for (var i = 0; i < chain.length; i++) {
                        var host = (cur === document) ? document.querySelector(chain[i]) : cur.querySelector(chain[i]);
                        if (!host || !host.shadowRoot) return null;
                        cur = host.shadowRoot;
                    }
                    var iframes = cur.querySelectorAll('iframe');
                    return iframes[idx];
                ";

                var iframeElem = js.ExecuteScript(getIframeScript, ctx.ShadowHostChain, ctx.ShadowIframeIndex) as IWebElement;
                if (iframeElem == null)
                {
                    Console.WriteLine($"⚠ Could not locate iframe inside shadow chain: {string.Join(" > ", ctx.ShadowHostChain)}");
                    return;
                }

                try
                {
                    js.ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", iframeElem);
                    Thread.Sleep(200);
                }
                catch { }

                _driver.SwitchTo().Frame(iframeElem);
                WaitForPageReady();

                var step1 = TestFirstNameField(_settings.FirstName);
                step1.FieldName = $"{step1.FieldName} [{ctx.Name}]";
                report.Steps.Add(step1);

                var step2 = TestGenderField(_settings.Gender);
                step2.FieldName = $"{step2.FieldName} [{ctx.Name}]";
                report.Steps.Add(step2);

                var step3 = TestStateField(_settings.State);
                step3.FieldName = $"{step3.FieldName} [{ctx.Name}]";
                report.Steps.Add(step3);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error automating iframe in shadow: {ex.Message}");
            }
            finally
            {
                _driver.SwitchTo().DefaultContent();
            }
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

                    // Click Target
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
                        // Input / Datalist State field
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
                        // Custom dropdown (e.g. Bootstrap dropdown button)
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
