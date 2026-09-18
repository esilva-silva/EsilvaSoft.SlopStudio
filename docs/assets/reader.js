/* Local Markdown reader. Marked is distributed with its MIT license. */
(() => {
  const entries = window.DOCS_SNAPSHOT || [];
  const $ = selector => document.querySelector(selector);
  const body = $('#viewer-body');
  const normalize = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
  const escape = value => value.replace(/[&<>"']/g, c => ({'&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;'}[c]));
  const root = new URL('../', document.currentScript.src);
  let selected = '', category = 'Todos', request = null;
  function group(file) {
    // A pasta decide primeiro; o prefixo numérico só vale para os documentos transversais da raiz.
    if (file.startsWith('phases/')) return 'Fases';
    if (file.startsWith('backlog/')) return 'Backlog';
    if (file.startsWith('done/')) return 'Releases concluídas';
    if (file.startsWith('ui/')) return 'Produto';
    if (file.startsWith('auto-complite/')) return 'Editor e IA';
    const number = Number(file.slice(0, 2));
    if ([6,20,21,22,23,26].includes(number)) return 'Editor e IA';
    if ([5,7,10].includes(number)) return 'Arquitetura';
    if ([8,11,15,16].includes(number)) return 'Qualidade e validação';
    if ([9,12,24].includes(number)) return 'Fases';
    return 'Produto';
  }
  function renderIndex() {
    const query = normalize($('#search').value.trim());
    const visible = entries.filter(d => (category === 'Todos' || group(d.file) === category) && normalize(d.title + ' ' + d.file).includes(query));
    $('#grid').innerHTML = visible.map(d => `<button type="button" class="card${selected === d.file ? ' selected' : ''}" data-file="${escape(d.file)}" aria-current="${selected === d.file ? 'page' : 'false'}"><span class="tag">${escape(group(d.file))}</span><span class="card-title">${escape(d.title)}</span><span class="file">${escape(d.file)}</span></button>`).join('');
    $('#count').textContent = `${visible.length} de ${entries.length} documentos`;
    $('#empty').style.display = visible.length ? 'none' : 'block';
  }
  // Copy only the Markdown element vocabulary and explicitly safe attributes.
  // Raw HTML, event handlers and executable URL schemes never reach the page.
  function sanitize(html, file) {
    const parsed = new DOMParser().parseFromString(html, 'text/html');
    const allowed = new Set('P H1 H2 H3 H4 H5 H6 A IMG STRONG EM DEL CODE PRE BLOCKQUOTE UL OL LI TABLE THEAD TBODY TR TH TD HR BR INPUT'.split(' '));
    function copy(node) {
      if (node.nodeType === Node.TEXT_NODE) return document.createTextNode(node.textContent);
      if (node.nodeType !== Node.ELEMENT_NODE) return document.createTextNode('');
      if (!allowed.has(node.tagName)) return document.createTextNode(node.textContent);
      const el = document.createElement(node.tagName);
      if (node.tagName === 'INPUT') {
        el.type = 'checkbox'; el.disabled = true; el.checked = node.hasAttribute('checked');
      }
      if (node.tagName === 'OL' && /^\d+$/.test(node.getAttribute('start') || '')) el.start = Number(node.getAttribute('start'));
      if (node.tagName === 'IMG' || node.tagName === 'A') {
        const attr = node.tagName === 'IMG' ? 'src' : 'href';
        const raw = node.getAttribute(attr) || '';
        try {
          const url = new URL(raw, new URL(file, root));
          if (['http:', 'https:'].includes(url.protocol) || (url.protocol === 'file:' && url.href.startsWith(root.href)) || (attr === 'href' && url.protocol === 'mailto:')) el.setAttribute(attr, url.href);
        } catch { /* Invalid links remain plain labels. */ }
        if (node.tagName === 'IMG') { el.alt = node.getAttribute('alt') || ''; el.loading = 'lazy'; }
        else el.rel = 'noopener noreferrer';
      }
      if (['TH', 'TD'].includes(node.tagName)) {
        const alignment = node.style.textAlign;
        if (['left','right','center'].includes(alignment)) el.style.textAlign = alignment;
      }
      if (node.tagName === 'CODE' && /^language-[\w-]+$/.test(node.className)) el.className = node.className;
      for (const child of node.childNodes) el.append(copy(child));
      return el;
    }
    const fragment = document.createDocumentFragment();
    for (const node of parsed.body.childNodes) fragment.append(copy(node));
    return fragment;
  }
  function decorate(anchor) {
    body.querySelectorAll('table').forEach(table => {
      const wrapper = document.createElement('div'); wrapper.className = 'table-scroll';
      wrapper.tabIndex = 0; wrapper.setAttribute('role', 'region'); wrapper.setAttribute('aria-label', 'Tabela — role horizontalmente para ver todas as colunas');
      table.before(wrapper); wrapper.append(table);
    });
    const ids = new Map();
    const headings = [...body.querySelectorAll('h1,h2,h3,h4,h5,h6')];
    headings.forEach(h => {
      const base = h.textContent.toLowerCase().replace(/[^\p{L}\p{N}\s_-]/gu, '').trim().replace(/\s/g, '-');
      const n = ids.get(base) || 0; ids.set(base, n + 1); h.id = base + (n ? '-' + n : '');
    });
    $('#toc').innerHTML = headings.filter(h => ['H2','H3'].includes(h.tagName)).map(h => `<a class="${h.tagName.toLowerCase()}" href="#${encodeURIComponent(selected)}::${encodeURIComponent(h.id)}">${escape(h.textContent)}</a>`).join('');
    if (anchor) document.getElementById(anchor)?.scrollIntoView();
    else $('.content').scrollTop = 0;
  }
  async function load(file, anchor = '') {
    const entry = entries.find(d => d.file === file);
    if (!entry) return;
    request?.abort();
    const current = new AbortController(); request = current;
    selected = file; renderIndex();
    $('#viewer-title').textContent = entry.title;
    body.setAttribute('aria-busy', 'true'); body.textContent = 'Carregando…';
    $('#toc').replaceChildren();
    try {
      let content = entry.content;
      if (location.protocol !== 'file:') {
        const response = await fetch(new URL(file, root), { signal: current.signal });
        if (!response.ok) throw new Error(`Não foi possível carregar o documento (HTTP ${response.status}).`);
        content = await response.text();
      }
      if (current.signal.aborted) return;
      body.replaceChildren(sanitize(marked.parse(content, { gfm: true }), file));
      decorate(anchor);
    } catch (error) {
      if (!current.signal.aborted) body.textContent = error.message || 'Não foi possível carregar este documento.';
    } finally {
      if (!current.signal.aborted) body.setAttribute('aria-busy', 'false');
    }
  }
  function route() {
    try {
      const [file, anchor = ''] = location.hash.slice(1).split('::').map(decodeURIComponent);
      load(entries.some(d => d.file === file) ? file : 'README.md', anchor);
    } catch { load('README.md'); }
  }
  $('#grid').addEventListener('click', event => {
    const item = event.target.closest('[data-file]');
    if (item) location.hash = encodeURIComponent(item.dataset.file);
  });
  body.addEventListener('click', event => {
    const link = event.target.closest('a[href]');
    if (!link) return;
    const url = new URL(link.href);
    const entry = entries.find(d => new URL(d.file, root).href === url.href.split('#')[0]);
    if (entry) { event.preventDefault(); location.hash = encodeURIComponent(entry.file) + (url.hash ? '::' + url.hash.slice(1) : ''); }
  });
  $('#search').addEventListener('input', renderIndex);
  document.querySelectorAll('[data-category]').forEach(button => button.addEventListener('click', () => {
    category = button.dataset.category;
    document.querySelectorAll('[data-category]').forEach(b => { b.classList.toggle('active', b === button); b.setAttribute('aria-pressed', String(b === button)); });
    renderIndex();
  }));
  window.addEventListener('hashchange', route);
  route();
})();
