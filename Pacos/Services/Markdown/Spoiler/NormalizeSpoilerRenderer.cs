using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace Pacos.Services.Markdown.Spoiler;

public sealed class NormalizeSpoilerRenderer : HtmlObjectRenderer<SpoilerInline>
{
    protected override void Write(HtmlRenderer renderer, SpoilerInline obj)
    {
        if (renderer.EnableHtmlForInline)
        {
            renderer.Write("<span").WriteAttributes(obj).Write(">");
        }
        renderer.WriteEscape(obj.Content);
        if (renderer.EnableHtmlForInline)
        {
            renderer.Write("</span>");
        }
    }
}
