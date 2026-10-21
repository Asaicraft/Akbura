using System;

namespace Akbura.FeatureGallery.Routing;

/// <summary>Browser location boundary; implementations own their event listeners.</summary>
public interface IJsBrowserUrl : IDisposable
{
    string BaseUrl { get; }
    string CurrentUrl { get; }
    event EventHandler<string>? UrlChanged;
    void PushUrl(string url);
    void ReplaceUrl(string url);
}
