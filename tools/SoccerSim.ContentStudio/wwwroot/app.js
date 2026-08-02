'use strict';

// Content Studio front end. No framework and no build step on purpose: `dotnet run` is the
// only command needed to author content, and the whole UI is three files anyone can read.

const state = {
  category: null,
  rows: [],
  columns: [],
  refs: {},
  search: '',
};

const el = (id) => document.getElementById(id);

// ---------------------------------------------------------------- server calls

async function api(path, options) {
  const response = await fetch('/api/' + path, options);
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (!response.ok) {
    throw new Error(body && body.error ? body.error : `${response.status} ${response.statusText}`);
  }
  return body;
}

const jsonRequest = (method, payload) => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(payload),
});

// ---------------------------------------------------------------- flattening
// Rows are nested (players carry an `attributes` object) but a grid is flat, so the UI works
// in dotted leaf paths. Same convention as the CSV importer, which is what lets a user export,
// edit in a spreadsheet, and paste back without learning a second format.

function leafPaths(object, prefix, into) {
  for (const [name, value] of Object.entries(object)) {
    const path = prefix ? `${prefix}.${name}` : name;
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      leafPaths(value, path, into);
    } else if (!into.includes(path)) {
      into.push(path);
    }
  }
  return into;
}

function valueAt(object, path) {
  let node = object;
  for (const part of path.split('.')) {
    if (node === null || node === undefined) return '';
    node = node[part];
  }
  if (node === null || node === undefined) return '';
  return Array.isArray(node) ? node.join('|') : String(node);
}

function setAt(object, path, raw) {
  const parts = path.split('.');
  let node = object;
  for (let i = 0; i < parts.length - 1; i++) {
    if (typeof node[parts[i]] !== 'object' || node[parts[i]] === null) node[parts[i]] = {};
    node = node[parts[i]];
  }
  node[parts[parts.length - 1]] = coerce(path, raw, object);
}

// The server is the authority on types; this only has to avoid sending a string where the
// original row held a number, an array, or null.
function coerce(path, raw, original) {
  const previous = pathValue(original, path);
  if (raw === '') return Array.isArray(previous) ? [] : null;
  if (Array.isArray(previous) || raw.includes('|')) {
    return raw.split('|').map((s) => s.trim()).filter((s) => s.length > 0);
  }
  if (typeof previous === 'number') {
    const n = Number(raw);
    return Number.isNaN(n) ? raw : n;
  }
  if (typeof previous === 'boolean') return raw === 'true';
  if (previous === null || previous === undefined) {
    if (/^-?\d+$/.test(raw)) return Number(raw);
    if (/^-?\d*\.\d+$/.test(raw)) return Number(raw);
  }
  return raw;
}

function pathValue(object, path) {
  let node = object;
  for (const part of path.split('.')) {
    if (node === null || node === undefined) return undefined;
    node = node[part];
  }
  return node;
}

// A `*Key` column points at another category, so offer its keys instead of free text. The
// plural mapping is a convention, not a schema — an unknown target just falls back to a text box.
const REF_CATEGORIES = {
  leagueKey: 'leagues',
  teamKey: 'teams',
  nationKey: 'nations',
  stadiumKey: 'stadiums',
  competitionKey: 'competitions',
  playerKey: 'players',
  coachKey: 'coaches',
};

// ---------------------------------------------------------------- rendering

async function loadCategories() {
  const categories = await api('categories');
  const nav = el('categories');
  nav.innerHTML = '';
  for (const category of categories) {
    const link = document.createElement('a');
    link.dataset.category = category.name;
    link.className = category.name === state.category ? 'active' : '';
    link.innerHTML =
      `<span>${category.name.replace(/_/g, ' ')}</span>` +
      (category.errors > 0 ? `<span class="badge">${category.errors}</span>` : '') +
      `<span class="count">${category.count}</span>`;
    link.onclick = () => selectCategory(category.name);
    nav.appendChild(link);
  }
}

async function selectCategory(name) {
  state.category = name;
  state.search = '';
  el('search').value = '';
  el('category-title').textContent = name.replace(/_/g, ' ');
  el('csv-link').href = `/api/${name}/export.csv`;
  el('csv-link').setAttribute('download', `${name}.csv`);
  await loadRows();
  await loadCategories();
}

async function loadRows() {
  const query = state.search ? `?q=${encodeURIComponent(state.search)}` : '';
  state.rows = await api(`${state.category}${query}`);

  const columns = [];
  for (const row of state.rows) leafPaths(row, '', columns);
  // Key first — it is the row's identity and stays readable while scrolling sideways.
  state.columns = columns.sort((a, b) => (a === 'key' ? -1 : b === 'key' ? 1 : 0));
  renderGrid();
}

function renderGrid() {
  const head = el('grid-head');
  const body = el('grid-body');
  head.innerHTML = '';
  body.innerHTML = '';

  el('grid-empty').classList.toggle('hidden', state.rows.length > 0);

  for (const column of state.columns) {
    const th = document.createElement('th');
    th.textContent = column;
    head.appendChild(th);
  }
  head.appendChild(document.createElement('th'));

  for (const row of state.rows) body.appendChild(renderRow(row));
}

function renderRow(row) {
  const tr = document.createElement('tr');

  for (const column of state.columns) {
    const td = document.createElement('td');
    if (column === 'key') td.className = 'key';

    const refCategory = REF_CATEGORIES[column.split('.').pop()];
    const options = refCategory ? state.refs[refCategory] : null;

    let input;
    if (options && options.length > 0) {
      input = document.createElement('select');
      const blank = document.createElement('option');
      blank.value = '';
      blank.textContent = '—';
      input.appendChild(blank);
      for (const option of options) {
        const opt = document.createElement('option');
        opt.value = option;
        opt.textContent = option;
        input.appendChild(opt);
      }
      const current = valueAt(row, column);
      // A dangling reference must stay visible and editable rather than silently reset to
      // blank — that is exactly the state the user needs to see in order to fix it.
      if (current && !options.includes(current)) {
        const stale = document.createElement('option');
        stale.value = current;
        stale.textContent = `${current} (missing)`;
        input.appendChild(stale);
      }
      input.value = current;
    } else {
      input = document.createElement('input');
      input.value = valueAt(row, column);
      // `id` is assigned by the server and referenced by save files; showing it is useful,
      // editing it by hand is a foot-gun.
      if (column === 'id') input.readOnly = true;
    }

    input.onchange = () => saveCell(row, column, input.value, td);
    td.appendChild(input);
    tr.appendChild(td);
  }

  const actions = document.createElement('td');
  actions.className = 'row-actions';
  const remove = document.createElement('button');
  remove.type = 'button';
  remove.textContent = '✕';
  remove.title = `Delete ${row.key}`;
  remove.onclick = () => deleteRow(row);
  actions.appendChild(remove);
  tr.appendChild(actions);

  return tr;
}

async function saveCell(row, column, raw, td) {
  const previousKey = row.key;
  const updated = structuredClone(row);
  setAt(updated, column, raw);

  try {
    if (column === 'key') {
      // Renaming is delete-then-create, because references point at the key.
      await api(`${state.category}`, jsonRequest('POST', updated));
      await api(`${state.category}/${encodeURIComponent(previousKey)}`, { method: 'DELETE' });
      toast(`Renamed ${previousKey} → ${updated.key}. References to the old key now dangle.`);
    } else {
      await api(`${state.category}/${encodeURIComponent(previousKey)}`, jsonRequest('PUT', updated));
    }
    td.classList.remove('invalid');
    Object.assign(row, updated);
    await refresh();
  } catch (error) {
    td.classList.add('invalid');
    toast(error.message, true);
  }
}

async function deleteRow(row) {
  if (!confirm(`Delete ${row.key}?`)) return;
  try {
    await api(`${state.category}/${encodeURIComponent(row.key)}`, { method: 'DELETE' });
    await refresh();
  } catch (error) {
    toast(error.message, true);
  }
}

async function addRow() {
  const key = prompt('Key for the new entry (lowercase letters, digits, - _ .):');
  if (!key) return;

  // Seed from an existing row so required fields are present and the shape is right; a brand
  // new category falls back to just the key and lets the server report what is missing.
  const template = state.rows.length > 0 ? structuredClone(state.rows[0]) : {};
  blankOut(template);
  template.key = key;
  delete template.id;

  try {
    await api(state.category, jsonRequest('POST', template));
    await refresh();
    toast(`Added ${key}.`);
  } catch (error) {
    toast(error.message, true);
  }
}

function blankOut(object) {
  for (const [name, value] of Object.entries(object)) {
    if (Array.isArray(value)) object[name] = [];
    else if (value !== null && typeof value === 'object') blankOut(value);
    else if (typeof value === 'string') object[name] = name === 'key' ? '' : value;
  }
}

// ---------------------------------------------------------------- validation & build

async function refresh() {
  state.refs = await api('refs');
  await loadRows();
  await loadCategories();
  await runValidation();
}

async function runValidation() {
  const result = await api('validate');
  const panel = el('issues');
  panel.innerHTML = '';

  if (result.issues.length === 0) {
    panel.innerHTML = '<p class="issue-ok">No issues. Content is ready to build.</p>';
    return;
  }

  const errors = result.issues.filter((i) => i.severity === 'Error').length;
  const warnings = result.issues.length - errors;
  const summary = document.createElement('p');
  summary.className = 'muted';
  summary.textContent = `${errors} error(s), ${warnings} warning(s)`;
  panel.appendChild(summary);

  for (const issue of result.issues) {
    const div = document.createElement('div');
    div.className = `issue ${issue.severity}`;
    div.innerHTML =
      `<div class="where">${issue.category}${issue.entityKey ? ' / ' + issue.entityKey : ''}` +
      `${issue.field ? '.' + issue.field : ''}</div>` +
      `<div class="msg">${escapeHtml(issue.message)}</div>` +
      `<code>${issue.code}</code>`;
    div.onclick = () => jumpTo(issue);
    panel.appendChild(div);
  }
}

async function jumpTo(issue) {
  if (!issue.category || issue.category === state.category) return;
  try {
    await selectCategory(issue.category);
  } catch {
    // World-level issues (seasons, fixtures) have no editable grid yet.
  }
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

async function build() {
  try {
    const result = await api('build', { method: 'POST' });
    toast(`Built ${result.built}`);
  } catch (error) {
    toast(error.message, true);
  }
}

// ---------------------------------------------------------------- paste import

function openPaste() {
  el('paste-text').value = '';
  el('paste-result').innerHTML = '';
  el('paste-dialog').showModal();
}

async function importPaste(dryRun) {
  const text = el('paste-text').value;
  if (!text.trim()) return;

  const result = el('paste-result');
  try {
    const response = await fetch(`/api/${state.category}/import?dryRun=${dryRun}`, {
      method: 'POST',
      headers: { 'Content-Type': 'text/plain' },
      body: text,
    });
    const body = await response.json();

    if (body.errors && body.errors.length > 0) {
      result.innerHTML =
        '<div class="err">Nothing was imported. Fix these rows:</div>' +
        body.errors.map((e) => `<div class="err">line ${e.line}: ${escapeHtml(e.message)}</div>`).join('');
      return;
    }

    if (dryRun) {
      result.innerHTML = `<div class="ok">${body.wouldImport} row(s) parse cleanly. Press Import to apply.</div>`;
      return;
    }

    el('paste-dialog').close();
    await refresh();
    toast(`Imported ${body.imported} row(s).`);
  } catch (error) {
    result.innerHTML = `<div class="err">${escapeHtml(error.message)}</div>`;
  }
}

// ---------------------------------------------------------------- misc

let toastTimer = null;
function toast(message, isError) {
  const node = el('toast');
  node.textContent = message;
  node.className = isError ? 'error' : '';
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => node.classList.add('hidden'), isError ? 9000 : 3500);
}

async function start() {
  const manifest = await api('manifest');
  el('location').textContent = manifest.directory;

  state.refs = await api('refs');
  const categories = await api('categories');
  await selectCategory(categories[0].name);
  await runValidation();

  el('search').oninput = debounce(async (event) => {
    state.search = event.target.value;
    await loadRows();
  }, 200);

  el('add-btn').onclick = addRow;
  el('paste-btn').onclick = openPaste;
  el('validate-btn').onclick = runValidation;
  el('build-btn').onclick = build;
  el('paste-check').onclick = () => importPaste(true);
  el('paste-apply').onclick = () => importPaste(false);
}

function debounce(fn, ms) {
  let timer = null;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), ms);
  };
}

start().catch((error) => toast(error.message, true));
