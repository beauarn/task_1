using System;
using System.IO;
using System.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using ResilientFormAutomation.Config;

namespace ResilientFormAutomation.Services
{
    public static class WebDriverFactory
    {
        public static IWebDriver CreateDriver(AutomationSettings settings)
        {
            string requestedBrowser = (settings.Browser ?? "chrome").ToLowerInvariant().Trim();

            try
            {
                return InitializeDriver(requestedBrowser, settings);
            }
            catch (Exception ex) when (requestedBrowser != "chrome")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine($"⚠ Warning: Could not launch requested browser '{requestedBrowser}': {ex.Message}");
                Console.WriteLine("🔄 Falling back to available system browser (Chrome/Edge/Brave)...");
                Console.ResetColor();

                string fallback = AutoDetectBrowser();
                if (fallback.Equals(requestedBrowser, StringComparison.OrdinalIgnoreCase))
                {
                    fallback = "chrome";
                }

                try
                {
                    return InitializeDriver(fallback, settings);
                }
                catch (Exception fallbackEx)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize both '{requestedBrowser}' and fallback browser '{fallback}'. " +
                        $"Error: {fallbackEx.Message}", fallbackEx);
                }
            }
        }

        private static IWebDriver InitializeDriver(string browser, AutomationSettings settings)
        {
            if (browser == "auto")
            {
                browser = AutoDetectBrowser();
            }

            switch (browser.ToLowerInvariant())
            {
                case "brave":
                case "brave-browser":
                    return CreateBraveDriver(settings);

                case "edge":
                case "msedge":
                case "microsoft-edge":
                    return CreateEdgeDriver(settings);

                case "firefox":
                case "gecko":
                case "mozilla":
                    return CreateFirefoxDriver(settings);

                case "chrome":
                case "google-chrome":
                default:
                    return CreateChromeDriver(settings);
            }
        }

        public static string AutoDetectBrowser()
        {
            if (IsChromeInstalled()) return "chrome";
            if (IsBraveInstalled()) return "brave";
            if (IsEdgeInstalled()) return "edge";
            if (IsFirefoxInstalled()) return "firefox";
            return "chrome";
        }

        #region Chrome

        public static IWebDriver CreateChromeDriver(AutomationSettings settings)
        {
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

            if (!string.IsNullOrEmpty(settings.BrowserBinaryPath) && File.Exists(settings.BrowserBinaryPath))
            {
                options.BinaryLocation = settings.BrowserBinaryPath;
            }

            return new ChromeDriver(options);
        }

        private static bool IsChromeInstalled()
        {
            var candidates = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
                "/usr/bin/google-chrome",
                "/usr/bin/google-chrome-stable",
                "/usr/bin/chromium-browser",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
            };
            return candidates.Any(File.Exists);
        }

        #endregion

        #region Brave

        public static IWebDriver CreateBraveDriver(AutomationSettings settings)
        {
            var options = new ChromeOptions();

            string bravePath = settings.BrowserBinaryPath;
            if (string.IsNullOrEmpty(bravePath) || !File.Exists(bravePath))
            {
                bravePath = FindBraveBinaryPath() ?? "";
            }

            if (string.IsNullOrEmpty(bravePath) || !File.Exists(bravePath))
            {
                throw new FileNotFoundException(
                    "Brave browser executable was not found in standard installation directories. " +
                    "Please specify the path via --browser-binary=\"path/to/brave.exe\"");
            }

            options.BinaryLocation = bravePath;

            if (settings.Headless)
            {
                options.AddArgument("--headless=new");
            }

            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--window-size=1366,768");
            options.AddArgument("--log-level=3");

            return new ChromeDriver(options);
        }

        private static string? FindBraveBinaryPath()
        {
            var candidates = new[]
            {
                @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
                @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                "/usr/bin/brave-browser",
                "/usr/bin/brave",
                "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static bool IsBraveInstalled()
        {
            return FindBraveBinaryPath() != null;
        }

        #endregion

        #region Edge

        public static IWebDriver CreateEdgeDriver(AutomationSettings settings)
        {
            var options = new EdgeOptions();

            if (settings.Headless)
            {
                options.AddArgument("--headless=new");
            }

            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--window-size=1366,768");
            options.AddArgument("--log-level=3");

            if (!string.IsNullOrEmpty(settings.BrowserBinaryPath) && File.Exists(settings.BrowserBinaryPath))
            {
                options.BinaryLocation = settings.BrowserBinaryPath;
            }

            return new EdgeDriver(options);
        }

        private static bool IsEdgeInstalled()
        {
            var candidates = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
                "/usr/bin/microsoft-edge",
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
            };
            return candidates.Any(File.Exists);
        }

        #endregion

        #region Firefox

        public static IWebDriver CreateFirefoxDriver(AutomationSettings settings)
        {
            var options = new FirefoxOptions();

            if (settings.Headless)
            {
                options.AddArgument("-headless");
            }

            options.AddArgument("--width=1366");
            options.AddArgument("--height=768");

            if (!string.IsNullOrEmpty(settings.BrowserBinaryPath) && File.Exists(settings.BrowserBinaryPath))
            {
                options.BinaryLocation = settings.BrowserBinaryPath;
            }

            return new FirefoxDriver(options);
        }

        private static bool IsFirefoxInstalled()
        {
            var candidates = new[]
            {
                @"C:\Program Files\Mozilla Firefox\firefox.exe",
                @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Mozilla Firefox\firefox.exe"),
                "/usr/bin/firefox",
                "/usr/bin/firefox-esr",
                "/Applications/Firefox.app/Contents/MacOS/firefox"
            };
            return candidates.Any(File.Exists);
        }

        #endregion
    }
}
