// Rasterizes vscode/icon.svg into vscode/icon.png, which is what the manifest points at.
//
// The Marketplace accepts PNG only, so this one derived file is unavoidable. It is committed
// rather than drawn at packaging time: `vsce package` would then need a browser on whatever
// machine ran it, and a publish that fails for want of Chrome fails at the worst moment.
//
// Run it after changing the drawing:
//
//     node vscode/tools/draw-icon.js
//
// A test holds the result to 128 by 128 and to being a PNG, which catches a run that half
// worked. It cannot catch a drawing that changed and was not redrawn - only reading this file
// or the comment in icon.svg says that, which is why both say it.

'use strict';

const { spawn } = require('node:child_process');
const { existsSync, mkdtempSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join, resolve } = require('node:path');

const here = resolve(__dirname, '..');
const drawing = join(here, 'icon.svg');
const out = join(here, 'icon.png');

// The Marketplace asks for at least 128 square. Larger is allowed and is what a listing scales
// down from on a high-density screen, but the mark is four flat shapes - there is no detail for
// the extra pixels to carry, and a heavier file would buy nothing.
const SIZE = 128;

/** Chrome, wherever this machine keeps it. The same list the site's card drawing uses. */
function browser() {
    if (process.env.CHROME_PATH) {
        return process.env.CHROME_PATH;
    }

    return ({
        win32: [
            'C:/Program Files/Google/Chrome/Application/chrome.exe',
            'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
        ],
        darwin: ['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'],
        linux: [
            '/usr/bin/google-chrome',
            '/usr/bin/google-chrome-stable',
            '/usr/bin/chromium',
            '/usr/bin/chromium-browser',
        ],
    }[process.platform] ?? []).find(existsSync);
}

const chrome = browser();

if (!chrome) {
    console.error('Chrome was not found. Set CHROME_PATH to it, or install it.');
    process.exit(1);
}

// --default-background-color takes RGBA, and all-zero is the transparency this needs: the disc
// has to sit on a light listing and a dark one, and a white square behind it would show on one
// of them.
const running = spawn(chrome, [
    '--headless',
    '--disable-gpu',
    '--hide-scrollbars',
    '--force-device-scale-factor=1',
    '--default-background-color=00000000',
    `--window-size=${SIZE},${SIZE}`,
    `--screenshot=${out}`,
    `--user-data-dir=${mkdtempSync(join(tmpdir(), 'icon-'))}`,
    `file://${drawing.replace(/\\/g, '/')}`,
], { stdio: ['ignore', 'pipe', 'pipe'] });

let said = '';
running.stderr.on('data', chunk => { said += chunk; });

running.on('exit', code => {
    if (code !== 0 || !existsSync(out)) {
        console.error(`Chrome exited ${code} and wrote nothing.`);
        console.error(said.split('\n').slice(0, 10).join('\n'));
        process.exit(1);
    }

    console.log(`drew ${out} at ${SIZE}x${SIZE}`);
});
