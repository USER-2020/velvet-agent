// Local QA only. Chrome calls the actual .NET Unix socket via a Playwright binding.
// No mock metrics or browser-to-service TCP endpoint is used.
import { chromium } from 'playwright-core';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';
const output = path.resolve('../../artifacts/ui');
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({
  channel: 'chrome',
  headless: true,
  args: ['--disable-gpu'],
});
const page = await browser.newPage({
  viewport: { width: 1100, height: 720 },
  deviceScaleFactor: 1.5,
});
const failures = [];
page.on('pageerror', (e) => failures.push(e.message));
let offline = false;
await page.exposeFunction('velvetNativeRequest', async (command, args) => {
  if (offline) throw Error('Offline QA');
  if (
    command !== 'agent_request' ||
    !['GetAgentStatus', 'GetConfiguration'].includes(args.operation)
  )
    throw Error('QA bridge: read-only operations');
  return await new Promise((resolve, reject) => {
    const socket = net.createConnection(
      path.join(os.tmpdir(), 'CoreFxPipe_velvet-agent-v1'),
    );
    let data = '';
    socket.setTimeout(12000, () => socket.destroy(Error('Timeout')));
    socket.on('connect', () =>
      socket.write(
        JSON.stringify({ Version: 1, Operation: args.operation }) + '\n',
      ),
    );
    socket.on('data', (chunk) => {
      data += chunk;
      if (data.includes('\n')) {
        try {
          resolve(JSON.parse(data.split('\n')[0]));
        } catch (e) {
          reject(e);
        }
        socket.destroy();
      }
    });
    socket.on('error', reject);
  });
});
await page.addInitScript(() => {
  window.__TAURI_INTERNALS__ = {
    invoke: (command, args) => window.velvetNativeRequest(command, args),
  };
});
try {
  await page.goto(process.env.VELVET_PREVIEW_URL ?? 'http://127.0.0.1:5187');
  await page.getByText('Modo de desarrollo', { exact: true }).first().waitFor();
  await page.evaluate(() => document.fonts.ready);
  for (const [width, height] of [
    [1100, 720],
    [850, 600],
    [1280, 800],
  ]) {
    await page.setViewportSize({ width, height });
    await page.reload();
    await page
      .getByText('Modo de desarrollo', { exact: true })
      .first()
      .waitFor();
    await page.evaluate(() => document.fonts.ready);
    const overflow = await page.evaluate(() => {
      const main = document.querySelector('main');
      return {
        document: document.documentElement.scrollWidth > innerWidth,
        main: main.scrollWidth > main.clientWidth,
      };
    });
    assert.equal(overflow.document, false, `Document overflow ${width}`);
    assert.equal(overflow.main, false, `Main overflow ${width}`);
    await page.screenshot({
      scale: 'css',
      path: path.join(output, `dashboard-${width}.png`),
    });
  }
  for (const name of [
    'Monitoreo',
    'OBS Studio',
    'Configuración',
    'Diagnósticos',
  ]) {
    await page.getByRole('button', { name, exact: true }).click();
    assert.equal(
      await page
        .locator('main')
        .evaluate((el) => el.scrollWidth > el.clientWidth),
      false,
      `Overflow ${name}`,
    );
    await page.screenshot({
      scale: 'css',
      path: path.join(output, `${name.replaceAll(' ', '-')}.png`),
    });
  }
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar diagnóstico' }).click();
  const download = await downloaded;
  await download.saveAs(path.join(output, 'diagnostics.json'));
  const diagnostics = JSON.parse(
    await fs.readFile(path.join(output, 'diagnostics.json'), 'utf8'),
  );
  assert.equal(diagnostics.schemaVersion, 1);
  assert.equal(diagnostics.serviceAvailable, true);
  assert.equal(diagnostics.agent.mode, 'development-adapter');
  assert.equal('Identity' in diagnostics.agent, false);
  offline = true;
  await page.getByRole('button', { name: 'Actualizar estado' }).click();
  await page.getByText('No podemos consultar el servicio local').waitFor();
  await page.screenshot({
    scale: 'css',
    path: path.join(output, 'service-offline.png'),
  });
  offline = false;
  await page.getByRole('button', { name: 'Reintentar', exact: true }).click();
  await page.getByText('Servicio activo', { exact: true }).waitFor();
  assert.equal(
    await page.getByText('No podemos consultar el servicio local').count(),
    0,
  );
  assert.deepEqual(failures, []);
  console.log(
    'QA OK: 3 sizes at 150% DPR, 5 views, no horizontal overflow, actual IPC data, export, disconnect/recovery, zero page errors.',
  );
  console.log(`Screenshots: ${output}`);
} finally {
  await browser.close();
}
