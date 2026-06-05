import esbuild from 'esbuild';

const watch = process.argv.includes('--watch');

const ctx = await esbuild.context({
  entryPoints: ['src/extension.ts'],
  bundle: true,
  platform: 'node',
  format: 'cjs',
  target: 'node18',
  external: ['vscode'], // provided by the editor host at runtime
  outfile: 'dist/extension.js',
  sourcemap: true,
  minify: !watch,
});

if (watch) {
  await ctx.watch();
  console.log('[esbuild] watching…');
} else {
  await ctx.rebuild();
  await ctx.dispose();
  console.log('[esbuild] built dist/extension.js');
}
