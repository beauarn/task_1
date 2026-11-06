using System;
using System.Collections.Generic;
using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using ResilientFormAutomation.Config;
using ResilientFormAutomation.Models;

namespace ResilientFormAutomation.Services
{
    public class ScenarioRunner
    {
        private readonly AutomationSettings _baseSettings;

        public ScenarioRunner(AutomationSettings baseSettings)
        {
            _baseSettings = baseSettings;
        }

        public void RunAllMutationScenarios()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("===============================================================================");
            Console.WriteLine("🧪 RUNNING COMPREHENSIVE MUTATION & RESILIENCY TEST SUITE");
            Console.WriteLine("   Validating that automation survives order swaps, renamed IDs/classes,");
            Console.WriteLine("   HTML structural shifts, and distractor/similar fields!");
            Console.WriteLine("===============================================================================");
            Console.ResetColor();

            string scenariosDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scenarios");
            if (!Directory.Exists(scenariosDir))
            {
                // Fallback to project directory
                scenariosDir = Path.Combine(Directory.GetCurrentDirectory(), "Scenarios");
            }

            var scenarioFiles = new[]
            {
                ("Scenario 1: Reordered Elements (State on top, First Name at bottom)", Path.Combine(scenariosDir, "scenario1_reordered.html")),
                ("Scenario 2: Dynamic GUIDs & Obfuscated Classes (No standard IDs)", Path.Combine(scenariosDir, "scenario2_renamed_attributes.html")),
                ("Scenario 3: Table Layout (No form-groups or explicit labels)", Path.Combine(scenariosDir, "scenario3_table_layout.html")),
                ("Scenario 4: High Disambiguation (Middle/Last/User name, Country/City dropdowns)", Path.Combine(scenariosDir, "scenario4_similar_fields.html")),
                ("Scenario 5: Generic 'Name' Label (Instead of 'First Name')", Path.Combine(scenariosDir, "scenario5_generic_name_label.html")),
                ("Scenario 6: Linear Horizontal Line Layout (Single-Line Flex/Inline)", Path.Combine(scenariosDir, "scenario6_linear_inline_layout.html")),
                ("Scenario 7: Form Inside IFrame Without ID (Embedded Frame Context)", Path.Combine(scenariosDir, "scenario7_embedded_iframe_no_id.html")),
                ("Scenario 8: Inputs on Left, Missing Input & Multiple First Names", Path.Combine(scenariosDir, "scenario8_reversed_inline_with_missing_input.html")),
            };

            var scenarioResults = new List<(string Name, AutomationReport Report)>();

            foreach (var (name, path) in scenarioFiles)
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"⚠ Skipping {name}: file not found at {path}");
                    continue;
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"▶ Executing {name}...");
                Console.ResetColor();

                var scenarioSettings = new AutomationSettings
                {
                    TargetUrl = new Uri(Path.GetFullPath(path)).AbsoluteUri,
                    FirstName = _baseSettings.FirstName,
                    Gender = _baseSettings.Gender,
                    State = _baseSettings.State,
                    Headless = _baseSettings.Headless,
                    TimeoutSeconds = _baseSettings.TimeoutSeconds,
                    HighlightElements = _baseSettings.HighlightElements,
                    TakeScreenshots = _baseSettings.TakeScreenshots,
                    ScreenshotDirectory = Path.Combine(_baseSettings.ScreenshotDirectory, "Scenarios"),
                    Browser = _baseSettings.Browser,
                    BrowserBinaryPath = _baseSettings.BrowserBinaryPath
                };

                using var driver = WebDriverFactory.CreateDriver(scenarioSettings);
                var service = new FormInteractionService(driver, scenarioSettings);
                var report = service.ExecuteFullTest();
                scenarioResults.Add((name, report));
            }

            // Print Final Matrix
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("===============================================================================");
            Console.WriteLine("🏁 MUTATION SUITE TEST MATRIX SUMMARY");
            Console.WriteLine("===============================================================================");
            Console.ResetColor();

            Console.WriteLine(string.Format("{0,-55} | {1,-10} | {2,-8} | {3,-8} | {4,-8}",
                "Scenario", "Overall", "First Name", "Gender", "State"));
            Console.WriteLine(new string('-', 98));

            int passCount = 0;
            foreach (var (name, rep) in scenarioResults)
            {
                string fnStatus = rep.Steps.FirstOrDefault(s => s.FieldName.Contains("First"))?.Success == true ? "PASS" : "FAIL";
                string genStatus = rep.Steps.FirstOrDefault(s => s.FieldName.Contains("Gender"))?.Success == true ? "PASS" : "FAIL";
                string stStatus = rep.Steps.FirstOrDefault(s => s.FieldName.Contains("State"))?.Success == true ? "PASS" : "FAIL";
                string overall = rep.OverallSuccess ? "PASS" : "FAIL";

                if (rep.OverallSuccess) passCount++;

                Console.ForegroundColor = rep.OverallSuccess ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(string.Format("{0,-55} | {1,-10} | {2,-8} | {3,-8} | {4,-8}",
                    name, overall, fnStatus, genStatus, stStatus));
                Console.ResetColor();
            }

            Console.WriteLine(new string('-', 98));
            Console.ForegroundColor = passCount == scenarioResults.Count ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"Total Scenarios Tested: {scenarioResults.Count} | Passed: {passCount} | Success Rate: {(scenarioResults.Count > 0 ? (passCount * 100.0 / scenarioResults.Count) : 0):F0}%");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
