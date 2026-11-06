using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using ResilientFormAutomation.Models;

namespace ResilientFormAutomation.Locators
{
    public class SmartFieldResolver
    {
        private readonly IWebDriver _driver;
        private readonly DomInspector _inspector;

        // Semantic synonym dictionaries
        private static readonly string[] FirstNamePositiveKeywords = {
            "first name", "firstname", "first_name", "first-name",
            "fname", "given name", "givenname", "given-name", "forename", "first"
        };

        private static readonly string[] FirstNameNegativeKeywords = {
            "last", "surname", "family", "lname", "user", "username", "middle",
            "company", "full name", "fullname", "phone", "email", "address",
            "city", "state", "zip", "dob", "birth", "country", "mobile", "password"
        };

        private static readonly string[] GenericNameKeywords = {
            "name", "your name", "full name", "fullname", "candidate name", "person name"
        };

        private static readonly string[] CompetingNameKeywords = {
            "last", "surname", "family", "lname", "user", "username", "middle", "company", "father", "mother"
        };

        private static readonly string[] GenderKeywords = {
            "gender", "sex"
        };

        private static readonly string[] StateKeywords = {
            "state", "province", "region"
        };

        public SmartFieldResolver(IWebDriver driver, DomInspector inspector)
        {
            _driver = driver;
            _inspector = inspector;
        }

        #region First Name Resolution

        public CandidateMatch? ResolveFirstName()
        {
            var candidates = new List<CandidateMatch>();

            // 1. Gather all potential text-like input elements
            var inputs = FindAllPotentialTextInputs();
            if (inputs.Count == 0) return null;

            // 2. Gather all label elements on the page (pre-cache metadata for optimal performance)
            var rawLabels = _driver.FindElements(By.TagName("label")).ToList();
            var labelData = rawLabels.Select(l =>
            {
                try
                {
                    string text = (l.Text ?? l.GetAttribute("innerText") ?? "").Trim();
                    return new
                    {
                        Element = l,
                        Text = text,
                        LowerText = text.ToLowerInvariant(),
                        Name = (l.GetAttribute("name") ?? "").ToLowerInvariant(),
                        For = (l.GetAttribute("for") ?? "").ToLowerInvariant()
                    };
                }
                catch
                {
                    return null;
                }
            }).Where(l => l != null).Select(l => l!).ToList();

            foreach (var input in inputs)
            {
                var candidate = new CandidateMatch(input, 0, "Multi-Heuristic Smart Resolution");
                candidate.ElementSummary = _inspector.GetElementSummary(input);

                string id = (input.GetAttribute("id") ?? "").ToLowerInvariant();
                string name = (input.GetAttribute("name") ?? "").ToLowerInvariant();
                string placeholder = (input.GetAttribute("placeholder") ?? "").ToLowerInvariant();
                string ariaLabel = (input.GetAttribute("aria-label") ?? "").ToLowerInvariant();
                string autocomplete = (input.GetAttribute("autocomplete") ?? "").ToLowerInvariant();
                string title = (input.GetAttribute("title") ?? "").ToLowerInvariant();

                bool isVisible = _inspector.IsVisible(input);
                if (!isVisible)
                {
                    candidate.Penalize(150, "Element is not visible or has zero dimensions");
                }
                else
                {
                    candidate.AddScore(15, "Element is visible and interactable");
                }

                // Check Negative Keywords first (disambiguate against Last Name, Username, etc.)
                bool hasNegativeKeyword = false;
                foreach (var neg in FirstNameNegativeKeywords)
                {
                    if (id.Contains(neg) || name.Contains(neg) || placeholder.Contains(neg) || ariaLabel.Contains(neg))
                    {
                        candidate.Penalize(120, $"Direct attribute contains conflicting keyword '{neg}'");
                        hasNegativeKeyword = true;
                        break;
                    }
                }

                // Direct attribute positive matches
                foreach (var pos in FirstNamePositiveKeywords)
                {
                    if (id == pos || id.Contains(pos))
                    {
                        candidate.AddScore(35, $"id attribute matches '{pos}'");
                    }
                    if (name == pos || name.Contains(pos))
                    {
                        candidate.AddScore(35, $"name attribute matches '{pos}'");
                    }
                    if (placeholder == pos || placeholder.Contains(pos))
                    {
                        candidate.AddScore(30, $"placeholder matches '{pos}'");
                    }
                    if (ariaLabel.Contains(pos))
                    {
                        candidate.AddScore(35, $"aria-label matches '{pos}'");
                    }
                }

                if (autocomplete == "given-name")
                {
                    candidate.AddScore(45, "HTML5 standard autocomplete='given-name'");
                }

                // Generic 'Name' placeholder when not conflicting with last name
                if (!hasNegativeKeyword)
                {
                    if (placeholder.Equals("name", StringComparison.OrdinalIgnoreCase) || placeholder.Contains("enter name"))
                    {
                        candidate.AddScore(15, "placeholder contains generic 'Name'");
                    }
                    if (id.Equals("name", StringComparison.OrdinalIgnoreCase) || id.Equals("your_name", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.AddScore(25, "id attribute matches generic 'name'");
                    }
                    if (name.Equals("name", StringComparison.OrdinalIgnoreCase) || name.Equals("your_name", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.AddScore(25, "name attribute matches generic 'name'");
                    }
                }

                // Label-to-Input Association Heuristics
                foreach (var label in labelData)
                {
                    // Check if this label represents 'First Name'
                    bool isFirstNameLabel = FirstNamePositiveKeywords.Any(k => label.LowerText.Contains(k) || label.Name.Contains(k));
                    bool isCompetingNameLabel = CompetingNameKeywords.Any(k => label.LowerText.Contains(k) || label.Name.Contains(k));
                    bool isGenericNameLabel = !isCompetingNameLabel &&
                        GenericNameKeywords.Any(k => label.LowerText.Equals(k) || label.LowerText.StartsWith(k + ":") || label.LowerText.StartsWith(k + " *") || label.Name.Equals(k));

                    if (isFirstNameLabel)
                    {
                        // Explicit 'for' -> 'id' matching
                        if (!string.IsNullOrEmpty(label.For) && !string.IsNullOrEmpty(id) && label.For.Equals(id, StringComparison.OrdinalIgnoreCase))
                        {
                            candidate.AddScore(60, $"Explicit <label for='{label.For}'> matches element id='{id}'");
                        }

                        // Explicit 'name' matching
                        if (!string.IsNullOrEmpty(label.Name) && (label.Name.Equals(id, StringComparison.OrdinalIgnoreCase) || label.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            candidate.AddScore(50, $"Label name='{label.Name}' matches element identifier");
                        }

                        // Enclosing label check: <label>First Name <input ...></label>
                        try
                        {
                            var enclosingInputs = label.Element.FindElements(By.XPath(".//input"));
                            if (enclosingInputs.Any(e => e.Equals(input)))
                            {
                                candidate.AddScore(50, "Input is enclosed directly inside the First Name <label>");
                            }
                        }
                        catch { }

                        // Proximity / Visual Distance Check
                        if (isVisible)
                        {
                            double distance = _inspector.CalculateDistance(label.Element, input);
                            if (distance < 180)
                            {
                                double proximityPoints = Math.Max(5, 30 - (distance / 6.0));
                                candidate.AddScore(proximityPoints, $"High visual proximity to label '{label.Text}' (distance: {distance:F0}px)");
                            }
                        }

                        // Shared parent container (form-group, row, div, table-row)
                        if (AreInSameContainer(label.Element, input))
                        {
                            candidate.AddScore(45, $"Shares immediate field container with label '{label.Text}'");
                        }
                    }
                    else if (isGenericNameLabel)
                    {
                        // Fallback when label is simply "Name" or "Your Name" (without "first")
                        if (!string.IsNullOrEmpty(label.For) && !string.IsNullOrEmpty(id) && label.For.Equals(id, StringComparison.OrdinalIgnoreCase))
                        {
                            candidate.AddScore(45, $"Explicit <label for='{label.For}'> (generic name) matches element id='{id}'");
                        }

                        if (AreInSameContainer(label.Element, input))
                        {
                            candidate.AddScore(35, $"Shares immediate field container with generic name label '{label.Text}'");
                        }

                        if (isVisible)
                        {
                            double distance = _inspector.CalculateDistance(label.Element, input);
                            if (distance < 180)
                            {
                                double proximityPoints = Math.Max(5, 20 - (distance / 9.0));
                                candidate.AddScore(proximityPoints, $"High visual proximity to generic label '{label.Text}' (distance: {distance:F0}px)");
                            }
                        }
                    }
                    else if (isCompetingNameLabel && AreInSameContainer(label.Element, input))
                    {
                        candidate.Penalize(90, $"Shares immediate field container with competing label '{label.Text}'");
                    }
                }

                candidates.Add(candidate);
            }

            // Return top candidate with score >= 20
            var ranked = candidates.OrderByDescending(c => c.ConfidenceScore).ToList();
            var bestMatch = ranked.FirstOrDefault();
            return bestMatch != null && bestMatch.ConfidenceScore >= 20 ? bestMatch : null;
        }

        #endregion

        #region Gender Resolution

        public CandidateMatch? ResolveGender(string targetOption = "Male")
        {
            var candidates = new List<CandidateMatch>();
            string normalizedTarget = targetOption.Trim();
            var targetWordRegex = new Regex($@"\b{Regex.Escape(normalizedTarget)}\b", RegexOptions.IgnoreCase);

            // 1. Gather all radio buttons
            var radios = _driver.FindElements(By.XPath("//input[@type='radio']")).ToList();
            if (radios.Count == 0) return null;

            // 2. Locate gender contextual containers or labels
            var genderLabels = _driver.FindElements(By.XPath("//label | //legend | //span | //h3 | //h4 | //div[contains(@class,'label')]"))
                .Where(e => GenderKeywords.Any(k => (e.Text ?? "").ToLowerInvariant().Contains(k)))
                .ToList();

            foreach (var radio in radios)
            {
                var candidate = new CandidateMatch(radio, 0, "Resilient Radio Selection");
                candidate.ElementSummary = _inspector.GetElementSummary(radio);

                string id = (radio.GetAttribute("id") ?? "").ToLowerInvariant();
                string name = (radio.GetAttribute("name") ?? "").ToLowerInvariant();
                string val = (radio.GetAttribute("value") ?? "").Trim();
                string valLower = val.ToLowerInvariant();

                // Context match: Does this radio belong to a Gender group?
                bool belongsToGenderGroup = false;
                foreach (var gkw in GenderKeywords)
                {
                    if (name.Contains(gkw) || id.Contains(gkw))
                    {
                        candidate.AddScore(40, $"Radio name/id contains group identifier '{gkw}'");
                        belongsToGenderGroup = true;
                        break;
                    }
                }

                foreach (var gLabel in genderLabels)
                {
                    if (AreInSameContainer(gLabel, radio))
                    {
                        candidate.AddScore(35, $"Radio is within the container of Gender header/label '{gLabel.Text}'");
                        belongsToGenderGroup = true;
                        break;
                    }
                }

                // If not in gender group and has another group name (e.g. 'hobbies', 'newsletter'), penalize
                if (!belongsToGenderGroup && !string.IsNullOrEmpty(name))
                {
                    candidate.Penalize(50, $"Belongs to non-gender group name='{name}'");
                }

                // Value matching
                if (targetWordRegex.IsMatch(val))
                {
                    candidate.AddScore(50, $"Radio value='{val}' strictly matches target option '{normalizedTarget}'");
                }
                else if (!string.IsNullOrEmpty(val) && !valLower.Equals(normalizedTarget.ToLowerInvariant()))
                {
                    // Penalize different options (e.g. Female when target is Male)
                    candidate.Penalize(100, $"Radio value='{val}' belongs to alternative option");
                }

                // ID matching
                if (targetWordRegex.IsMatch(id))
                {
                    candidate.AddScore(40, $"Radio id='{id}' strictly matches target option '{normalizedTarget}'");
                }
                else if (id.Contains("female") && normalizedTarget.Equals("Male", StringComparison.OrdinalIgnoreCase))
                {
                    candidate.Penalize(120, "Radio id contains 'female' when target is 'Male'");
                }

                // Find adjacent / associated label or span
                var associatedTextElements = FindAdjacentLabelsOrSpans(radio);
                foreach (var (elem, text) in associatedTextElements)
                {
                    string trimmedText = text.Trim();
                    if (targetWordRegex.IsMatch(trimmedText))
                    {
                        candidate.AddScore(45, $"Adjacent element text '{trimmedText}' strictly matches target option");
                        candidate.ClickTarget = elem; // Prefer clicking the label/span if standard radio is styled/hidden
                    }
                    else if (!string.IsNullOrEmpty(trimmedText) && !trimmedText.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        if (trimmedText.Equals("Female", StringComparison.OrdinalIgnoreCase) && normalizedTarget.Equals("Male", StringComparison.OrdinalIgnoreCase))
                        {
                            candidate.Penalize(120, $"Adjacent label is 'Female' when target is 'Male'");
                        }
                    }
                }

                candidates.Add(candidate);
            }

            var bestMatch = candidates.OrderByDescending(c => c.ConfidenceScore).FirstOrDefault();
            return bestMatch != null && bestMatch.ConfidenceScore >= 30 ? bestMatch : null;
        }

        #endregion

        #region State Dropdown Resolution

        public CandidateMatch? ResolveState(string targetState = "United States")
        {
            var candidates = new List<CandidateMatch>();

            // 1. Gather all <select> elements
            var selects = _driver.FindElements(By.TagName("select")).ToList();

            // 2. Gather labels mentioning 'State', 'Province', etc.
            var stateLabels = _driver.FindElements(By.XPath("//label | //span[contains(@class,'label')]"))
                .Where(l => StateKeywords.Any(k => (l.Text ?? "").ToLowerInvariant().Contains(k) || (l.GetAttribute("name") ?? "").ToLowerInvariant().Contains(k)))
                .ToList();

            foreach (var select in selects)
            {
                var candidate = new CandidateMatch(select, 0, "Resilient Select Element Resolution");
                candidate.ElementSummary = _inspector.GetElementSummary(select);

                string id = (select.GetAttribute("id") ?? "").ToLowerInvariant();
                string name = (select.GetAttribute("name") ?? "").ToLowerInvariant();
                string ariaLabel = (select.GetAttribute("aria-label") ?? "").ToLowerInvariant();

                bool isVisible = _inspector.IsVisible(select);
                if (isVisible)
                {
                    candidate.AddScore(20, "Select element is visible");
                }

                // Direct attributes matching
                foreach (var skw in StateKeywords)
                {
                    if (id.Contains(skw))
                    {
                        candidate.AddScore(45, $"select id matches '{skw}'");
                    }
                    if (name.Contains(skw))
                    {
                        candidate.AddScore(45, $"select name matches '{skw}'");
                    }
                    if (ariaLabel.Contains(skw))
                    {
                        candidate.AddScore(40, $"select aria-label matches '{skw}'");
                    }
                }

                // Disambiguate against other dropdowns (e.g. Country dropdown, City dropdown)
                if (name.Contains("country") || id.Contains("country"))
                {
                    candidate.Penalize(60, "Element has identifier containing 'country' rather than 'state'");
                }

                // Label association
                foreach (var label in stateLabels)
                {
                    string labelFor = (label.GetAttribute("for") ?? "").ToLowerInvariant();
                    string labelName = (label.GetAttribute("name") ?? "").ToLowerInvariant();

                    if (!string.IsNullOrEmpty(labelFor) && labelFor.Equals(id, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.AddScore(50, $"Explicit <label for='{labelFor}'> matches select id");
                    }

                    if (!string.IsNullOrEmpty(labelName) && (labelName.Equals(id, StringComparison.OrdinalIgnoreCase) || labelName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidate.AddScore(45, $"Label name='{labelName}' matches select identifier");
                    }

                    if (AreInSameContainer(label, select))
                    {
                        candidate.AddScore(40, $"Select shares container with label '{label.Text}'");
                    }
                }

                // Content inspection: Does this select contain target option?
                try
                {
                    var options = select.FindElements(By.TagName("option"));
                    bool containsTarget = options.Any(o =>
                        (o.Text ?? "").Trim().Equals(targetState, StringComparison.OrdinalIgnoreCase) ||
                        (o.GetAttribute("value") ?? "").Trim().Equals(targetState, StringComparison.OrdinalIgnoreCase));

                    if (containsTarget)
                    {
                        candidate.AddScore(45, $"Select contains target option '{targetState}'");
                    }
                }
                catch { }

                candidates.Add(candidate);
            }

            var bestSelect = candidates.OrderByDescending(c => c.ConfidenceScore).FirstOrDefault();
            if (bestSelect != null && bestSelect.ConfidenceScore >= 35)
            {
                return bestSelect;
            }

            // Custom Dropdown fallback (e.g. Bootstrap/Select2/custom divs)
            var customDropdown = ResolveCustomDropdown(targetState);
            return customDropdown;
        }

        private CandidateMatch? ResolveCustomDropdown(string targetState)
        {
            try
            {
                var customDropdownTriggers = _driver.FindElements(By.XPath(
                    "//*[contains(@class,'dropdown-toggle') or @role='combobox' or contains(@class,'select2')]"));

                foreach (var trigger in customDropdownTriggers)
                {
                    string text = (trigger.Text ?? "").ToLowerInvariant();
                    string id = (trigger.GetAttribute("id") ?? "").ToLowerInvariant();
                    string name = (trigger.GetAttribute("name") ?? "").ToLowerInvariant();

                    if (StateKeywords.Any(k => text.Contains(k) || id.Contains(k) || name.Contains(k)))
                    {
                        var match = new CandidateMatch(trigger, 50, "Custom UI Dropdown Resolver");
                        match.ElementSummary = _inspector.GetElementSummary(trigger);
                        match.AddScore(30, "Custom dropdown trigger identified with state semantic keyword");
                        return match;
                    }
                }
            }
            catch { }

            return null;
        }

        #endregion

        #region Helper Routines

        private List<IWebElement> FindAllPotentialTextInputs()
        {
            var rawInputs = _driver.FindElements(By.TagName("input"));
            var textInputs = new List<IWebElement>();

            foreach (var input in rawInputs)
            {
                try
                {
                    string type = (input.GetAttribute("type") ?? "text").ToLowerInvariant();
                    if (type == "text" || type == "search" || type == "" || type == "tel")
                    {
                        textInputs.Add(input);
                    }
                }
                catch
                {
                    // Stale or detached element
                }
            }

            return textInputs;
        }

        private bool AreInSameContainer(IWebElement elem1, IWebElement elem2, int maxLevels = 2)
        {
            try
            {
                // In HTML forms, an element and its label typically share a field-level container:
                // e.g. .form-group, .field, .input-group, <td>, <p>, or direct parent element.
                // We deliberately exclude generic layout wrappers like 'row', 'form', 'body' so sibling fields aren't conflated.
                var result = ((IJavaScriptExecutor)_driver).ExecuteScript(@"
                    var e1 = arguments[0];
                    var e2 = arguments[1];
                    var fieldWrapper = e1.closest('.form-group, .form-field, .field, .input-group, td, tr, .row-box, .custom-control');
                    if (fieldWrapper && fieldWrapper.tagName !== 'BODY' && fieldWrapper.tagName !== 'FORM' && !fieldWrapper.classList.contains('row')) {
                        return fieldWrapper.contains(e2);
                    }
                    var p1 = e1.parentElement;
                    return (p1 && p1.tagName !== 'BODY' && p1.tagName !== 'FORM' && !p1.classList.contains('row')) ? p1.contains(e2) : false;
                ", elem1, elem2);

                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private List<(IWebElement element, string text)> FindAdjacentLabelsOrSpans(IWebElement radio)
        {
            var results = new List<(IWebElement, string)>();
            try
            {
                string id = radio.GetAttribute("id") ?? "";

                // 1. Explicit label with for="id"
                if (!string.IsNullOrEmpty(id))
                {
                    var forLabels = _driver.FindElements(By.XPath($"//label[@for='{id}'] | //span[@for='{id}']"));
                    foreach (var l in forLabels)
                    {
                        results.Add((l, l.Text));
                    }
                }

                // 2. Following siblings (e.g. <input ...><span ...>Male</span>)
                var followingSiblings = radio.FindElements(By.XPath("following-sibling::*[self::span or self::label][position() <= 2]"));
                foreach (var sib in followingSiblings)
                {
                    results.Add((sib, sib.Text));
                }

                // 3. Parent text (e.g. <label><input ...> Male</label>)
                var parent = radio.FindElement(By.XPath(".."));
                string parentText = parent.Text;
                if (!string.IsNullOrEmpty(parentText))
                {
                    results.Add((parent, parentText));
                }
            }
            catch { }

            return results;
        }

        #endregion
    }
}
