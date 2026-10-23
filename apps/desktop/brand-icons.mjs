import { chromium } from 'playwright-core';
import fs from 'node:fs/promises';
const browser = await chromium.launch({
  channel: 'chrome',
  headless: true,
  args: ['--disable-gpu'],
});
try {
  // Rasterize the unmodified official SVG with preserved proportions for native icon formats.
  const icon = await browser.newPage({
    viewport: { width: 256, height: 256 },
    deviceScaleFactor: 1,
  });
  const svg = await fs.readFile('public/brand/velvet-studio.svg', 'utf8');
  await icon.setContent(
    '<html><body style="margin:0;width:256px;height:256px;display:grid;place-items:center;background:transparent">' +
      svg.replace('<svg ', '<svg width="232" height="232" ') +
      '</body></html>',
  );
  await icon.screenshot({
    path: 'src-tauri/icons/icon.png',
    omitBackground: true,
  });
  const png = await fs.readFile('src-tauri/icons/icon.png');
  const header = Buffer.alloc(22);
  header.writeUInt16LE(1, 2); // ICO image type
  header.writeUInt16LE(1, 4); // one image; zero width/height bytes mean 256 px
  header.writeUInt16LE(1, 10);
  header.writeUInt16LE(32, 12);
  header.writeUInt32LE(png.length, 14);
  header.writeUInt32LE(22, 18);
  await fs.writeFile('src-tauri/icons/icon.ico', Buffer.concat([header, png]));
} finally {
  await browser.close();
}
