using CommunityToolkit.Mvvm.ComponentModel;

namespace Akbura.FeatureGallery.Routing;

public sealed class MockRouter : ObservableObject, IRouter
{
    private string _url = GalleryRoutePath.Overview;

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, GalleryRoutePath.Normalize(value));
    }
}
