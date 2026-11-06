using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium.Chrome;
using ResilientFormAutomation.Config;
using ResilientFormAutomation.Services;

namespace ResilientFormAutomation
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            PrintBanner();

            // 1. Build Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            var configRoot = builder.Build();
            var settings = new AutomationSettings();
            configRoot.GetSection("AutomationSettings").Bind(settings);

            // Read environment variables manually if set
            var envTargetUrl = Environment.GetEnvironmentVariable("APP_URL") ?? Environment.GetEnvironmentVariable("AUTOMATION_URL");
            if (!string.IsNullOrEmpty(envTargetUrl)) settings.TargetUrl = envTargetUrl;

            // 2. Parse Command-Line Arguments (highest priority)
            ParseCommandLineArgs(args, settings);

            // Print Effective Settings
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("CONFIGURATION LOADED:");
            Console.WriteLine($"  • Target URL   : {settings.TargetUrl}");
            Console.WriteLine($"  • First Name   : {settings.FirstName}");
            Console.WriteLine($"  • Gender       : {settings.Gender}");
            Console.WriteLine($"  • State        : {settings.State}");
            Console.WriteLine($"  • Target Frame : {settings.TargetFrame} (all | main | no-id | <id>)");
            Console.WriteLine($"  • Headless     : {settings.Headless}");
            Console.WriteLine($"  • Keep Open    : {settings.KeepBrowserOpen} (Press ENTER to close)");
            Console.WriteLine($"  • Screenshots  : {settings.TakeScreenshots} ({settings.ScreenshotDirectory})");
            Console.WriteLine($"  • Run Mutants  : {settings.RunMutationTests}");
            Console.ResetColor();

            if (settings.RunMutationTests)
            {
                var scenarioRunner = new ScenarioRunner(settings);
                scenarioRunner.RunAllMutationScenarios();
                return 0;
            }

            // 3. Setup ChromeDriver
            var options = new ChromeOptions();
            if (settings.Headless)
            {
                options.AddArgument("--headless=new");
            }
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--window-size=1366,768");
            options.AddArgument("--log-level=3");

            try
            {
                using var driver = new ChromeDriver(options);
                var service = new FormInteractionService(driver, settings);
                var report = service.ExecuteFullTest();

                if (!settings.Headless && settings.KeepBrowserOpen)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine();
                    Console.WriteLine("===============================================================================");
                    Console.WriteLine("👀 BROWSER KEPT OPEN FOR INSPECTION");
                    Console.WriteLine("   Chrome is left open so you can view all filled fields and selections.");
                    Console.WriteLine("   Press [ENTER] in this terminal when you want to close the browser and exit...");
                    Console.WriteLine("===============================================================================");
                    Console.ResetColor();

                    try
                    {
                        if (!Console.IsInputRedirected)
                        {
                            Console.ReadLine();
                        }
                    }
                    catch { }
                }

                return report.OverallSuccess ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine($"FATAL EXECUTION ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }

        private static void ParseCommandLineArgs(string[] args, AutomationSettings settings)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    Environment.Exit(0);
                }

                if (arg.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
                {
                    settings.TargetUrl = arg.Substring(6);
                }
                else if (arg.Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    settings.TargetUrl = args[++i];
                }
                else if (arg.StartsWith("--first-name=", StringComparison.OrdinalIgnoreCase))
                {
                    settings.FirstName = arg.Substring(13);
                }
                else if (arg.Equals("--first-name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    settings.FirstName = args[++i];
                }
                else if (arg.StartsWith("--gender=", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Gender = arg.Substring(9);
                }
                else if (arg.Equals("--gender", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    settings.Gender = args[++i];
                }
                else if (arg.StartsWith("--state=", StringComparison.OrdinalIgnoreCase))
                {
                    settings.State = arg.Substring(8);
                }
                else if (arg.Equals("--state", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    settings.State = args[++i];
                }
                else if (arg.StartsWith("--frame=", StringComparison.OrdinalIgnoreCase))
                {
                    settings.TargetFrame = arg.Substring(8);
                }
                else if (arg.Equals("--frame", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    settings.TargetFrame = args[++i];
                }
                else if (arg.Equals("--headless", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Headless = true;
                }
                else if (arg.Equals("--headed", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Headless = false;
                }
                else if (arg.Equals("--test-mutations", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--scenarios", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--run-scenarios", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RunMutationTests = true;
                }
                else if (arg.Equals("--keep-open", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--no-close", StringComparison.OrdinalIgnoreCase))
                {
                    settings.KeepBrowserOpen = true;
                }
                else if (arg.Equals("--auto-close", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--close", StringComparison.OrdinalIgnoreCase))
                {
                    settings.KeepBrowserOpen = false;
                }
                else if (!arg.StartsWith("-") && (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                                  arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                                  arg.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                                                  arg.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                                                  arg.Contains(".")))
                {
                    // Positional URL argument (supports domain names without http/https)
                    settings.TargetUrl = arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                         arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                         arg.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                                         ? arg : "https://" + arg;
                }
            }

            // Check environment variable fallback
            string? envUrl = Environment.GetEnvironmentVariable("APP_URL") ?? Environment.GetEnvironmentVariable("AUTOMATION_URL");
            if (!string.IsNullOrEmpty(envUrl) && (args.Length == 0 || !args.Any(a => a.Contains("url"))))
            {
                settings.TargetUrl = envUrl;
            }
        }

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
╔════════════════════════════════════════════════════════════════════════════════╗
║             SELF-HEALING & RESILIENT WEB FORM AUTOMATION                       ║
║                Task 1: Change-Tolerant Test Automation                         ║
╚════════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
Usage: dotnet run [options] [-- <url>]

Options:
  --url <url>             Configurable target page URL (default: https://app.cloudqa.io/home/AutomationPracticeForm)
  --first-name <name>     First Name value to fill (default: 'Kartik')
  --gender <gender>       Gender option to select (default: 'Male')
  --state <state>         State dropdown option to select (default: 'United States')
  --headless              Run Chrome in headless mode (default on CI / optional)
  --headed                Run Chrome with visible browser window
  --test-mutations        Run the built-in mutation test suite against modified HTML pages
  --help, -h              Show this help message

Examples:
  dotnet run --headless
  dotnet run -- ""https://app.cloudqa.io/home/AutomationPracticeForm""
  dotnet run --url=""https://app.cloudqa.io/home/AutomationPracticeForm"" --first-name=""Kartik""
  dotnet run --test-mutations --headless
");
        }
    }
}
