// Конвертация значков SVG → PNG для ленты Inventor (16 и 32 px, светлая и тёмная темы).
// Запуск: node resources/icons/render.js [папка для превью]
// Нужен Playwright с Chromium. Результат: src/InventorGostSpec.AddIn/Resources/Icons/*.png
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const srcDir = path.join(__dirname, 'src');
const outDir = path.join(__dirname, '..', '..', 'src', 'InventorGostSpec.AddIn', 'Resources', 'Icons');

// Нейтральные цвета, которые в тёмной теме Inventor заменяются; цветовые акценты остаются.
const darkPalette = {
  '#FFFFFF': '#4A515E', // бумага
  '#616161': '#D4D7DC', // контур листа, текст
  '#9E9E9E': '#A9AFB8', // графы
  '#C7C7C7': '#6E7684', // строки
  '#1C5F9E': '#7DB8F0', // синий контур
};

function toDark(svg) {
  return svg.replace(/#[0-9A-Fa-f]{6}/g, c => darkPalette[c.toUpperCase()] ?? c);
}

async function render(page, svg, size) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<html><body style="margin:0;background:transparent">${svg.replace('<svg ', '<svg style="display:block" ')}</body></html>`);
  return page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  const files = fs.readdirSync(srcDir).filter(f => f.endsWith('.svg')).sort();
  const names = [];

  for (const file of files) {
    const size = Number(/-(\d+)\.svg$/.exec(file)[1]);
    const base = file.replace(/\.svg$/, '');
    const svg = fs.readFileSync(path.join(srcDir, file), 'utf8');
    fs.writeFileSync(path.join(outDir, `${base}.png`), await render(page, svg, size));
    fs.writeFileSync(path.join(outDir, `${base}-dark.png`), await render(page, toDark(svg), size));
    names.push(base);
  }

  const previewDir = process.argv[2];
  if (previewDir) {
    // Превью: фактический размер и увеличение ×6 на фоне светлой и тёмной ленты.
    const icons = [...new Set(names.map(n => n.replace(/-\d+$/, '')))];
    const cell = (file, scale, bg) =>
      `<td style="background:${bg};padding:8px"><img src="data:image/png;base64,${fs.readFileSync(path.join(outDir, file)).toString('base64')}" style="image-rendering:pixelated;zoom:${scale}"></td>`;
    const rows = ['', '-dark'].map(theme => {
      const bg = theme ? '#3B4452' : '#F5F5F5';
      return icons.map(icon =>
        `<tr>${cell(`${icon}-32${theme}.png`, 1, bg)}${cell(`${icon}-16${theme}.png`, 1, bg)}${cell(`${icon}-32${theme}.png`, 6, bg)}${cell(`${icon}-16${theme}.png`, 6, bg)}</tr>`).join('');
    }).join('');
    await page.setViewportSize({ width: 800, height: 1800 });
    await page.setContent(`<html><body style="margin:0"><table style="border-collapse:collapse">${rows}</table></body></html>`);
    await page.waitForTimeout(200);
    await page.screenshot({ path: path.join(previewDir, 'icons-preview.png'), fullPage: true });
  }

  await browser.close();
  console.log(`Rendered ${files.length} icons to ${outDir}`);
})();
