import * as esbuild from 'esbuild';

const production = process.argv.includes('--production');
const watch = process.argv.includes('--watch');

const context = await esbuild.context({
    entryPoints: [
        'src/extension.ts'
    ],
    bundle: true,
    format: 'cjs',
    platform: 'node',
    target: 'node20',
    outfile: 'dist/extension.js',
    external: [
        'vscode'
    ],
    minify: production,
    sourcemap: production ? false : true,
    sourcesContent: false,
    logLevel: 'info'
});

if (watch) {
    await context.watch();
    console.log('[Akbura] Watching VS Code extension sources...');
} else {
    await context.rebuild();
    await context.dispose();
}
