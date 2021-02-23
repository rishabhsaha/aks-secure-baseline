using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleChainApi;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RestAPIClient.Pages
{
    public class ResponseModel : PageModel
    {
        public DependencyResult ResponseBody { get; set; }

        public string FormatedResult { get; set; }
        public void OnGet(string result)
        {
            ResponseBody = JsonConvert.DeserializeObject<DependencyResult>(result);
            StringBuilder stringBuilder = new StringBuilder();
            Format(ResponseBody, stringBuilder, 0);
            FormatedResult = stringBuilder.ToString();
        }

        public void Format(DependencyResult dependencyResult, StringBuilder sb, int indent)
        {
            if (dependencyResult.ExternalDependencies.Any() || dependencyResult.SelfCalled.Any())
            {
                sb.AppendLine("<br><div style='padding-left: " + indent + "em;'><br>");
                sb.AppendLine("<label>External dependencies: </label><br><ul>");
                foreach (var externalDependency in dependencyResult.ExternalDependencies)
                {
                    var color = externalDependency.Success ? "text-success" : "text-danger";
                    sb.Append($"<li><p class=\"{color}\"><i>{externalDependency.URI}</i><br>");
                    sb.Append($" StatusCode: {externalDependency.StatusCode}");
                    sb.Append("</li></p>");
                }
                sb.Append("</ul>");

                sb.AppendLine("<label>Recursive dependencies: </label><br><ul>");
                foreach (var selfCalled in dependencyResult.SelfCalled)
                {
                    var color = selfCalled.Success ? "text-success" : "text-danger";
                    sb.Append($"<li><p class=\"{color}\"><i>{selfCalled.URI}</i><br>");
                    sb.Append($" StatusCode: {selfCalled.StatusCode}");
                    Format(selfCalled.DependencyResult, sb, indent + 2);
                    sb.Append("</li></p>");
                }
                sb.Append("</ul>");

                sb.AppendLine("</div>");
            }
        }
    }
}