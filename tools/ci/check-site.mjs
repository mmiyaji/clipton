// Checks the static site in docs/ before it is deployed: every local file a page references must exist,
// and every data-i18n key used in the pages must have an English string.
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

const root = path.resolve(process.argv[2] ?? 'docs');
const pages = ['index.html', 'privacy/index.html', 'terms/index.html', 'snippet-variables/index.html'];
const problems = [];

const english = readFileSync(path.join(root, 'assets/i18n-en.js'), 'utf8');
const englishKeys = new Set(Object.keys(JSON.parse(english.slice(english.indexOf('{'), english.lastIndexOf('}') + 1))));

for (const page of pages) {
  const file = path.join(root, page);
  if (!existsSync(file)) {
    problems.push(`${page}: missing`);
    continue;
  }
  const html = readFileSync(file, 'utf8');
  for (const [, reference] of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    if (/^(?:[a-z]+:|#|\/\/)/i.test(reference)) continue;
    const target = path.resolve(path.dirname(file), reference.split(/[?#]/)[0]);
    const resolved = reference.split(/[?#]/)[0].endsWith('/') ? path.join(target, 'index.html') : target;
    if (!existsSync(resolved)) problems.push(`${page}: ${reference} does not exist`);
  }
  for (const [, shot] of html.matchAll(/data-shot="([^"]+)"/g)) {
    for (const lang of ['ja', 'en']) {
      if (!existsSync(path.join(root, 'images/site', `${shot}-${lang}.png`))) problems.push(`${page}: images/site/${shot}-${lang}.png does not exist`);
    }
  }
  for (const [, key] of html.matchAll(/data-i18n="([^"]+)"/g)) {
    if (!englishKeys.has(key)) problems.push(`${page}: no English text for ${key}`);
  }
}

if (problems.length) {
  console.error([...new Set(problems)].join('\n'));
  process.exit(1);
}
console.log(`Checked ${pages.length} pages in ${root}`);
