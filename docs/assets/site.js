(function () {
  var root = document.documentElement;
  var EN = window.CLIPTON_EN || {};
  var names = { ja: '日本語', en: 'English' };
  var store = {
    ja: 'https://apps.microsoft.com/detail/9nss9p4f6s5m?hl=ja-JP&gl=JP',
    en: 'https://apps.microsoft.com/detail/9nss9p4f6s5m?hl=en-US&gl=US'
  };
  var lightbox = null;

  function save(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* not persisted */ } }
  function meta(selector, value) { var element = document.querySelector(selector); if (element) element.content = value; }

  // Japanese is the markup itself; each element keeps it so switching back needs no reload.
  function localize(element, lang) {
    if (element._ja === undefined) element._ja = element.innerHTML;
    var value = lang === 'en' ? EN[element.getAttribute('data-i18n')] : element._ja;
    if (typeof value === 'string') element.innerHTML = value;
  }

  function caption(shot, lang, japanese) {
    var value = EN['cap.' + shot];
    return lang === 'en' && typeof value === 'string' ? value : japanese;
  }

  function applyLanguage(lang) {
    root.lang = lang;
    root.dataset.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach(function (element) { localize(element, lang); });
    document.querySelectorAll('img[data-shot]').forEach(function (image) {
      if (image._jaAlt === undefined) image._jaAlt = image.alt;
      image.src = 'images/site/' + image.getAttribute('data-shot') + '-' + lang + '.png';
      image.alt = caption(image.getAttribute('data-shot'), lang, image._jaAlt);
    });
    document.querySelectorAll('a[data-shot]').forEach(function (link) {
      if (link._jaCaption === undefined) link._jaCaption = link.getAttribute('data-description') || '';
      link.href = 'images/site/' + link.getAttribute('data-shot') + '-' + lang + '.png';
      link.setAttribute('data-description', caption(link.getAttribute('data-shot'), lang, link._jaCaption));
    });
    document.querySelectorAll('img[data-badge]').forEach(function (image) {
      image.src = 'images/ms-store-badge-' + lang + '-' + image.getAttribute('data-badge') + '.svg';
      image.alt = lang === 'en' ? 'Get it from Microsoft' : 'Microsoft Store から入手';
    });
    document.querySelectorAll('[data-store-link]').forEach(function (link) { link.href = store[lang]; });

    var descriptionMeta = document.querySelector('meta[name="description"]');
    if (descriptionMeta && descriptionMeta._ja === undefined) descriptionMeta._ja = descriptionMeta.content;
    var description = lang === 'en' ? EN['meta.description'] : descriptionMeta && descriptionMeta._ja;
    if (description) {
      meta('meta[name="description"]', description);
      meta('meta[property="og:description"]', description);
    }
    meta('meta[property="og:locale"]', lang === 'en' ? 'en_US' : 'ja_JP');

    var label = document.getElementById('lang-label');
    var button = document.getElementById('lang');
    if (label) { label.textContent = names[lang]; label.lang = lang; }
    if (button) button.setAttribute('aria-label', (lang === 'en' ? 'Language: ' : '言語: ') + names[lang]);
    document.querySelectorAll('#lang-list a[data-lang]').forEach(function (link) {
      var code = link.getAttribute('data-lang');
      link.href = location.pathname + '?lang=' + code + location.hash;
      if (code === lang) link.setAttribute('aria-current', 'true');
      else link.removeAttribute('aria-current');
    });
    themeLabel();

    if (window.GLightbox) {
      if (lightbox) lightbox.destroy();
      lightbox = GLightbox({ selector: '.glightbox', touchNavigation: true, loop: true });
    }
    root.classList.remove('en-pending');
  }

  function setUrlLanguage(lang) {
    try {
      var url = new URL(location.href);
      url.searchParams.set('lang', lang);
      history.replaceState(null, '', url.pathname + url.search + url.hash);
    } catch (e) { /* the address stays as it is */ }
  }

  var langButton = document.getElementById('lang');
  var langList = document.getElementById('lang-list');
  if (langButton && langList) {
    var links = function () { return Array.prototype.slice.call(langList.querySelectorAll('a')); };
    var setOpen = function (open, focusFirst) {
      langList.hidden = !open;
      langButton.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (open && focusFirst) (langList.querySelector('[aria-current]') || links()[0]).focus();
    };
    langButton.addEventListener('click', function () { setOpen(langList.hidden, false); });
    langButton.addEventListener('keydown', function (event) {
      if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setOpen(true, true); }
    });
    langList.addEventListener('keydown', function (event) {
      var items = links(), index = items.indexOf(document.activeElement);
      if (event.key === 'Escape') { setOpen(false); langButton.focus(); }
      else if (event.key === 'ArrowDown') { event.preventDefault(); items[(index + 1) % items.length].focus(); }
      else if (event.key === 'ArrowUp') { event.preventDefault(); items[(index - 1 + items.length) % items.length].focus(); }
    });
    langList.addEventListener('click', function (event) {
      var link = event.target.closest('a[data-lang]');
      if (!link) return;
      event.preventDefault();
      var lang = link.getAttribute('data-lang');
      save('clipton-language', lang);
      setUrlLanguage(lang);
      applyLanguage(lang);
      setOpen(false);
      langButton.focus();
    });
    document.addEventListener('click', function (event) {
      if (!langList.hidden && !event.target.closest('.langmenu')) setOpen(false);
    });
    langList.addEventListener('focusout', function (event) {
      if (!event.relatedTarget || !event.relatedTarget.closest('.langmenu')) setOpen(false);
    });
  }

  var themeButton = document.getElementById('theme');
  function currentTheme() {
    return root.dataset.theme || (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  }
  function themeLabel() {
    if (!themeButton) return;
    var english = root.lang === 'en';
    var toLight = currentTheme() === 'dark';
    themeButton.setAttribute('aria-label', english
      ? (toLight ? 'Switch to light mode' : 'Switch to dark mode')
      : (toLight ? 'ライトモードに切り替え' : 'ダークモードに切り替え'));
  }
  if (themeButton) {
    themeButton.addEventListener('click', function () {
      root.dataset.theme = currentTheme() === 'dark' ? 'light' : 'dark';
      save('clipton-theme', root.dataset.theme);
      themeLabel();
    });
  }

  // Highlights the sidebar entry for the section currently at the top of the window.
  var navLinks = Array.prototype.slice.call(document.querySelectorAll('aside a[href^="#"]'));
  var sections = navLinks.map(function (link) { return document.getElementById(link.getAttribute('href').slice(1)); });
  function markCurrent() {
    var current = 0;
    sections.forEach(function (section, index) {
      if (section && section.getBoundingClientRect().top <= 120) current = index;
    });
    navLinks.forEach(function (link, index) { link.classList.toggle('on', index === current); });
  }
  if (navLinks.length) {
    window.addEventListener('scroll', markCurrent, { passive: true });
    markCurrent();
  }

  applyLanguage(root.dataset.lang === 'en' ? 'en' : 'ja');
})();
