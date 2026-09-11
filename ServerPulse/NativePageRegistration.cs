using SharedLibraryCore.Interfaces;

namespace ServerPulse;

internal static class NativePageRegistration
{
    public static void Add(IPageList pages, string name, string location, string icon)
    {
        pages.Pages[name] = location;

        // The icon dictionary was added to the bundle-capable IW4MAdmin host
        // after the public plugin SDK used to compile this release. Resolve it
        // at runtime so the plugin remains binary-compatible with both hosts.
        if (pages.GetType().GetProperty("PageIcons")?.GetValue(pages) is IDictionary<string, string> icons)
            icons[name] = icon;
    }
}
