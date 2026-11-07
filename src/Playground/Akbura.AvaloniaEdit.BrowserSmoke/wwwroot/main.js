import { dotnet } from './_framework/dotnet.js';
const runtime = await dotnet.withDiagnosticTracing(false).create();
await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
