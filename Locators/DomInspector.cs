using System;
using System.Collections.Generic;
using System.Drawing;
using OpenQA.Selenium;

namespace ResilientFormAutomation.Locators
{
    public class DomInspector
    {
        private readonly IWebDriver _driver;
        private readonly IJavaScriptExecutor _js;

        public DomInspector(IWebDriver driver)
        {
            _driver = driver;
            _js = (IJavaScriptExecutor)driver;
        }

        public void Highlight(IWebElement element, string color = "#28a745")
        {
            try
            {
                _js.ExecuteScript(
                    @"arguments[0].style.outline = '3px solid ' + arguments[1];
                      arguments[0].style.transition = 'all 0.3s ease';
                      arguments[0].style.boxShadow = '0 0 12px ' + arguments[1];",
                    element, color);
            }
            catch
            {

            }
        }

        public void ScrollIntoView(IWebElement element)
        {
            try
            {
                _js.ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", element);
            }
            catch
            {

            }
        }

        public void JsClick(IWebElement element)
        {
            _js.ExecuteScript("arguments[0].click();", element);
        }

        public void DispatchInputEvents(IWebElement element)
        {
            try
            {
                _js.ExecuteScript(@"
                    arguments[0].dispatchEvent(new Event('input', { bubbles: true }));
                    arguments[0].dispatchEvent(new Event('change', { bubbles: true }));
                ", element);
            }
            catch
            {

            }
        }

        public Rectangle GetBoundingRect(IWebElement element)
        {
            try
            {
                var dict = _js.ExecuteScript(@"
                    var rect = arguments[0].getBoundingClientRect();
                    return {
                        x: Math.round(rect.x),
                        y: Math.round(rect.y),
                        width: Math.round(rect.width),
                        height: Math.round(rect.height)
                    };
                ", element) as Dictionary<string, object>;

                if (dict != null)
                {
                    int x = Convert.ToInt32(dict["x"]);
                    int y = Convert.ToInt32(dict["y"]);
                    int w = Convert.ToInt32(dict["width"]);
                    int h = Convert.ToInt32(dict["height"]);

                    return new Rectangle(x, y, w, h);
                }
                return new Rectangle(element.Location.X, element.Location.Y, element.Size.Width, element.Size.Height);
            }
            catch
            {
                return new Rectangle(element.Location.X, element.Location.Y, element.Size.Width, element.Size.Height);
            }
        }

        public double CalculateDistance(IWebElement fromElem, IWebElement toElem)
        {
            try
            {
                var r1 = GetBoundingRect(fromElem);
                var r2 = GetBoundingRect(toElem);

                double c1x = r1.X + r1.Width / 2.0;
                double c1y = r1.Y + r1.Height / 2.0;

                double c2x = r2.X + r2.Width / 2.0;
                double c2y = r2.Y + r2.Height / 2.0;

                double dx = c2x - c1x;
                double dy = c2y - c1y;

                return Math.Sqrt(dx * dx + dy * dy);
            }
            catch
            {
                return double.MaxValue;
            }
        }

        public bool IsSameHorizontalRow(IWebElement fromElem, IWebElement toElem, int tolerancePx = 25)
        {
            try
            {
                var r1 = GetBoundingRect(fromElem);
                var r2 = GetBoundingRect(toElem);
                double c1y = r1.Y + r1.Height / 2.0;
                double c2y = r2.Y + r2.Height / 2.0;
                return Math.Abs(c2y - c1y) <= tolerancePx;
            }
            catch
            {
                return false;
            }
        }

        public bool IsVisible(IWebElement element)
        {
            try
            {
                if (!element.Displayed) return false;
                var size = element.Size;
                return size.Width > 0 && size.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        public string GetElementSummary(IWebElement element)
        {
            try
            {
                string tag = element.TagName;
                string id = element.GetAttribute("id") ?? "";
                string name = element.GetAttribute("name") ?? "";
                string type = element.GetAttribute("type") ?? "";
                string placeholder = element.GetAttribute("placeholder") ?? "";
                string className = element.GetAttribute("class") ?? "";

                var parts = new List<string> { $"<{tag}" };
                if (!string.IsNullOrEmpty(type)) parts.Add($"type='{type}'");
                if (!string.IsNullOrEmpty(id)) parts.Add($"id='{id}'");
                if (!string.IsNullOrEmpty(name)) parts.Add($"name='{name}'");
                if (!string.IsNullOrEmpty(placeholder)) parts.Add($"placeholder='{placeholder}'");
                if (!string.IsNullOrEmpty(className)) parts.Add($"class='{className}'");

                return string.Join(" ", parts) + ">";
            }
            catch
            {
                return "<element detached/inaccessible>";
            }
        }
    }
}
