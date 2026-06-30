(function () {
  const form = document.getElementById('endpoint-form');
  if (!form) return;

  const table = document.getElementById('endpoint-table');
  const status = document.getElementById('result-status');
  const urlLabel = document.getElementById('result-url');
  const jsonBox = document.getElementById('result-json');
  const counter = document.getElementById('demo-counter');
  const gate = document.getElementById('register-gate');
  const localKey = 'leadbase.demoUses';
  const maxDemoUses = 2;

  function getDemoUses() {
    return Number.parseInt(localStorage.getItem(localKey) || '0', 10) || 0;
  }

  function setDemoUses(value) {
    localStorage.setItem(localKey, String(value));
    updateCounter();
  }

  function updateCounter() {
    const remaining = Math.max(0, maxDemoUses - getDemoUses());
    counter.textContent = remaining > 0
      ? `Demo: pozostały ${remaining} darmowe zapytania bez konta.`
      : 'Demo wykorzystane. Wpisz API key albo utwórz konto.';
    gate.hidden = remaining > 0;
  }

  function selectedColumns(data) {
    return data.getAll('columns').filter(Boolean);
  }

  function queryFromForm(data, includePage) {
    const params = new URLSearchParams();
    const columns = selectedColumns(data).join(',');
    if (columns) params.set('columns', columns);
    ['name', 'city', 'mainPkdCode'].forEach((name) => {
      const value = String(data.get(name) || '').trim();
      if (value) params.set(name, value);
    });
    if (includePage) {
      params.set('page', '1');
      params.set('pageSize', '10');
    }
    return params;
  }

  function renderTable(items) {
    const rows = Array.isArray(items) ? items : [];
    const columns = rows.length ? Object.keys(rows[0]) : selectedColumns(new FormData(form));
    table.querySelector('thead').innerHTML = `<tr>${columns.map((column) => `<th>${escapeHtml(column)}</th>`).join('')}</tr>`;
    table.querySelector('tbody').innerHTML = rows.map((row) => (
      `<tr>${columns.map((column) => `<td>${formatCell(row[column])}</td>`).join('')}</tr>`
    )).join('') || `<tr><td colspan="${columns.length || 1}">Brak wyników dla podanych filtrów.</td></tr>`;
  }

  function formatCell(value) {
    if (value === null || value === undefined || value === '') return '<span class="muted-cell">brak</span>';
    const text = Array.isArray(value) ? value.join(', ') : String(value);
    if (text.toLowerCase() === 'aktywny') return '<span class="status ok">Aktywny</span>';
    if (text.toLowerCase() === 'zawieszony') return '<span class="status warn">Zawieszony</span>';
    return escapeHtml(text);
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#039;');
  }

  async function runQuery(event) {
    event.preventDefault();
    const data = new FormData(form);
    const apiKey = String(data.get('apiKey') || '').trim();
    const isAuthenticated = apiKey.length > 0;

    if (!isAuthenticated && getDemoUses() >= maxDemoUses) {
      status.textContent = 'Wymagana rejestracja';
      jsonBox.textContent = 'Limit 2 zapytań demo został wykorzystany. Utwórz konto lub wpisz API key.';
      gate.hidden = false;
      return;
    }

    const params = queryFromForm(data, isAuthenticated);
    const endpoint = isAuthenticated ? `/companies?${params}` : `/demo/companies?${params}`;
    urlLabel.textContent = `GET ${endpoint}`;
    status.textContent = 'Wysyłanie zapytania...';

    const headers = isAuthenticated ? { 'X-Api-Key': apiKey } : {};
    const response = await fetch(endpoint, { headers });
    const payload = await response.json().catch(() => ({ error: 'Nie udało się odczytać odpowiedzi JSON.' }));

    if (!response.ok) {
      status.textContent = payload.registrationRequired ? 'Wymagana rejestracja' : `Błąd ${response.status}`;
      jsonBox.textContent = JSON.stringify(payload, null, 2);
      if (payload.registrationRequired) gate.hidden = false;
      return;
    }

    if (!isAuthenticated) {
      setDemoUses(Math.min(maxDemoUses, getDemoUses() + 1));
    }

    status.textContent = isAuthenticated
      ? `Pobrano ${payload.returnedRows ?? payload.items?.length ?? 0} rekordów. Tokeny: ${payload.tokenCost ?? '-'}`
      : `Demo OK. Pozostało ${payload.demoUsesRemaining ?? Math.max(0, maxDemoUses - getDemoUses())} prób.`;
    renderTable(payload.items || []);
    jsonBox.textContent = JSON.stringify(payload, null, 2);
  }

  form.addEventListener('submit', runQuery);
  renderTable([
    { nip: '7312045678', name: 'FIRMA ABC JAN KOWALSKI', city: 'Warszawa', email: 'biuro@firmaabc.pl', www: 'firmaabc.pl', pkd: '62.01.Z', status: 'Aktywny' },
    { nip: '9491832736', name: 'PV SOLUTIONS SPOLKA Z O.O.', city: 'Krakow', email: 'kontakt@pvsolutions.pl', www: 'pvsolutions.pl', pkd: '43.21.Z', status: 'Aktywny' }
  ]);
  updateCounter();
})();

