using System.Collections.Generic;
using OpenQA.Selenium;

namespace ResilientFormAutomation.Models
{
    public class CandidateMatch
    {
        public IWebElement Element { get; set; }
        public double ConfidenceScore { get; set; }
        public string StrategyName { get; set; } = string.Empty;
        public List<string> Reasoning { get; set; } = new();
        public string ElementSummary { get; set; } = string.Empty;
        public IWebElement? ClickTarget { get; set; }

        public CandidateMatch(IWebElement element, double initialScore, string strategy)
        {
            Element = element;
            ConfidenceScore = initialScore;
            StrategyName = strategy;
        }

        public void AddScore(double points, string reason)
        {
            ConfidenceScore += points;
            Reasoning.Add($"[+{points} pts] {reason}");
        }

        public void Penalize(double points, string reason)
        {
            ConfidenceScore -= points;
            Reasoning.Add($"[-{points} pts] {reason}");
        }

        public override string ToString()
        {
            return $"{StrategyName} (Score: {ConfidenceScore:F1}) - {ElementSummary} | Reasons: {string.Join("; ", Reasoning)}";
        }
    }
}
