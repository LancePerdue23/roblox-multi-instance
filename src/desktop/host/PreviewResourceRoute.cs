using System;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace RobloxMultiInstance.Desktop;

internal static class PreviewResourceRoute
{
    public static void Attach(CoreWebView2 core,PreviewGridService previews,Func<bool> current)
    {
        core.AddWebResourceRequestedFilter(PreviewConfiguration.ImageOrigin+"/*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>
        {
            byte[]? bytes=null;
            string referer=e.Request.Headers.Contains("Referer")?e.Request.Headers.GetHeader("Referer"):"";
            if(current() && e.ResourceContext==CoreWebView2WebResourceContext.Image && e.Request.Method=="GET" && BridgeProtocol.IsTrusted(core.Source) && BridgeProtocol.IsTrusted(referer))
                bytes=previews.Read(e.Request.Uri,referer);
            e.Response=core.Environment.CreateWebResourceResponse(bytes is null?null:new MemoryStream(bytes,false),bytes is null?404:200,bytes is null?"Not Found":"OK","Content-Type: image/jpeg\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
        };
    }
}
