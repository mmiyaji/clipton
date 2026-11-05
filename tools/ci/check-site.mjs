import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import './test-site-navigation.mjs';

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

const home = readFileSync(path.join(root, 'index.html'), 'utf8');
const nav = home.match(/<aside\b[^>]*>([\s\S]*?)<\/aside>/)?.[1] ?? '';
const navTargets = [...nav.matchAll(/href="#([^"]+)"/g)].map(([, id]) => id);
const sectionIds = new Set([...home.matchAll(/<main\b[^>]*\bid="([^"]+)"|<h2\b[^>]*\bid="([^"]+)"/g)].map((match) => match[1] ?? match[2]));
if (!nav.includes('data-i18n="grp.guide"') || !nav.includes('data-i18n="grp.ref"') || !nav.includes('data-i18n="grp.docs"')) {
  problems.push('index.html: sidebar should have Guide, Reference, and Documents headings');
}
if (!nav.includes('href="#top"') || nav.indexOf('data-i18n="grp.guide"') > nav.indexOf('href="#top"')) {
  problems.push('index.html: Overview should be a link under Guide');
}
for (const id of navTargets) if (!sectionIds.has(id)) problems.push(`index.html: sidebar target #${id} does not exist`);

const navigationScript = readFileSync(path.join(root, 'assets/site.js'), 'utf8');
for (const [description, pattern] of [
  ['aria-current location state', /setAttribute\('aria-current', 'location'\)/],
  ['modified click handling', /event\.ctrlKey \|\| event\.metaKey \|\| event\.shiftKey \|\| event\.altKey/],
  ['smooth scroll settling', /setTimeout\(finishScroll, 160\)/],
  ['resize and load refresh', /addEventListener\('resize', scheduleSelection\)[\s\S]*addEventListener\('load', scheduleSelection\)/],
  ['history fragment refresh', /addEventListener\('hashchange'/],
]) if (!pattern.test(navigationScript)) problems.push(`assets/site.js: missing ${description}`);

if (problems.length) {
  console.error([...new Set(problems)].join('\n'));
  process.exit(1);
}
console.log(`Checked ${pages.length} pages and sidebar navigation in ${root}`);
