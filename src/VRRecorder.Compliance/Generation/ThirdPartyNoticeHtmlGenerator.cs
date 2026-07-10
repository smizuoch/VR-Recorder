using System.Net;
using System.Text;

namespace VRRecorder.Compliance.Generation;

public static class ThirdPartyNoticeHtmlGenerator
{
    public static string Generate(
        string productName,
        ApprovedReleaseGraph approvedGraph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(approvedGraph);

        var components = approvedGraph.Graph.Components
            .Where(component => component.Scope is not (
                NoticeScope.TestOnly or NoticeScope.BuildOnly))
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();
        var output = new StringBuilder();
        output.Append("<!doctype html>\n")
            .Append("<html lang=\"en\">\n<head>\n")
            .Append("<meta charset=\"utf-8\">\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>")
            .Append(Encode(productName))
            .Append(" third-party notices</title>\n")
            .Append("<style>body{font-family:system-ui,sans-serif;max-width:72rem;margin:auto;padding:1rem}pre{white-space:pre-wrap;overflow-wrap:anywhere}dt{font-weight:bold}section{margin-block:2rem}</style>\n")
            .Append("</head>\n<body>\n<header>\n<h1>")
            .Append(Encode(productName))
            .Append(" third-party notices</h1>\n")
            .Append("<p>Use your browser's Find command to search this offline document.</p>\n")
            .Append("</header>\n<nav aria-label=\"Contents\">\n<ol>\n");

        foreach (var component in components)
        {
            output.Append("<li><a href=\"#component-")
                .Append(Encode(component.Id))
                .Append("\">")
                .Append(Encode(component.DisplayName))
                .Append(" — ")
                .Append(Encode(component.Version))
                .Append("</a></li>\n");
        }

        output.Append("</ol>\n</nav>\n<main>\n");
        foreach (var component in components)
        {
            output.Append("<section id=\"component-")
                .Append(Encode(component.Id))
                .Append("\">\n<h2>")
                .Append(Encode(component.DisplayName))
                .Append("</h2>\n<dl>\n")
                .Append("<dt>Version</dt><dd>")
                .Append(Encode(component.Version))
                .Append("</dd>\n<dt>SPDX declared</dt><dd>")
                .Append(Encode(component.License.DeclaredExpression))
                .Append("</dd>\n<dt>SPDX concluded</dt><dd>")
                .Append(Encode(component.License.ConcludedExpression))
                .Append("</dd>\n<dt>Copyright</dt><dd>")
                .Append(Encode(component.CopyrightNotice))
                .Append("</dd>\n<dt>Usage</dt><dd>")
                .Append(Encode(component.Usage))
                .Append("</dd>\n<dt>Linkage</dt><dd>")
                .Append(Encode(component.Linkage))
                .Append("</dd>\n<dt>Modified</dt><dd>")
                .Append(component.Modified ? "yes" : "no")
                .Append("</dd>\n<dt>Source</dt><dd>")
                .Append(Encode(component.SourceInformation))
                .Append("</dd>\n</dl>\n");

            foreach (var legalFile in component.LegalFiles
                         .OrderBy(file => file.Kind)
                         .ThenBy(
                             file => file.RelativePath,
                             StringComparer.Ordinal))
            {
                output.Append("<h3>")
                    .Append(Encode(legalFile.Kind.ToString()))
                    .Append(" — ")
                    .Append(Encode(legalFile.RelativePath))
                    .Append("</h3>\n<pre>")
                    .Append(Encode(legalFile.Utf8Content))
                    .Append("</pre>\n");
            }

            output.Append("</section>\n");
        }

        return output.Append("</main>\n</body>\n</html>\n").ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
