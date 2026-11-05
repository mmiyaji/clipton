import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../docs/assets/site.js', import.meta.url), 'utf8');
const start = source.indexOf('  // Keep the selected section stable while a sidebar link');
const end = source.indexOf('  applyLanguage(root.dataset.lang', start);
assert.notEqual(start, -1, 'navigation implementation marker exists');
assert.notEqual(end, -1, 'navigation implementation ends before initial localization');
const applyLanguageBody = source.slice(source.indexOf('function applyLanguage'), start);
assert.match(applyLanguageBody, /root\.classList\.remove\('en-pending'\);\s*scheduleSelection\(\);/, 'language changes refresh section selection after replacing translated content');
const navigation = source.slice(start, end);

function makeClassList() {
  const classes = new Set();
  return {
    add(value) { classes.add(value); },
    remove(value) { classes.delete(value); },
    toggle(value, force) {
      const enabled = force === undefined ? !classes.has(value) : force;
      if (enabled) classes.add(value); else classes.delete(value);
      return enabled;
    },
    contains(value) { return classes.has(value); },
  };
}
function makeLink(href, extra = {}) {
  const listeners = {};
  const attributes = { href, ...extra };
  return {
    attributes,
    classList: makeClassList(),
    listeners,
    addEventListener(type, callback) { listeners[type] = callback; },
    getAttribute(name) { return attributes[name] ?? null; },
    setAttribute(name, value) { attributes[name] = value; },
    removeAttribute(name) { delete attributes[name]; },
  };
}

const ids = ['top', 'features', 'start', 'gallery', 'tips', 'faq', 'release'];
const sections = ids.map((id, index) => ({ id, top: index === 0 ? 0 : 1000 + index * 100 }));
const sectionById = Object.fromEntries(sections.map((section) => [section.id, section]));
const sectionLinks = ids.map((id) => makeLink(`#${id}`));
const documents = [
  makeLink('./privacy/', { target: '_blank' }),
  makeLink('./terms/', { download: '' }),
];
const asideLinks = [...sectionLinks, ...documents];
const document = {
  documentElement: { scrollHeight: 5000 },
  querySelectorAll(selector) {
    assert.equal(selector, 'aside a[href^="#"]');
    return asideLinks.filter((link) => link.getAttribute('href').startsWith('#'));
  },
  getElementById(id) { return sectionById[id] ?? null; },
};
let scrollY = 0;
let innerHeight = 800;
const windowListeners = {};
let nextTimerId = 1;
const timers = new Map();
const window = {
  get scrollY() { return scrollY; },
  get innerHeight() { return innerHeight; },
  addEventListener(type, callback) { windowListeners[type] = callback; },
};
const context = {
  document,
  window,
  location: { hash: '' },
  requestAnimationFrame(callback) { callback(); },
  setTimeout(callback, delay) {
    const id = nextTimerId++;
    timers.set(id, { callback, delay });
    return id;
  },
  clearTimeout(id) { timers.delete(id); },
};
function runTimers(delay) {
  const ready = [...timers.entries()].filter(([, timer]) => timer.delay === delay);
  for (const [id, timer] of ready) {
    if (!timers.has(id)) continue;
    timers.delete(id);
    timer.callback();
  }
}
function dispatchWindow(type, event = {}) { windowListeners[type]?.(event); }
function click(link, overrides = {}) {
  link.listeners.click({
    defaultPrevented: false,
    button: 0,
    ctrlKey: false,
    metaKey: false,
    shiftKey: false,
    altKey: false,
    ...overrides,
  });
}
function selectedId() {
  const selected = sectionLinks.filter((link) => link.classList.contains('on'));
  assert.equal(selected.length, 1, 'exactly one section link is selected');
  const current = sectionLinks.filter((link) => link.getAttribute('aria-current') === 'location');
  assert.equal(current.length, 1, 'exactly one section link has aria-current=location');
  assert.equal(current[0], selected[0], 'aria-current and visual selection agree');
  return selected[0].getAttribute('href').slice(1);
}
function setSectionTops(activeId) {
  sections.forEach((section, index) => { section.getBoundingClientRect = () => ({ top: section.id === activeId ? 0 : 500 + index * 100 }); });
}

setSectionTops('top');
vm.runInNewContext(navigation, context, { filename: 'docs/assets/site.js#navigation' });
assert.equal(selectedId(), 'top', 'initial section follows the viewport');
assert.equal(documents.some((link) => link.listeners.click), false, 'document links receive no navigation click handler');
assert.equal(documents.some((link) => link.getAttribute('aria-current')), false, 'document links are excluded from selected state');

// A regular section click selects immediately and stays selected across intermediate scroll events.
click(sectionLinks[1]);
assert.equal(selectedId(), 'features', 'clicked section selects immediately');
setSectionTops('faq');
dispatchWindow('scroll');
assert.equal(selectedId(), 'features', 'selected click is held during smooth scrolling');
runTimers(160);
assert.equal(selectedId(), 'faq', 'selection follows the viewport after scroll settles');

// Modifier clicks, non-primary clicks, downloads, and target=_blank links keep native browser behavior.
click(sectionLinks[2], { ctrlKey: true });
assert.equal(selectedId(), 'faq', 'Ctrl-click is not intercepted');
click(sectionLinks[2], { metaKey: true });
assert.equal(selectedId(), 'faq', 'Meta-click is not intercepted');
click(sectionLinks[2], { shiftKey: true });
assert.equal(selectedId(), 'faq', 'Shift-click is not intercepted');
click(sectionLinks[2], { altKey: true });
assert.equal(selectedId(), 'faq', 'Alt-click is not intercepted');
click(sectionLinks[2], { button: 1 });
assert.equal(selectedId(), 'faq', 'non-primary click is not intercepted');
assert.equal(documents[0].getAttribute('target'), '_blank');
assert.equal(documents[1].getAttribute('download'), '');

// Resize and language-triggered selection refreshes use current layout measurements.
setSectionTops('gallery');
dispatchWindow('resize');
assert.equal(selectedId(), 'gallery', 'resize refreshes the current section');
setSectionTops('tips');
// applyLanguage schedules the same selection refresh after replacing translated content.
vm.runInNewContext('scheduleSelection()', context);
assert.equal(selectedId(), 'tips', 'language layout refresh recalculates the current section');

// The last short section becomes current at the page bottom even when its heading cannot reach the threshold.
sections.forEach((section, index) => { section.getBoundingClientRect = () => ({ top: index === sections.length - 1 ? 200 : -500 }); });
scrollY = 4199;
innerHeight = 800;
document.documentElement.scrollHeight = 5000;
dispatchWindow('scroll');
assert.equal(selectedId(), 'release', 'last short section is selected at the page bottom');

console.log('Sidebar navigation behavior passed');
