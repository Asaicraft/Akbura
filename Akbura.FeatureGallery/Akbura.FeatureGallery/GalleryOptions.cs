using Akbura.Engine;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery;

public sealed class GalleryOptions
{
    public string RepositoryUrl { get; set; } =
        "https://github.com/Asaicraft/Akbura";

    public string MainBranchName { get; set; } =
        "master";

    public string PathToGallery { get; set; } =
        "Akbura.FeatureGallery/Akbura.FeatureGallery";
}

public sealed class GalleryOptionsProvider : IServiceProvider
{
    private static readonly Type s_optionsType = typeof(GalleryOptions);
    public static readonly GalleryOptionsProvider Instance = new();
    public static readonly GalleryOptions GalleryOptions = new();

    public object? GetService(Type serviceType)
    {
        return serviceType == s_optionsType ? GalleryOptions : null;
    }
}

public static class GalleryOptionsExtensions
{
    extension(AkburaEngineExtensions.AkburaEngineBuilder builder)
    {
        public AkburaEngineExtensions.AkburaEngineBuilder WithGalleryOptions()
        {
            return builder.WithServiceProvider(GalleryOptionsProvider.Instance);
        }
    }
}