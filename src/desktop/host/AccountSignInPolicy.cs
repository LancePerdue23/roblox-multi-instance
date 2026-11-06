using System;

namespace RobloxMultiInstance.Desktop;

internal static class AccountSignInPolicy
{
    internal static bool AllowedNavigation(string value) =>
        Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme=="https" &&
        uri.Port==443 && uri.UserInfo.Length==0 && !value.Contains('\\') &&
        uri.Host is "www.roblox.com" or "roblox.com";
    internal static bool AllowedFrame(string value) =>
        value=="about:blank" || (Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
        uri.Scheme=="https" && uri.UserInfo.Length==0 && uri.Host!="desktop.local");
}
