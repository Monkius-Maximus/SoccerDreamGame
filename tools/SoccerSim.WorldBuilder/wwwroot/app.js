/*
 * Ferramenta de Mundo — read surface (Sprint 3).
 *
 * This file renders; it does not derive. Squad overall, invariant levels and the
 * geographic breadcrumb are computed by SoccerSim.Core and arrive over the API,
 * so the browser and the game can never disagree about how strong a club is or
 * whether it passes its checks. The only maths here is presentation: how to
 * format a number, and which colours a kit's fabric pattern needs.
 */

'use strict';

const state = {
  clubs: [],          // rail rows, from /api/clubs
  world: null,        // counts, enums, calibration
  fields: null,       // /api/fields — the form definition AND the server's validation surface
  clubId: null,
  page: null,         // /api/clubs/{id}
  squad: [],
  player: null,       // /api/characters/{id}, when the modal is open
  filters: { text: '', band: '', city: '' },
  squadSort: 'ovr',
  gen: null,          // the generator panel: { options, preview } while it is open
  exchange: null,     // the export/import dialog: { manifest, files, preview, error }
};

// ---------------------------------------------------------------- formatting

/** Escapes text before it goes into innerHTML — club names, mottos and audit
 *  notes contain quotes and angle brackets, and a broken page is a data bug
 *  that looks like a rendering bug. */
function esc(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

const ptBR = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 });

function fmtInt(n) {
  if (n === null || n === undefined || n === '') return '—';
  return ptBR.format(n);
}

function decimal(n, places) {
  if (n === null || n === undefined) return '—';
  return Number(n).toFixed(places).replace('.', ',');
}

function fmtEur(n) {
  if (n === null || n === undefined) return '—';
  if (n >= 1e6) return '€' + decimal(n / 1e6, n >= 1e7 ? 0 : 1) + 'M';
  if (n >= 1e3) return '€' + Math.round(n / 1e3) + 'k';
  return '€' + fmtInt(n);
}

function fmtBrl(n) {
  if (n === null || n === undefined) return '—';
  if (n >= 1e6) return 'R$' + decimal(n / 1e6, 1) + 'M';
  if (n >= 1e3) return 'R$' + Math.round(n / 1e3) + 'k';
  return 'R$' + fmtInt(n);
}

// ------------------------------------------------------------------- colours

/** WCAG relative luminance (ALGORITHMS.md §1.3). Used only to pick legible ink
 *  over a club's own colour — the stored luminance value comes from the server. */
function relLuminance(hex) {
  const parsed = /^#?([0-9a-f]{6})$/i.exec(String(hex || ''));
  if (!parsed) return 1;
  const int = parseInt(parsed[1], 16);
  const channels = [(int >> 16) & 255, (int >> 8) & 255, int & 255].map((c) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

/** Dark ink over a light club colour, white over a dark one. The cut is 0.55 on
 *  a chip and 0.5 on a palette swatch — the swatch is bigger, so it tolerates
 *  less contrast (design README, §Club page). */
function inkOn(hex, threshold) {
  return relLuminance(hex) > threshold ? '#1d1f20' : '#ffffff';
}

const SHIELD_CLIP = {
  Heater: 'polygon(0% 0%, 100% 0%, 100% 52%, 50% 100%, 0% 52%)',
  Ogival: 'polygon(50% 0%, 100% 26%, 100% 66%, 50% 100%, 0% 66%, 0% 26%)',
  Square: 'none',
  Round: 'none',
  Oval: 'none',
};
const SHIELD_RADIUS = { Round: '50%', Oval: '50% / 62%', Heater: '0', Square: '0', Ogival: '0' };

/** The seven fabric patterns as CSS gradients (ALGORITHMS.md §7.2). `main` is
 *  the shirt colour, `alt` the contrast: the secondary on the home kit, the
 *  primary on the away one. */
function patternBackground(pattern, main, alt) {
  const a = main || '#888888';
  const b = alt || '#ffffff';
  switch (pattern) {
    case 'VerticalStripes': return `repeating-linear-gradient(90deg, ${a} 0 11%, ${b} 11% 22%)`;
    case 'HorizontalStripes': return `repeating-linear-gradient(180deg, ${a} 0 9%, ${b} 9% 18%)`;
    case 'Pinstripes': return `repeating-linear-gradient(90deg, ${a} 0 6%, ${b} 6% 7.5%)`;
    case 'Sash': return `linear-gradient(58deg, ${a} 0 38%, ${b} 38% 58%, ${a} 58% 100%)`;
    case 'ContrastSleeves': return `linear-gradient(90deg, ${b} 0 20%, ${a} 20% 80%, ${b} 80% 100%)`;
    case 'Checks': return `repeating-conic-gradient(${a} 0% 25%, ${b} 25% 50%) 0 0 / 26% 22%`;
    default: return a;
  }
}

const LEVEL_CLASS = { Ok: 'ok', Warning: 'warning', Error: 'error' };
const LEVEL_LABEL = { Ok: 'ok', Warning: 'aviso', Error: 'erro' };

/** Squad roles are stored under ASCII schema names (a C# enum member cannot be
 *  "Rotação"), but the tool is in Portuguese and the user wrote these words.
 *  The schema keeps its spelling; the screen keeps theirs. */
const SQUAD_ROLE_LABEL = { Titular: 'Titular', Rotacao: 'Rotação', Reserva: 'Reserva', Promessa: 'Promessa' };

function squadRoleLabel(role) {
  return SQUAD_ROLE_LABEL[role] || role;
}

// ---------------------------------------------------------------------- data

async function getJson(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} → ${response.status}`);
  return response.json();
}

/**
 * Sends one field change. Writes are immediate — there is no Save button — so the
 * result of a patch is either a new version or a reason, and the caller shows the
 * reason on the field that caused it.
 *
 * Returns { ok: true, version, pendingEdits } or { ok: false, status, message }.
 */
async function sendPatch(url, path, value, version, method = 'PATCH') {
  const response = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path, value, version }),
  });

  if (response.ok) {
    const body = await response.json();
    setPending(body.pendingEdits);
    return { ok: true, ...body };
  }

  let message = `${response.status}`;
  try {
    const body = await response.json();
    message = body.error || message;
  } catch {
    // A response without a JSON body (a 500 page, a dropped connection) still needs to say
    // something the user can act on.
  }

  return { ok: false, status: response.status, message };
}

function setPending(count) {
  const element = document.getElementById('pending');
  element.dataset.pending = count > 0;
  document.getElementById('pending-label').textContent =
    count > 0 ? `${count} não exportada${count === 1 ? '' : 's'}` : 'em dia';
}

let toastTimer = null;

function toast(message) {
  const element = document.getElementById('toast');
  element.textContent = message;
  element.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { element.hidden = true; }, 6000);
}

// ------------------------------------------------------------------ the rail

function filteredClubs() {
  const { text, band, city } = state.filters;
  const needle = text.trim().toLowerCase();

  return state.clubs.filter((club) => {
    if (band && club.prestigeBand !== band) return false;
    if (city && club.cityName !== city) return false;
    if (!needle) return true;
    return [club.officialName, club.shortName, club.displayCode, club.cityName, club.clubId]
      .some((field) => String(field || '').toLowerCase().includes(needle));
  });
}

function renderRail() {
  const clubs = filteredClubs();
  document.getElementById('rail-count').textContent = `${clubs.length} clubes`;

  document.getElementById('rail-list').innerHTML = clubs.map((club) => `
    <button class="rail-row" type="button" data-club="${esc(club.clubId)}"
            aria-current="${club.clubId === state.clubId}">
      <span class="rail-chip" style="background: ${esc(club.primaryColor)}; color: ${inkOn(club.primaryColor, 0.55)}">
        ${esc(club.displayCode)}
      </span>
      <span class="rail-main">
        <span class="rail-name">${esc(club.shortName)}</span>
        <span class="rail-sub">${esc(club.cityName)} · ${esc(club.prestigeBand)}</span>
      </span>
      <span class="rail-right">
        <span class="rail-ovr">${club.squadOverall || '—'}</span>
        <span class="rail-flag level-${LEVEL_CLASS[club.invariantLevel]}">${LEVEL_LABEL[club.invariantLevel]}</span>
      </span>
    </button>`).join('');
}

function renderFilters() {
  const bands = state.world.enums.PrestigeBand;
  const cities = [...new Set(state.clubs.map((club) => club.cityName))].sort((a, b) => a.localeCompare(b, 'pt-BR'));

  document.getElementById('filter-band').innerHTML =
    '<option value="">Todas as bandas</option>' +
    bands.map((band) => `<option value="${esc(band)}">Banda ${esc(band)}</option>`).join('');

  document.getElementById('filter-city').innerHTML =
    '<option value="">Todas as cidades</option>' +
    cities.map((city) => `<option value="${esc(city)}">${esc(city)}</option>`).join('');
}

// ------------------------------------------------------------- the club page

function crestHtml(club, size) {
  const shape = club.crest.shieldShape;
  const clip = SHIELD_CLIP[shape] === 'none' ? '' : `clip-path: ${SHIELD_CLIP[shape]};`;
  const radius = SHIELD_RADIUS[shape] || '0';

  return `
    <div class="crest" style="width: ${size}px; height: ${size}px; ${clip} border-radius: ${radius}">
      <div class="crest-field" style="background: ${esc(club.palette.primary)}"></div>
      <div class="crest-band" style="background: ${esc(club.palette.secondary)};
           border-top: 1px solid ${esc(club.palette.tertiary)};
           border-bottom: 1px solid ${esc(club.palette.tertiary)}"></div>
      <div class="crest-code" style="font-size: ${Math.round(size * 0.19)}px;
           color: ${inkOn(club.palette.secondary, 0.5)}">${esc(club.displayCode)}</div>
    </div>`;
}

function kitHtml(club, which, size) {
  const kit = club.kits[which];
  const alt = which === 'home' ? club.palette.secondary : club.palette.primary;
  const background = patternBackground(kit.fabricPattern, kit.shirt, alt);

  return `
    <div class="kit">
      <div class="kit-shirt" style="width: ${size}px; height: ${Math.round(size * 1.05)}px; background: ${background}"></div>
      <div class="kit-lower">
        <div class="kit-shorts" style="width: ${Math.round(size * 0.42)}px; height: ${Math.round(size * 0.3)}px; background: ${esc(kit.shorts)}"></div>
        <div class="kit-socks" style="width: ${Math.round(size * 0.2)}px; height: ${Math.round(size * 0.3)}px; background: ${esc(kit.socks)}"></div>
      </div>
      <div>
        <div class="kit-label">${which === 'home' ? 'Titular' : 'Reserva'}</div>
        <div class="kit-pattern">${esc(kit.fabricPattern)}</div>
      </div>
    </div>`;
}

function metricsHtml(page) {
  const club = page.club;
  const metrics = page.metrics;
  const threshold = club.kits.deltaEThreshold;
  const deltaEBelow = club.kits.deltaE < threshold;

  const cells = [
    ['OVR (11 melhores)', metrics.overall || '—', `força ${decimal(club.world.clubStrength, 2)}`],
    ['Valor de elenco', fmtEur(metrics.totalMarketValueEur), `${metrics.playerCount} jogadores`],
    ['Folha mensal', fmtBrl(metrics.totalMonthlyWageBrl),
      `média ${fmtBrl(metrics.playerCount ? metrics.totalMonthlyWageBrl / metrics.playerCount : 0)}`],
    ['Capacidade', fmtInt(club.stadium.capacity), club.stadium.atmosphereArchetype],
    ['Vantagem de casa', `+${decimal(club.aiProfile.homeAdvantageModifier, 2)}`, 'homeAdv'],
    ['ΔE dos kits', decimal(club.kits.deltaE, 1), `limite ${decimal(threshold, 0)}`, deltaEBelow],
    ['Banda', club.world.prestigeBand, `mult ${decimal(page.bandValueMult, 4)}`],
  ];

  return `<div class="metrics">${cells.map(([label, value, note, alarm]) => `
    <div class="metric">
      <span class="metric-label">${esc(label)}</span>
      <span class="metric-value"${alarm ? ' style="color: var(--level-error-text)"' : ''}>${esc(value)}</span>
      <span class="metric-note">${esc(note)}</span>
    </div>`).join('')}</div>`;
}

function squadSummaryHtml(page) {
  const metrics = page.metrics;
  const cells = state.world.enums.Position.map((position) => [position, metrics.countByPosition[position]]);

  cells.push(
    ['Idade média', metrics.playerCount ? decimal(metrics.averageAge, 1) : '—'],
    ['Titulares', metrics.countByRole.Titular],
    ['Promessas', metrics.countByRole.Promessa],
    ['Ancorados', metrics.anchoredCount],
  );

  return `<div class="squad-summary">${cells.map(([key, value]) => `
    <div class="summary-cell">
      <span class="summary-key">${esc(key)}</span>
      <span class="summary-value">${esc(value)}</span>
    </div>`).join('')}</div>`;
}

const SQUAD_SORTS = {
  ovr: (a, b) => b.overall - a.overall,
  pos: (a, b) => {
    const order = state.world.enums.Position;
    return (order.indexOf(a.primaryPosition) - order.indexOf(b.primaryPosition)) || (b.overall - a.overall);
  },
  age: (a, b) => a.age - b.age,
  value: (a, b) => b.marketValueEur - a.marketValueEur,
  shirt: (a, b) => a.shirtNumber - b.shirtNumber,
};

function squadTableHtml() {
  const rows = state.squad.slice().sort(SQUAD_SORTS[state.squadSort] || SQUAD_SORTS.ovr);

  return `
    <table class="table squad-table">
      <thead>
        <tr>
          <th class="num" style="width: 34px">#</th>
          <th>Jogador</th>
          <th style="width: 52px">Pos</th>
          <th style="width: 84px">Papel</th>
          <th class="num" style="width: 44px">Idade</th>
          <th class="num" style="width: 44px">OVR</th>
          <th class="num" style="width: 44px">POT</th>
          <th class="num" style="width: 96px">Valor</th>
          <th class="num" style="width: 104px">Salário/mês</th>
          <th style="width: 74px">Origem</th>
        </tr>
      </thead>
      <tbody>
        ${rows.map((player) => `
          <tr class="squad-row" data-player="${esc(player.playerId)}" style="cursor: pointer">
            <td class="num"><span class="squad-shirt">${player.shirtNumber}</span></td>
            <td>
              <span class="squad-name">${esc(player.firstName)} ${esc(player.lastName)}</span>
              <span class="squad-nat">${esc(player.nationality)}</span>
            </td>
            <td><span class="squad-pos">${esc(player.primaryPosition)}</span></td>
            <td class="squad-role">${esc(squadRoleLabel(player.squadRole))}</td>
            <td class="num">${player.age}</td>
            <td class="num squad-ovr">${player.overall}</td>
            <td class="num squad-pot">${player.potentialOverall > player.overall
              ? '+' + (player.potentialOverall - player.overall) : '—'}</td>
            <td class="num">${esc(fmtEur(player.marketValueEur))}</td>
            <td class="num">${esc(fmtBrl(player.salaryMonthlyBrl))}</td>
            <td class="squad-origin">${player.provenance === 'Anchored' ? 'âncora' : 'regen'}</td>
          </tr>`).join('')}
      </tbody>
    </table>`;
}

// ------------------------------------------------------------ squad generator

const FORMATIONS = ['4-3-3', '4-2-3-1', '4-4-2', '3-5-2'];

const AGE_PROFILES = [['Balanced', 'Equilibrado'], ['Young', 'Jovem'], ['Experienced', 'Experiente']];

const XI_LINES = ['Goleiro', 'Defesa', 'Meio', 'Frente'];

/** A position with seven players fills its bar. Above that the bar simply stays full —
 *  the number to its right is the fact, the bar is only the shape. */
const BAR_FULL = 7;

function genControl(label, hint, control) {
  return `
    <label class="gen-control">
      <span class="gen-control-label">${esc(label)}</span>
      ${control}
      <span class="gen-control-hint">${esc(hint)}</span>
    </label>`;
}

function generatorControlsHtml() {
  const options = state.gen.options;
  const club = state.page.club;

  const select = (key, entries) => `
    <select class="tpin" data-gen="${key}">
      ${entries.map(([value, label]) => `
        <option value="${esc(value)}"${options[key] === value ? ' selected' : ''}>${esc(label)}</option>`).join('')}
    </select>`;

  return `
    <div class="gen-controls">
      ${genControl('Tamanho do elenco', 'entre 28 e 40 jogadores',
        `<input class="tpin" type="number" min="28" max="40" step="1" data-gen="squadSize" value="${options.squadSize}">`)}

      ${genControl('Formação do XI', 'define quantos de cada posição são titulares',
        select('formation', FORMATIONS.map((formation) => [formation, formation])))}

      ${genControl('OVR alvo', `derivado da força ${decimal(club.world.clubStrength, 2)}`,
        `<input class="tpin" type="number" min="40" max="95" step="1" data-gen="targetOverall" value="${options.targetOverall}">`)}

      ${genControl('Perfil de idade', 'desloca a curva etária do elenco inteiro',
        select('ageProfile', AGE_PROFILES))}

      ${genControl('Semente', 'mesma semente, mesmo elenco',
        `<input class="tpin" type="number" min="1" step="1" data-gen="seed" value="${options.seed}">`)}

      <button type="button" class="btn btn-ghost" id="gen-reseed">↺ Nova semente</button>

      <div class="gen-bars">
        ${state.world.enums.Position.map((position) => {
          const count = state.gen.preview.composition[position] || 0;
          return `
            <div class="gen-bar-row">
              <span class="gen-bar-key">${esc(position)}</span>
              <span class="gen-bar-track">
                <span class="gen-bar-fill" style="width: ${Math.min(100, (count / BAR_FULL) * 100)}%"></span>
              </span>
              <span class="gen-bar-count">${count}</span>
            </div>`;
        }).join('')}
      </div>
    </div>`;
}

function generatorPreviewHtml() {
  const preview = state.gen.preview;
  const metrics = preview.metrics;

  const cells = [
    ['OVR do XI', preview.xiOverall, `alvo ${state.gen.options.targetOverall}`],
    ['OVR do elenco', metrics.averageOverall, `${metrics.playerCount} jogadores`],
    ['Valor', fmtEur(metrics.totalMarketValueEur), 'somado'],
    ['Folha mensal', fmtBrl(metrics.totalMonthlyWageBrl), 'somada'],
    ['Idade média', decimal(metrics.averageAge, 1), state.gen.options.ageProfile === 'Balanced'
      ? 'perfil equilibrado' : 'perfil deslocado'],
    ['Promessas', metrics.countByRole.Promessa, 'papel Promessa'],
  ];

  return `
    <div class="gen-preview">
      <div class="metrics gen-metrics">${cells.map(([label, value, note]) => `
        <div class="metric">
          <span class="metric-label">${esc(label)}</span>
          <span class="metric-value">${esc(value)}</span>
          <span class="metric-note">${esc(note)}</span>
        </div>`).join('')}</div>

      <div class="gen-pitch">
        ${XI_LINES.map((label, line) => `
          <div class="gen-line" aria-label="${esc(label)}">
            ${preview.startingXi.filter((slot) => slot.line === line).map((slot) => `
              <span class="gen-slot">
                <span class="gen-slot-pos">${esc(slot.position)}</span>
                <span class="gen-slot-name">${esc(slot.lastName)}</span>
                <span class="gen-slot-ovr">${slot.overall}</span>
              </span>`).join('')}
          </div>`).join('')}
      </div>
    </div>`;
}

/**
 * The panel. Nothing here has been written: the squad on screen exists only in this
 * response, and the same seed produces it again when the user confirms.
 */
function generatorHtml() {
  const preview = state.gen.preview;
  const replacing = preview.existingPlayers > 0;

  const note = replacing
    ? `Isto substitui os ${preview.existingPlayers} jogadores atuais do clube${preview.existingAnchored > 0
        ? `, dos quais ${preview.existingAnchored} ${preview.existingAnchored === 1 ? 'está ancorado' : 'estão ancorados'}
           em pessoas reais — essa pesquisa se perde e não volta.`
        : '.'}`
    : 'Todos os jogadores gerados nascem com proveniência Regen e sem âncora: nenhum deles afirma '
      + 'nada sobre uma pessoa real.';

  return `
    <div class="card blueprint gen-panel">
      <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
      <div class="card-kicker">Gerador de elenco · generator</div>

      <div class="gen-grid">
        ${generatorControlsHtml()}
        ${generatorPreviewHtml()}
      </div>

      <div class="gen-foot">
        <button type="button" class="btn btn-primary" id="gen-write"${state.gen.busy ? ' disabled' : ''}>
          ${replacing ? 'Substituir elenco' : 'Gravar elenco'}
        </button>
        <button type="button" class="btn btn-secondary" id="gen-cancel">Cancelar</button>
        <span class="gen-note${replacing && preview.existingAnchored > 0 ? ' gen-note-alarm' : ''}">${esc(note)}</span>
      </div>
    </div>`;
}

function clubPageHtml(page) {
  const club = page.club;
  const healthLabel = { Ok: 'invariantes ok', Warning: 'com avisos', Error: 'com erros' }[page.invariantLevel];
  const capacityOutsideProfile = page.stadiumProfile
    && (club.stadium.capacity < page.stadiumProfile.min || club.stadium.capacity > page.stadiumProfile.max);

  return `
    <div class="page">
      <div class="club-header">
        ${crestHtml(club, 92)}
        <div style="flex: 1; min-width: 0">
          <div class="club-id">${esc(club.clubId)}</div>
          <h1 class="club-name">${esc(club.identity.officialName)}</h1>
          <div class="club-meta">
            <span>“${esc(club.identity.nickname)}”</span>
            <span class="sep">·</span>
            <span>fund. ${club.identity.foundingYear}</span>
            <span class="sep">·</span>
            <span>${esc(club.geography.cityName)} / ${esc(club.geography.uf)}</span>
            <span class="sep">·</span>
            <span>${esc(page.geoPath.join(' › '))}</span>
          </div>
          <div class="club-tags">
            <span class="tag tag-accent">Banda ${esc(club.world.prestigeBand)}</span>
            <span class="tag tag-outline">${esc(club.geography.districtArchetype)}</span>
            <span class="tag tag-outline">${esc(club.aiProfile.defaultTacticalStyle)}</span>
            <span class="tag tag-outline">${esc(club.world.namingRule)}</span>
            <span class="health-tag badge-${LEVEL_CLASS[page.invariantLevel]}">${esc(healthLabel)}</span>
          </div>
        </div>
      </div>

      ${metricsHtml(page)}

      <div class="card-pair">
        <div class="card blueprint">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <div class="card-kicker">Uniformes · kits</div>
          <div class="kits">
            ${kitHtml(club, 'home', 74)}
            ${kitHtml(club, 'away', 74)}
            <div class="kit-facts">
              <div class="factline">
                <span class="factline-key">Gola / caimento</span>
                <span>${esc(club.kits.collarStyle)} / ${esc(club.kits.fitStyle)}</span>
              </div>
              <div class="factline">
                <span class="factline-key">ΔE titular × reserva</span>
                <span${club.kits.deltaE < club.kits.deltaEThreshold ? ' class="level-error"' : ''}>
                  ${decimal(club.kits.deltaE, 1)} (limite ${decimal(club.kits.deltaEThreshold, 0)})
                </span>
              </div>
              <div class="factline">
                <span class="factline-key">Luminância titular</span>
                <span>${decimal(club.kits.home.luminance, 4)}</span>
              </div>
              <div class="kit-pattern" style="text-align: left; line-height: 1.4">${esc(club.kits.polarityRule)}</div>
            </div>
          </div>
        </div>

        <div class="card blueprint">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <div class="card-kicker">Paleta · palette</div>
          <div class="swatches">
            ${[['Primária', club.palette.primary], ['Secundária', club.palette.secondary], ['Terciária', club.palette.tertiary]]
              .map(([role, hex]) => {
                const ink = inkOn(hex, 0.5);
                return `<div class="swatch" style="background: ${esc(hex)}">
                  <span class="swatch-role" style="color: ${ink}">${esc(role)}</span>
                  <span class="swatch-hex" style="color: ${ink}">${esc(String(hex).toUpperCase())}</span>
                </div>`;
              }).join('')}
          </div>
          <div class="palette-facts">
            <div class="factline"><span class="factline-key">Tipografia</span><span>${esc(club.palette.typographyStyle)}</span></div>
            <div class="factline"><span class="factline-key">Forma do escudo</span><span>${esc(club.crest.shieldShape)}</span></div>
            <div class="factblock"><span class="factblock-key">Carga central</span><span>${esc(club.crest.centralCharge)}</span></div>
            <div class="factblock"><span class="factblock-key">Mote</span><span class="motto">“${esc(club.crest.motto)}”</span></div>
          </div>
        </div>
      </div>

      <div class="card-pair">
        <div class="card blueprint">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <div class="card-kicker">Estádio · stadium</div>
          <div class="card-title">${esc(club.stadium.name)}</div>
          <div class="stadium-grid">
            <div class="stadium-cell">
              <span class="stadium-key">Capacidade</span>
              <span class="stadium-value">${fmtInt(club.stadium.capacity)}</span>
              <span class="stadium-note${capacityOutsideProfile ? ' level-error' : ''}">${page.stadiumProfile
                ? `perfil ${fmtInt(page.stadiumProfile.min)}–${fmtInt(page.stadiumProfile.max)}`
                : 'sem perfil'}</span>
            </div>
            <div class="stadium-cell">
              <span class="stadium-key">Atmosfera</span>
              <span class="stadium-value">${esc(club.stadium.atmosphereArchetype)}</span>
              <span class="stadium-note">vantagem +${decimal(club.aiProfile.homeAdvantageModifier, 2)}</span>
            </div>
            <div class="stadium-cell">
              <span class="stadium-key">Gramado</span>
              <span class="stadium-value">${esc(club.stadium.pitchSurface)}</span>
            </div>
            <div class="stadium-cell">
              <span class="stadium-key">Clássico</span>
              <span class="stadium-value">${esc(page.derbyRivalName || '—')}</span>
            </div>
          </div>
        </div>

        <div class="card blueprint">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <div class="card-kicker">Invariantes · checks</div>
          <div class="checks">
            ${page.findings.map((finding) => `
              <div class="check">
                <span class="badge badge-${LEVEL_CLASS[finding.level]}">${LEVEL_LABEL[finding.level]}</span>
                <span style="flex: 1; min-width: 0">
                  <span class="check-label">${esc(finding.label)}</span>
                  <span class="check-detail">${esc(finding.detail)}</span>
                </span>
              </div>`).join('')}
          </div>
        </div>
      </div>

      <div class="section-head">
        <h2>Elenco</h2>
        <span class="section-note">${page.metrics.playerCount} jogadores · clique para abrir a ficha</span>
        <span class="grow"></span>
        ${state.squad.length ? `
          <select class="tpin" id="squad-sort" style="width: 190px" aria-label="Ordenar elenco">
            <option value="ovr">Ordenar por OVR</option>
            <option value="pos">Ordenar por posição</option>
            <option value="age">Ordenar por idade</option>
            <option value="value">Ordenar por valor</option>
            <option value="shirt">Ordenar por camisa</option>
          </select>` : ''}
        <button type="button" class="btn btn-primary" id="gen-open">
          ${state.squad.length ? 'Regerar elenco' : 'Gerar elenco'}
        </button>
      </div>

      <div id="generator">${state.gen ? generatorHtml() : ''}</div>

      ${state.squad.length ? squadSummaryHtml(page) + squadTableHtml() : `
        <div class="empty-squad">
          <div class="empty-squad-title">Nenhum jogador neste clube</div>
          <div class="empty-squad-body">
            O clube existe como identidade, mas sem elenco não há OVR, valor nem folha salarial — e a
            competição não consegue avaliá-lo.
          </div>
          <button type="button" class="btn btn-primary" id="gen-open-empty">Gerar elenco</button>
        </div>`}

      ${fieldGroupsHtml(state.fields.club, readPaths(club, state.fields.club), 'club')}
    </div>`;
}

/** Shown when the database has a schema but no world. The user asked for this
 *  explicitly: the tool should explain what it is and how to load data rather
 *  than render an empty shell. */
function introHtml() {
  const structures = [
    ['ClubIdentity', 'Identidade, geografia, escudo, paleta, uniformes, estádio e perfil de IA — uma página por clube, no lugar de 11 abas.'],
    ['CharacterRecord', '12 atributos em escala 1–99, com OVR, valor e salário calculados pela calibração.'],
    ['GeoNode', 'Hierarquia geográfica: Mundo › Confederação › País › Região › Cidade.'],
    ['Competition', 'Escopo, banda, promoção e rebaixamento. Nenhuma rodada é simulada aqui.'],
    ['Calibração', 'Todo número com a origem declarada: constantes, curva de idade, bandas e pesos por posição.'],
    ['Auditoria de desvio', 'A citação ao lado de cada afirmação factual sobre a âncora do clube.'],
  ];

  return `
    <div class="page intro">
      <h1>Ferramenta de Mundo</h1>
      <p>
        Editor da base de mundo do Terra Paralela. A base ainda não foi carregada neste banco —
        o schema existe, mas não há clubes.
      </p>

      <div class="intro-steps">
        <div class="intro-step">
          <span class="intro-step-num">01</span>
          <span>Exporte o JSON da base (<code>terraparalela_base_de_mundo.json</code>) ou use o documento do pacote de handoff.</span>
        </div>
        <div class="intro-step">
          <span class="intro-step-num">02</span>
          <span>Rode <code>worldbuilder import &lt;arquivo.json&gt; &lt;banco.db&gt;</code>. O import é tudo ou nada: um registro malformado rejeita o documento inteiro, com uma mensagem por registro.</span>
        </div>
        <div class="intro-step">
          <span class="intro-step-num">03</span>
          <span>Recarregue esta página. O rail à esquerda lista os clubes; clicar em um abre a ficha completa.</span>
        </div>
      </div>

      <div class="intro-structures">
        ${structures.map(([name, description]) => `
          <div class="intro-structure">
            <div class="intro-structure-name">${esc(name)}</div>
            <div class="intro-structure-desc">${esc(description)}</div>
          </div>`).join('')}
      </div>
    </div>`;
}

function errorHtml(message) {
  return `<div class="page"><div class="error-banner">${esc(message)}</div></div>`;
}

// ------------------------------------------------------------- editable fields

const SEAL_LABEL = { Authored: 'autorado', Derived: 'derivado', Sampled: 'amostrado', Calculated: 'calculado' };

/** Renders one field's control from the catalog's description of it. The control is chosen
 *  by kind, so adding a field to the catalog is enough to make it appear and be editable. */
function fieldControl(field, value, scope) {
  const path = field.path;
  const common = `class="tpin" data-path="${esc(path)}" data-scope="${scope}"`;

  if (!field.editable) {
    return `<input ${common} value="${esc(value ?? '')}" readonly tabindex="-1">`;
  }

  if (field.kind === 'Enum') {
    const options = (state.world.enums[field.enumName] || []).map((option) =>
      `<option value="${esc(option)}"${option === value ? ' selected' : ''}>${esc(option)}</option>`).join('');
    return `<select ${common}>${options}</select>`;
  }

  if (field.kind === 'Color') {
    // A picker for choosing and a text box for pasting a known hex — the data is authored
    // both ways.
    return `<span class="field-color-row">
      <input type="color" data-path="${esc(path)}" data-scope="${scope}" value="${esc(value || '#000000')}">
      <input ${common} value="${esc(value ?? '')}" spellcheck="false">
    </span>`;
  }

  if (field.kind === 'LongText') {
    return `<input ${common} value="${esc(value ?? '')}" title="${esc(value ?? '')}">`;
  }

  const type = field.kind === 'Int' || field.kind === 'Float' ? 'number' : 'text';
  const step = field.kind === 'Float' ? ' step="any"' : '';
  return `<input ${common} type="${type}"${step} value="${esc(value ?? '')}">`;
}

function fieldHtml(field, value, scope) {
  return `
    <label class="field">
      <span class="field-head">
        <span class="field-label">${esc(field.label)}</span>
        <span class="seal seal-${field.provenance.toLowerCase()}">${SEAL_LABEL[field.provenance]}</span>
      </span>
      ${fieldControl(field, value, scope)}
      <span class="field-path">${esc(field.path)}</span>
    </label>`;
}

/** The nine groups of the club sheet, rendered from the same catalog the server validates
 *  against — a form built from one list and checked against another drifts silently. */
function fieldGroupsHtml(groups, values, scope) {
  return `<div class="field-groups">${groups.map((group) => `
    <section class="field-group">
      <div class="field-group-head">
        <span class="field-group-title">${esc(group.name)}</span>
        <span class="field-group-rule"></span>
        <span class="field-group-id">${esc(group.id)}</span>
      </div>
      <div class="fields">
        ${group.fields.map((field) => fieldHtml(field, values[field.path], scope)).join('')}
      </div>
    </section>`).join('')}</div>`;
}

/** Reads every catalogued path out of a record so the form can be filled without a second
 *  request. Mirrors the server's readers; the paths are the contract. */
function readPaths(source, groups) {
  const values = {};
  for (const group of groups) {
    for (const field of group.fields) {
      values[field.path] = readPath(source, field.path);
    }
  }
  return values;
}

function readPath(source, path) {
  let current = source;
  for (const part of path.split('.')) {
    if (current === null || current === undefined) return null;
    current = current[part];
  }

  if (current === null || current === undefined) return null;
  if (Array.isArray(current)) return current.join(path === 'secondaryPositions' ? '|' : ', ');
  if (typeof current === 'boolean') return current ? '1' : '0';
  return String(current);
}

// ----------------------------------------------------------------- player modal

function playerModalHtml(page) {
  const player = page.character;
  const club = state.page.club;
  const values = readPaths(player, state.fields.character);

  const metrics = [
    ['OVR', player.overall, page.recalculated.overall],
    ['Potencial', player.potentialOverall, page.recalculated.potentialOverall],
    ['Idade', player.age, null],
    ['Valor', fmtEur(player.marketValueEur), fmtEur(page.recalculated.marketValueEur)],
    ['Salário', fmtBrl(player.salaryMonthlyBrl), fmtBrl(page.recalculated.salaryMonthlyBrl)],
    ['Altura', `${player.height} cm`, null],
  ];

  return `
    <div class="dialog" role="dialog" aria-label="Ficha do jogador">
      <button class="dialog-close" type="button" id="player-close" aria-label="Fechar">✕</button>

      <div class="dialog-head">
        <span class="player-badge" style="background: ${esc(club.palette.primary)}; color: ${inkOn(club.palette.primary, 0.55)}">
          ${player.shirtNumber}
        </span>
        <div style="flex: 1; min-width: 0">
          <div class="player-id">${esc(player.playerId)}</div>
          <h2 class="player-name">${esc(player.firstName)} ${esc(player.lastName)}</h2>
          <div class="club-tags">
            <span class="tag tag-accent">${esc(player.primaryPosition)}</span>
            <span class="tag tag-outline">${esc(squadRoleLabel(player.squadRole))}</span>
            <span class="tag tag-outline">${esc(player.phase)}</span>
            <span class="tag tag-outline">${esc(player.nationality)}</span>
            <span class="tag tag-outline">${esc(club.identity.shortName)}</span>
          </div>
        </div>
      </div>

      <div class="metrics">
        ${metrics.map(([label, stored, fresh]) => {
          const diverges = fresh !== null && String(stored) !== String(fresh);
          return `<div class="metric">
            <span class="metric-label">${esc(label)}</span>
            <span class="metric-value">${esc(stored)}</span>
            <span class="metric-note${diverges ? ' diverges' : ''}">${diverges ? `calculado ${esc(fresh)}` : 'confere'}</span>
          </div>`;
        }).join('')}
      </div>

      ${page.economyDiverges ? `
        <div class="recalc-bar">
          <span class="grow">
            Os valores gravados não batem com a calibração atual — uma re-fitagem envelheceu este
            registro. Recalcular reescreve OVR, valor e salário a partir dos atributos.
          </span>
          <button class="btn btn-secondary" type="button" id="player-recalc">Recalcular OVR, valor e salário</button>
        </div>` : ''}

      ${state.fields.character.map((group) => group.id === 'attrs'
        ? attributeGroupHtml(group, player, page.positionWeights)
        : `<section class="field-group">
             <div class="field-group-head">
               <span class="field-group-title">${esc(group.name)}</span>
               <span class="field-group-rule"></span>
               <span class="field-group-id">${esc(group.id)}</span>
             </div>
             <div class="fields">
               ${group.fields.map((field) => fieldHtml(field, values[field.path], 'character')).join('')}
             </div>
           </section>`).join('')}
    </div>`;
}

/** The twelve attributes, each with a bar for its size and the weight it carries in this
 *  player's position. The weight column is the point: it shows which attributes actually
 *  decide this player's overall and which are decoration. */
function attributeGroupHtml(group, player, weights) {
  return `
    <section class="field-group">
      <div class="field-group-head">
        <span class="field-group-title">${esc(group.name)}</span>
        <span class="field-group-rule"></span>
        <span class="field-group-id">peso na posição ${esc(player.primaryPosition)}</span>
      </div>
      <div class="fields">
        ${group.fields.map((field) => {
          const attr = field.path.slice('attrs.'.length);
          const value = player.attrs[attr];
          const weight = weights[attr] || 0;
          return `
            <label class="field">
              <span class="field-head">
                <span class="field-label">${esc(field.label)}</span>
              </span>
              <span class="attr-row">
                <input class="tpin" type="number" min="1" max="99"
                       data-path="${esc(field.path)}" data-scope="character" value="${value}">
                <span class="attr-bar"><span style="width: ${Math.max(0, Math.min(100, value))}%"></span></span>
                <span class="attr-weight${weight > 0 ? ' matters' : ''}">×${decimal(weight, 2)}</span>
              </span>
            </label>`;
        }).join('')}
      </div>
    </section>`;
}

async function openPlayer(playerId) {
  try {
    state.player = await getJson(`/api/characters/${encodeURIComponent(playerId)}`);
  } catch (error) {
    toast(`Não foi possível abrir a ficha: ${error.message}`);
    return;
  }

  const modal = document.getElementById('player-modal');
  modal.innerHTML = playerModalHtml(state.player);
  modal.hidden = false;
}

function closePlayer() {
  document.getElementById('player-modal').hidden = true;
  state.player = null;
}

// ------------------------------------------------------ export and import

const CHANGE_LABEL = { Added: 'entra', Changed: 'muda', Removed: 'sai' };

/** The tabs the diff is about, in the order the dialog lists them. */
function exchangeTabsHtml() {
  return `
    <table class="table export-table">
      <thead>
        <tr>
          <th>Aba</th>
          <th class="num" style="width: 70px">Linhas</th>
          <th class="num" style="width: 78px">Colunas</th>
          <th style="width: 150px">Observação</th>
          <th style="width: 92px"></th>
        </tr>
      </thead>
      <tbody>
        ${state.exchange.manifest.tabs.map((tab) => `
          <tr>
            <td class="export-name">${esc(tab.fileName)}</td>
            <td class="num">${fmtInt(tab.rows)}</td>
            <td class="num">${fmtInt(tab.columns)}</td>
            <td>
              ${tab.authoringOnly
                ? '<span class="badge badge-warning" title="Carrega ReferenceAnchor — nomes reais. Não vai no build.">só autoria</span>'
                : tab.importable ? '' : '<span class="export-note">só leitura</span>'}
            </td>
            <td><button class="btn btn-ghost" type="button" data-export="${esc(tab.name)}">baixar</button></td>
          </tr>`).join('')}
      </tbody>
    </table>`;
}

function exchangeDiffHtml() {
  const preview = state.exchange.preview;
  if (!preview) return '';

  // A removal is the expensive mistake, so it is counted first and coloured.
  return `
    <div class="diff">
      <div class="diff-summary">
        <span class="diff-count diff-added">${preview.added} entra${preview.added === 1 ? '' : 'm'}</span>
        <span class="diff-count diff-changed">${preview.changed} muda${preview.changed === 1 ? '' : 'm'}</span>
        <span class="diff-count diff-removed">${preview.removed} ${preview.removed === 1 ? 'sai' : 'saem'}</span>
        <span class="export-note">de ${preview.tabs.map(esc).join(', ')}</span>
      </div>

      ${preview.changes.length === 0 ? `
        <div class="diff-empty">Os arquivos descrevem exatamente a base atual — nada mudaria.</div>` : `
        <div class="diff-list tpscroll">
          ${preview.changes.map((change) => `
            <div class="diff-row diff-${change.change.toLowerCase()}">
              <span class="diff-kind">${CHANGE_LABEL[change.change]}</span>
              <span class="diff-where">
                <span class="diff-label">${esc(change.label || change.id)}</span>
                <span class="diff-id">${esc(change.tab)} · ${esc(change.id)}</span>
              </span>
              <span class="diff-fields">
                ${change.fields.map((field) => `
                  <span class="diff-field">
                    <span class="diff-col">${esc(field.column)}</span>
                    <s>${esc(field.before ?? '—')}</s> → <b>${esc(field.after ?? '—')}</b>
                  </span>`).join('')}
              </span>
            </div>`).join('')}
        </div>`}

      <div class="diff-foot">
        <button class="btn btn-primary" type="button" id="import-apply"${preview.changes.length === 0 ? ' disabled' : ''}>
          Aplicar ${preview.changes.length} alteraç${preview.changes.length === 1 ? 'ão' : 'ões'}
        </button>
        <button class="btn btn-secondary" type="button" id="import-discard">Descartar</button>
        ${preview.removed > 0 ? `
          <span class="diff-warning">
            ${preview.removed} registro(s) saem da base: uma linha ausente do arquivo é uma remoção.
          </span>` : ''}
      </div>
    </div>`;
}

function exchangeHtml() {
  const exchange = state.exchange;

  return `
    <div class="dialog" role="dialog" aria-label="Exportar e importar">
      <button class="dialog-close" type="button" id="export-close" aria-label="Fechar">✕</button>

      <div class="card-kicker">Exportar · importar</div>
      <h2 class="dialog-title">Um arquivo por aba</h2>
      <p class="dialog-intro">
        Espelha a planilha original, com separador <code>;</code> e vírgula decimal — abre no Excel
        pt-BR sem assistente. Exportar é o único gesto que zera o contador de pendências.
      </p>

      ${exchangeTabsHtml()}

      <div class="export-actions">
        <button class="btn btn-primary" type="button" id="export-all">Baixar todas as abas</button>
        <button class="btn btn-secondary" type="button" id="export-json">Exportar JSON</button>
        <span class="export-note">${esc(exchange.manifest.jsonFileName)}</span>
      </div>

      <h2 class="dialog-title" style="margin-top: var(--space-6)">Reimportar</h2>
      <p class="dialog-intro">
        Escolha os arquivos editados. Nada é gravado até você ver o diff e confirmar — importar sem
        ver o diff é como se perde trabalho.
      </p>

      <div class="export-actions">
        <input class="tpin" type="file" id="import-files" multiple accept=".csv" style="width: auto">
        <span class="export-note">${exchange.files.length
          ? esc(exchange.files.map((file) => file.tab).join(', '))
          : 'nenhum arquivo escolhido'}</span>
      </div>

      ${exchange.error ? `<div class="diff-errors tpscroll">${esc(exchange.error)}</div>` : ''}
      ${exchangeDiffHtml()}
    </div>`;
}

function renderExchange() {
  const modal = document.getElementById('export-modal');
  modal.innerHTML = state.exchange ? exchangeHtml() : '';
  modal.hidden = !state.exchange;
}

async function openExchange() {
  try {
    const manifest = await getJson('/api/export');
    state.exchange = { manifest, files: [], preview: null, error: null };
  } catch (error) {
    toast(`Não foi possível montar a exportação: ${error.message}`);
    return;
  }

  renderExchange();
}

function closeExchange() {
  state.exchange = null;
  renderExchange();
}

/** Fetches a file and hands it to the browser, then re-reads the counter the export cleared. */
async function download(url) {
  const response = await fetch(url);
  if (!response.ok) {
    toast(`Exportação falhou: ${response.status}`);
    return;
  }

  const blob = await response.blob();
  const name = /filename=("?)([^";]+)\1/.exec(response.headers.get('content-disposition') || '');

  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = name ? name[2] : 'export';
  link.click();
  URL.revokeObjectURL(link.href);

  setPending(await getJson('/api/pending'));
}

async function downloadAllTabs() {
  for (const tab of state.exchange.manifest.tabs)
    await download(`/api/export/csv/${encodeURIComponent(tab.name)}`);
}

/** Matches each chosen file to a tab by its name, then asks the server what would change. */
async function previewImport(fileList) {
  const known = new Set(state.exchange.manifest.tabs.map((tab) => tab.name));
  const files = [];

  for (const file of fileList) {
    const tab = file.name.replace(/\.csv$/i, '');
    if (!known.has(tab)) {
      state.exchange.error = `'${file.name}' não é uma aba desta base. `
        + `O nome do arquivo é o nome da aba — renomeie-o ou exporte de novo.`;
      state.exchange.preview = null;
      renderExchange();
      return;
    }

    files.push({ tab, content: await file.text() });
  }

  state.exchange.files = files;
  const result = await postJson('/api/import/preview', { files });

  state.exchange.error = result.ok ? null : result.message;
  state.exchange.preview = result.ok ? result.body : null;
  renderExchange();
}

async function applyImport() {
  const result = await postJson('/api/import/apply', { files: state.exchange.files });

  if (!result.ok) {
    state.exchange.error = result.message;
    renderExchange();
    return;
  }

  setPending(result.body.pendingEdits);
  closeExchange();

  // The base moved under every open screen, so the rail and the club page are re-read.
  await reloadWorld();
  toast(`Importado: ${result.body.added} entraram, ${result.body.changed} mudaram, `
    + `${result.body.removed} saíram.`);
}

// -------------------------------------------------------- generator wiring

/** The generator posts whole option objects rather than one field at a time, so it needs
 *  a plain POST alongside {@link sendPatch} rather than a widened version of it. */
async function postJson(url, body) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  if (response.ok) return { ok: true, body: await response.json() };

  let message = `${response.status}`;
  try {
    message = (await response.json()).error || message;
  } catch {
    // A response without a JSON body still has to say something the user can act on.
  }

  return { ok: false, message };
}

function renderGenerator() {
  document.getElementById('generator').innerHTML = state.gen ? generatorHtml() : '';
}

async function openGenerator() {
  let options;
  try {
    options = await getJson(`/api/clubs/${encodeURIComponent(state.clubId)}/squad/options`);
  } catch (error) {
    toast(`Não foi possível abrir o gerador: ${error.message}`);
    return;
  }

  state.gen = { options, preview: null, busy: false };
  await refreshPreview();
}

function closeGenerator() {
  state.gen = null;
  renderGenerator();
}

/**
 * Draws a squad without writing one. A rejected option is put back rather than left on
 * screen — the same rule the field editor follows, for the same reason: a control showing
 * a value the server refused is a control that lies about what would be generated.
 */
async function refreshPreview() {
  const previous = state.gen.preview;
  const result = await postJson(
    `/api/clubs/${encodeURIComponent(state.clubId)}/squad/preview`,
    state.gen.options);

  if (!result.ok) {
    toast(result.message);
    // Nothing was ever previewed: there is no panel to fall back to.
    if (previous) state.gen.options = { ...previous.options };
    else state.gen = null;
    renderGenerator();
    return;
  }

  state.gen.preview = result.body;
  // The server echoes the options it actually used, so the controls show those and not
  // whatever was typed at them. A copy, not the echo itself: the controls mutate this
  // object, and the preview's copy is what a rejected change is restored from.
  state.gen.options = { ...result.body.options };
  renderGenerator();
}

function changeGenOption(control) {
  state.gen.options[control.dataset.gen] =
    control.type === 'number' ? Number(control.value) : control.value;
  refreshPreview();
}

/** Writes the previewed squad. Destructive, and the confirmation says what is lost. */
async function writeSquad() {
  const preview = state.gen.preview;

  if (preview.existingPlayers > 0) {
    const anchored = preview.existingAnchored > 0
      ? `\n\n${preview.existingAnchored} ${preview.existingAnchored === 1
          ? 'deles está ancorado' : 'deles estão ancorados'} em pessoas reais. `
        + 'Essa pesquisa se perde e não pode ser recuperada.'
      : '';

    if (!confirm(`Substituir os ${preview.existingPlayers} jogadores deste clube `
      + `por ${preview.squad.length} gerados?${anchored}`)) return;
  }

  state.gen.busy = true;
  renderGenerator();

  const result = await postJson(
    `/api/clubs/${encodeURIComponent(state.clubId)}/squad/generate`,
    state.gen.options);

  if (!result.ok) {
    state.gen.busy = false;
    renderGenerator();
    toast(result.message);
    return;
  }

  setPending(result.body.pendingEdits);

  const { written, replaced } = result.body;
  await selectClub(state.clubId);
  toast(replaced > 0
    ? `${written} jogadores gravados no lugar dos ${replaced} anteriores.`
    : `${written} jogadores gravados.`);
}

// ------------------------------------------------------------------ wiring

async function selectClub(clubId) {
  state.clubId = clubId;
  // A preview belongs to the club it was drawn for, and to the squad it would replace.
  state.gen = null;
  renderRail();

  const content = document.getElementById('content');
  try {
    const [page, squad] = await Promise.all([
      getJson(`/api/clubs/${encodeURIComponent(clubId)}`),
      getJson(`/api/clubs/${encodeURIComponent(clubId)}/squad`),
    ]);
    state.page = page;
    state.squad = squad;
    content.innerHTML = clubPageHtml(page);
    content.scrollTop = 0;
  } catch (error) {
    content.innerHTML = errorHtml(`Não foi possível carregar o clube: ${error.message}`);
  }
}

function bindEvents() {
  document.getElementById('rail-list').addEventListener('click', (event) => {
    const row = event.target.closest('[data-club]');
    if (row) selectClub(row.dataset.club);
  });

  document.getElementById('filter-text').addEventListener('input', (event) => {
    state.filters.text = event.target.value;
    renderRail();
  });

  document.getElementById('filter-band').addEventListener('change', (event) => {
    state.filters.band = event.target.value;
    renderRail();
  });

  document.getElementById('filter-city').addEventListener('change', (event) => {
    state.filters.city = event.target.value;
    renderRail();
  });

  const content = document.getElementById('content');

  // The sort select and every field control are re-rendered with the page, so the handlers
  // live on the container rather than the controls.
  content.addEventListener('change', (event) => {
    if (event.target.id === 'squad-sort') {
      state.squadSort = event.target.value;
      document.querySelector('.squad-table').outerHTML = squadTableHtml();
      return;
    }

    // A generator control changes what would be generated, not what is stored: it redraws
    // the preview and writes nothing.
    if (event.target.dataset.gen) {
      changeGenOption(event.target);
      return;
    }

    // Commit on change, never on keystroke: an edit is a decision, not every character of
    // typing one (design README, "Interactions & Behavior").
    if (event.target.dataset.path) commitField(event.target);
  });

  content.addEventListener('click', (event) => {
    const id = event.target.closest('button')?.id;

    if (id === 'gen-open' || id === 'gen-open-empty') {
      // Clicking the button again while the panel is open closes it: one control, one state.
      if (state.gen) closeGenerator();
      else openGenerator();
      return;
    }
    if (id === 'gen-cancel') {
      closeGenerator();
      return;
    }
    if (id === 'gen-reseed') {
      state.gen.options.seed = Math.floor(Math.random() * 1000000) + 1;
      refreshPreview();
      return;
    }
    if (id === 'gen-write') {
      writeSquad();
      return;
    }

    const row = event.target.closest('[data-player]');
    if (row) openPlayer(row.dataset.player);
  });

  const modal = document.getElementById('player-modal');

  modal.addEventListener('change', (event) => {
    if (event.target.dataset.path) commitField(event.target);
  });

  modal.addEventListener('click', (event) => {
    // Clicking the backdrop closes; clicking inside the dialog does not.
    if (event.target === modal || event.target.id === 'player-close') {
      closePlayer();
      return;
    }
    if (event.target.id === 'player-recalc') recalculatePlayer();
  });

  document.getElementById('export-open').addEventListener('click', openExchange);

  const exchange = document.getElementById('export-modal');

  exchange.addEventListener('change', (event) => {
    if (event.target.id === 'import-files') previewImport(event.target.files);
  });

  exchange.addEventListener('click', (event) => {
    if (event.target === exchange || event.target.id === 'export-close') {
      closeExchange();
      return;
    }

    const button = event.target.closest('button');
    if (!button) return;

    if (button.dataset.export) download(`/api/export/csv/${encodeURIComponent(button.dataset.export)}`);
    else if (button.id === 'export-all') downloadAllTabs();
    else if (button.id === 'export-json') download('/api/export/json');
    else if (button.id === 'import-apply') applyImport();
    else if (button.id === 'import-discard') {
      state.exchange.files = [];
      state.exchange.preview = null;
      state.exchange.error = null;
      renderExchange();
    }
  });

  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    if (!modal.hidden) closePlayer();
    else if (!exchange.hidden) closeExchange();
  });
}

/**
 * Writes one field and reconciles the screen with what the server actually stored.
 *
 * A rejected value is put back rather than left on screen: showing a value the database
 * does not hold is how a user comes to trust a number that was never saved.
 */
async function commitField(control) {
  const path = control.dataset.path;
  const scope = control.dataset.scope;
  const value = control.value;

  const isClub = scope === 'club';
  const url = isClub
    ? `/api/clubs/${encodeURIComponent(state.clubId)}`
    : `/api/characters/${encodeURIComponent(state.player.character.playerId)}`;
  const version = isClub ? state.page.version : state.player.version;

  control.classList.add('saving');
  const result = await sendPatch(url, path, value, version);
  control.classList.remove('saving');

  if (!result.ok) {
    control.classList.add('rejected');
    toast(result.status === 409
      ? 'Este registro mudou desde que a página foi aberta. Recarregando para não sobrescrever a outra edição.'
      : result.message);

    // A conflict means our copy is stale, so re-read rather than letting the user keep
    // editing against a version the server has already moved past.
    if (result.status === 409) {
      if (isClub) await selectClub(state.clubId);
      else await openPlayer(state.player.character.playerId);
    }
    return;
  }

  // Re-read so every derived value the edit moved (ΔE, the overall, the invariant badge)
  // is the server's, not a guess made here.
  const playerId = state.player?.character?.playerId;
  await selectClub(state.clubId);
  if (!isClub && playerId) await openPlayer(playerId);
}

async function recalculatePlayer() {
  const playerId = state.player.character.playerId;
  const result = await sendPatch(
    `/api/characters/${encodeURIComponent(playerId)}/recalculate`,
    'economy',
    null,
    state.player.version,
    'POST');

  if (!result.ok) {
    toast(result.message);
    return;
  }

  await selectClub(state.clubId);
  await openPlayer(playerId);
}

/** Re-reads the rail and the open club after something changed the base wholesale. */
async function reloadWorld() {
  state.clubs = await getJson('/api/clubs');
  renderFilters();
  renderRail();

  const stillThere = state.clubs.some((club) => club.clubId === state.clubId);
  await selectClub(stillThere ? state.clubId : state.clubs[0]?.clubId);
}

async function init() {
  const loading = document.getElementById('loading');
  const content = document.getElementById('content');

  try {
    const [world, clubs, fields, pending] = await Promise.all([
      getJson('/api/world'),
      getJson('/api/clubs'),
      getJson('/api/fields'),
      getJson('/api/pending'),
    ]);
    state.world = world;
    state.clubs = clubs;
    state.fields = fields;

    document.getElementById('schema-version').textContent = world.schemaVersion;
    setPending(pending);

    if (!world.loaded) {
      content.innerHTML = introHtml();
      return;
    }

    renderFilters();
    renderRail();
    bindEvents();
    await selectClub(clubs[0].clubId);
  } catch (error) {
    content.innerHTML = errorHtml(`A base de mundo não respondeu: ${error.message}`);
  } finally {
    loading.hidden = true;
  }
}

init();
