using System;
using System.Collections.Generic;

namespace ResilientFormAutomation.Models
{
    public class StepResult
    {
        public string FieldName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ExpectedValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;
        public string WinningStrategy { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }
        public List<string> StrategyLog { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    public class AutomationReport
    {
        public string TargetUrl { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration => EndTime - StartTime;
        public bool OverallSuccess => Steps.Count > 0 && Steps.TrueForAll(s => s.Success);
        public List<StepResult> Steps { get; set; } = new();
        public string ScreenshotPath { get; set; } = string.Empty;
    }
}
