using System;

namespace ResilientFormAutomation.Config
{
    public class AutomationSettings
    {
        public string TargetUrl { get; set; } = "https://app.cloudqa.io/home/AutomationPracticeForm";
        public string FirstName { get; set; } = "Kartik";
        public string Gender { get; set; } = "Male";
        public string State { get; set; } = "United States";
        public bool Headless { get; set; } = false;
        public int TimeoutSeconds { get; set; } = 15;
        public bool HighlightElements { get; set; } = true;
        public bool TakeScreenshots { get; set; } = true;
        public string ScreenshotDirectory { get; set; } = "Screenshots";
        public bool RunMutationTests { get; set; } = false;
        public string TargetFrame { get; set; } = "all";
    }
}
