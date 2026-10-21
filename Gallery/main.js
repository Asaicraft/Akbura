import { dotnet } from './_framework/dotnet.js'
import { galleryConfig } from './gallery-config.js';
import { resolveGalleryBaseUrl } from './browser-router.js';

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .create();

const config = dotnetRuntime.getConfig();

const galleryBaseUrl = resolveGalleryBaseUrl(galleryConfig.baseUrl, import.meta.url);
await dotnetRuntime.runMain(config.mainAssemblyName, [galleryBaseUrl]);
