namespace SevenSeas.Core.Models;

/// <summary>
/// A saved start page that behaves like a bookmark. Opening one gives the user somewhere to
/// browse from, which is how new site bookmarks get discovered and added.
/// </summary>
public sealed class HomePage
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool TryValidate(out string error)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = $"'{Url}' is not a valid http(s) URL.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public HomePage Clone() => new() { Name = Name, Url = Url };
}
