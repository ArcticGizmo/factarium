// Generates the application favicon and README artwork from the single source
// vector (factarium.svg). Every raster is rendered straight from the SVG at its
// target pixel size via resvg (a high-quality vector rasterizer) rather than by
// downscaling one large bitmap, so each size stays crisp. Re-run after editing
// factarium.svg:  cd tools && npm install && npm run generate
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Resvg } from '@resvg/resvg-js';
import pngToIco from 'png-to-ico';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(repoRoot, 'factarium.svg');
const webPublic = join(repoRoot, 'web', 'public');
const docs = join(repoRoot, 'docs');

// Render the source SVG to a PNG buffer at an exact width (height follows the
// square viewBox). Transparent background keeps the vector's own rounded edges.
function renderPng(svg, size) {
  const resvg = new Resvg(svg, {
    fitTo: { mode: 'width', value: size },
    background: 'rgba(0,0,0,0)',
  });
  return Buffer.from(resvg.render().asPng());
}

async function main() {
  const svg = await readFile(source, 'utf8');
  await mkdir(webPublic, { recursive: true });
  await mkdir(docs, { recursive: true });

  // favicon.ico: the classic sizes browsers pick from, packed multi-resolution.
  const icoSizes = [16, 32, 48];
  const icoPngs = icoSizes.map((s) => renderPng(svg, s));
  await writeFile(join(webPublic, 'favicon.ico'), await pngToIco(icoPngs));

  // Scalable favicon for modern browsers — the source vector, served as-is.
  await copyFile(source, join(webPublic, 'favicon.svg'));

  // Home-screen / tab icons and the 512px README artwork.
  await writeFile(join(webPublic, 'apple-touch-icon.png'), renderPng(svg, 180));
  await writeFile(join(docs, 'factarium.png'), renderPng(svg, 512));

  console.log('Generated:');
  console.log('  web/public/favicon.ico        (16/32/48 multi-resolution)');
  console.log('  web/public/favicon.svg        (scalable source)');
  console.log('  web/public/apple-touch-icon.png (180x180)');
  console.log('  docs/factarium.png            (512x512, for the README)');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
