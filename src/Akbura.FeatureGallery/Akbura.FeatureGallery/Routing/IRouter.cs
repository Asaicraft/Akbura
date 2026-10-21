using System.ComponentModel;

namespace Akbura.FeatureGallery.Routing;

public interface IRouter : INotifyPropertyChanged
{
    string Url { get; set; }
}
