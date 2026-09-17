// Usage: node tests/browser/startup.cjs <publish-directory>
const { chromium } = require('../../work-tests/browser/node_modules/playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const { spawn } = require('node:child_process');

async function verifyStaticAssets(directory, base) {
    const endpoints = JSON.parse(fs.readFileSync(
        path.join(directory, 'VideoNote.Server.staticwebassets.endpoints.json'))).Endpoints;
    const decoded = new Map();
    for (const item of endpoints) {
        const asset = path.join(directory, 'wwwroot', item.AssetFile);
        if (decoded.has(asset)) continue;
        let buffer = fs.readFileSync(asset);
        if (asset.endsWith('.br')) buffer = zlib.brotliDecompressSync(buffer);
        else if (asset.endsWith('.gz')) buffer = zlib.gunzipSync(buffer);
        decoded.set(asset, buffer);
        const original = asset.replace(/\.(br|gz)$/, '');
        if (original !== asset)
            assert.deepEqual(buffer, fs.readFileSync(original), `Compressed content: ${item.AssetFile}`);
    }
    let next = 0;
    await Promise.all(Array.from({ length: 8 }, async () => {
        while (next < endpoints.length) {
            const item = endpoints[next++];
            const selector = item.Selectors.find(s => s.Name === 'Content-Encoding');
            const response = await fetch(`${base}/${item.Route}`, {
                headers: { 'Accept-Encoding': selector?.Value || 'identity' },
                signal: AbortSignal.timeout(10000)
            });
            assert.equal(response.status, 200, `${item.Route}: HTTP status`);
            for (const name of ['Content-Type', 'Content-Encoding'])
                assert.equal(response.headers.get(name),
                    item.ResponseHeaders.find(h => h.Name === name)?.Value || null, `${item.Route}: ${name}`);
            assert.deepEqual(Buffer.from(await response.arrayBuffer()),
                decoded.get(path.join(directory, 'wwwroot', item.AssetFile)), `${item.Route}: response content`);
        }
    }));
    console.log(`PASS: all ${endpoints.length} static endpoint representations match published content, type and encoding.`);
}

(async () => {
    assert.ok(process.argv[2], 'Pass a published Server directory.');
    const directory = path.resolve(process.argv[2]);
    const storage = fs.mkdtempSync(path.join(directory, 'startup-test-'));
    let browser;
    let output = '';
    let base;
    const server = spawn('dotnet', ['VideoNote.Server.dll', '--urls', 'http://127.0.0.1:0'], {
        cwd: directory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
        env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production',
            Storage__RootPath: path.basename(storage), ConnectionStrings__VideoNote: 'Data Source=videonote.db' }
    });
    const exited = new Promise(resolve => server.once('exit', resolve));
    const collect = data => {
        output += data.toString();
        base ??= output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
    };
    server.stdout.on('data', collect);
    server.stderr.on('data', collect);
    let spawnError;
    server.on('error', error => { spawnError = error; });
    try {
        for (let i = 0; i < 150 && !base && server.exitCode === null && !spawnError; i++)
            await new Promise(resolve => setTimeout(resolve, 200));
        if (spawnError) throw spawnError;
        assert.ok(base, `Server did not start: ${output}`);
        const script = await fetch(`${base}/_framework/blazor.web.js`);
        assert.equal(script.status, 200, 'Blazor bootstrap must return HTTP 200.');
        assert.match(script.headers.get('content-type'), /javascript/);
        await verifyStaticAssets(directory, base);
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('response', response => {
            if (response.url().startsWith(`${base}/`) && response.status() >= 400)
                errors.push(`${response.status()} ${response.url()}`);
        });
        page.on('requestfailed', request => {
            if (request.url().startsWith(`${base}/`))
                errors.push(`${request.failure()?.errorText} ${request.url()}`);
        });
        await page.goto(base, { waitUntil: 'domcontentloaded' });
        await page.getByRole('heading', { name: '把视频放映成一份结构化解读' }).waitFor();
        await page.getByRole('link', { name: '查看任务列表', exact: true }).click();
        await page.waitForURL('**/tasks');
        await page.getByRole('heading', { name: '任务列表', exact: true }).waitFor();
        for (const [route, heading] of [
            ['/tasks/new', '新建解读任务'], ['/settings', '模型设置'], ['/prompts', '提示词模板']
        ]) {
            await page.goto(base + route, { waitUntil: 'networkidle' });
            await page.getByRole('heading', { name: heading, exact: true }).waitFor();
        }
        await page.goto(base + '/not-found', { waitUntil: 'networkidle' });
        await page.getByText('404 · 这一帧不存在', { exact: true }).waitFor();
        assert.deepEqual(errors, []);
        console.log('PASS: homepage, task list, new task, settings, prompts and not-found pages render without resource errors.');
    } finally {
        if (browser) await browser.close();
        if (server.exitCode === null && !spawnError) {
            server.kill();
            await exited;
        }
        assert.equal(path.dirname(path.resolve(storage)), directory);
        assert.ok(path.basename(storage).startsWith('startup-test-'));
        fs.rmSync(storage, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
