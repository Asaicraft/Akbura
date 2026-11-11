const startup = document.getElementById('startup');

try {
    const { dotnet } = await import('./_framework/dotnet.js');
    const runtime = await dotnet.create();
    await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
    startup.remove();
} catch (error) {
    startup.textContent = 'Akbura Playground could not start. See the browser console.';
    console.error(error);
}
