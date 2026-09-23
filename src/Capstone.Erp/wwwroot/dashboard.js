'use strict';
const $ = id => document.getElementById(id);
let key = '', selected = null, busy = false, timer = null, generation = 0;
const exchanges = [];
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const descriptions = {
  Pending: ['Queued for delivery.', 'ERP has persisted the order and outbox entry. Warehouse acceptance has not yet been confirmed.', 'warn'],
  Acknowledged: ['Warehouse acceptance confirmed.', 'ERP has persisted a valid WMS receipt for this order. Verify the warehouse records below or challenge the receiver with a duplicate.', 'good'],
  Rejected: ['Delivery was explicitly rejected.', 'The receiver returned a business rejection. This is the persisted delivery outcome, not a dashboard rule.', 'bad'],
  RecoveryRequired: ['Delivery outcome is uncertain.', 'ERP could not confirm a valid acceptance. WMS may still have committed the order. Inspect its evidence before deciding what to do; automatic recovery is outside this milestone.', 'warn']
};
function notice(message, error = false) { $('notice').textContent = message; $('notice').className = `notice${error ? ' error' : ''}`; }
function badge(id, text, tone = 'neutral') { $(id).textContent = text; $(id).className = `badge ${tone}`; }
function controls() {
  for (const id of ['submit', 'lookup']) $(id).disabled = busy || !key;
  $('refresh').disabled = busy || !key || !selected;
  for (const id of ['duplicate', 'conflict']) $(id).disabled = busy || !key || selected?.status !== 'Acknowledged';
  $('new-order').disabled = busy;
  for (const id of ['order-id', 'sku', 'quantity', 'correlation-id', 'connect', 'api-key', 'disconnect']) $(id).disabled = busy;
}
function warehouseReset(message = 'Counts are retrieved from WMS, not calculated from browser activity.') {
  $('order-count').textContent = '—'; $('receipt-count').textContent = '—';
  badge('invariant', 'Not checked'); $('warehouse-description').textContent = message;
}
function resetSelection() {
  clearTimeout(timer); generation++; selected = null;
  $('decision-emblem').className = 'decision-emblem idle';
  badge('state-badge', 'No order'); $('decision-title').textContent = 'Ready when you are.';
  $('decision-description').textContent = 'Submit a valid order or look up an existing order to view its persisted delivery state.';
  for (const id of ['selected-order', 'checked-at', 'receipt-id']) $(id).textContent = '—';
  for (const id of ['flow-erp', 'flow-delivery', 'flow-wms']) $(id).className = '';
  $('challenge-result').textContent = 'No receiver check performed.'; warehouseReset(); controls();
}
function newOrder() {
  resetSelection(); $('order-id').value = crypto.randomUUID(); $('correlation-id').value = crypto.randomUUID();
  $('sku').value = 'DEMO-PALLET-01'; $('quantity').value = 5;
  notice(key ? 'New identity prepared. Submit to begin a fresh delivery.' : 'Connect to submit an order or look up its status.');
}
function showExchange(entry) {
  $('request-label').textContent = `${entry.method} ${entry.path}`;
  $('request-json').textContent = entry.body ? JSON.stringify(entry.body, null, 2) : '(No request body)';
  $('response-label').textContent = entry.code ? `HTTP ${entry.code} · ${entry.ms} ms` : 'NO HTTP RESPONSE';
  $('response-json').textContent = entry.raw || '(Empty response body)';
  badge('exchange-status', entry.code ? `HTTP ${entry.code}` : 'Network error', entry.code >= 200 && entry.code < 300 ? 'good' : 'bad');
}
function history(entry, inspect) {
  exchanges.unshift(entry); exchanges.splice(20); $('history').replaceChildren();
  for (const item of exchanges) {
    const row = document.createElement('tr');
    for (const text of [item.time, '', item.code || '—', `${item.ms} ms`]) { const cell = document.createElement('td'); cell.textContent = text; row.append(cell); }
    const button = document.createElement('button'); button.type = 'button'; button.textContent = `${item.method} ${item.path}`;
    button.addEventListener('click', () => showExchange(item)); row.children[1].append(button); $('history').append(row);
  }
  if (inspect) showExchange(entry);
}
async function api(method, path, body, inspect = true) {
  const started = performance.now();
  let code = 0, raw = '', data = null;
  try {
    const response = await fetch(path, { method, headers: { 'X-Api-Key': key, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(15000), cache: 'no-store', credentials: 'omit' });
    code = response.status; raw = await response.text();
    try { data = JSON.parse(raw); } catch { /* Preserve non-JSON responses as received. */ }
  } catch { raw = 'No response received. The outcome of a submitted request may be unknown. Look up the same order ID before submitting a new identity.'; }
  const entry = { method, path, body, code, raw, data, ms: Math.round(performance.now() - started), time: new Date().toLocaleTimeString() };
  history(entry, inspect);
  if (code === 401) { key = ''; clearTimeout(timer); $('api-key').hidden = false; $('connect').hidden = false; $('disconnect').hidden = true; $('connection-title').textContent = 'Reconnect your local session'; }
  return entry;
}
function problem(result) {
  const errors = {
    quantity_out_of_range: 'Quantity must be an integer between 1 and 100,000.',
    invalid_sku: 'The backend rejected the SKU format.',
    immutable_order_conflict: 'This order ID already exists with different business content. The original order is unchanged.',
    database_unavailable: 'The service cannot access its database. Check availability and look up the same order ID before trying again.',
    missing_delivery_identity: 'This source has no configured WMS delivery identity.',
    wms_unreachable_outcome_unknown: 'WMS could not be reached. No acceptance or rejection can be inferred.',
    wms_timeout_outcome_unknown: 'WMS did not respond in time. The request may still have been accepted.'
  };
  if (!result.code) return result.raw;
  if (result.code === 401) return 'API key not accepted. Reconnect with the ERP key.';
  if (result.code === 404) return 'No order was found for this identity and authenticated source.';
  return errors[result.data?.error] || `Request returned HTTP ${result.code}. Inspect the response below.`;
}
function renderStatus(status) {
  selected = status;
  $('decision-emblem').className = `decision-emblem ${status.status === 'Acknowledged' ? 'confirmed' : status.status === 'Pending' ? 'waiting' : 'attention'}`;
  const [title, description, tone] = descriptions[status.status] || ['Unrecognized delivery state.', 'Inspect the backend response below.', 'neutral'];
  badge('state-badge', status.status, tone); $('decision-title').textContent = title; $('decision-description').textContent = description;
  $('selected-order').textContent = status.order.orderId; $('checked-at').textContent = new Date().toLocaleTimeString();
  $('receipt-id').textContent = status.receipt?.receiptId || 'Not confirmed';
  $('flow-erp').className = 'done'; $('flow-delivery').className = status.status === 'Acknowledged' ? 'done' : status.status === 'Pending' ? 'pending' : 'attention';
  $('flow-wms').className = status.status === 'Acknowledged' ? 'done' : ''; controls();
}
async function warehouse(id) {
  warehouseReset('Checking WMS…');
  const result = await api('GET', `/api/verification/${id}`, null, false);
  if (result.code !== 200) { warehouseReset(result.code === 404 ? 'WMS reports no accepted order for this source and ID at the time of this check.' : problem(result)); badge('invariant', 'Unavailable', 'warn'); return; }
  $('order-count').textContent = result.data.orderCount; $('receipt-count').textContent = result.data.receiptCount;
  const matches = result.data.orderCount === 1 && result.data.receiptCount === 1 && (!selected?.receipt || selected.receipt.receiptId === result.data.receipt.receiptId);
  badge('invariant', matches ? '1 order · 1 receipt' : 'Review evidence', matches ? 'good' : 'warn');
  $('warehouse-description').textContent = `WMS accepted ${result.data.order.quantity} × ${result.data.order.sku}. Checked ${new Date().toLocaleTimeString()}. Receipt ${result.data.receipt.receiptId}.`;
}
async function refresh(id, inspect = false) {
  const result = await api('GET', `/api/orders/${id}`, null, inspect);
  if (result.code !== 200) { notice(problem(result), true); $('checked-at').textContent = 'Refresh failed — state above is last known'; warehouseReset('Latest evidence could not be refreshed.'); return false; }
  renderStatus(result.data);
  if (result.data.status !== 'Pending') await warehouse(id);
  return true;
}
function poll(id, started = Date.now(), epoch = generation) {
  clearTimeout(timer);
  if (Date.now() - started >= 30000) { notice('Order is still pending after 30 seconds. Use Refresh evidence to check again.'); return; }
  timer = setTimeout(async () => {
    if (epoch !== generation || !key || selected?.order.orderId !== id) return;
    if (busy) { poll(id, started, epoch); return; }
    busy = true; controls();
    try { const ok = await refresh(id); if (ok && selected.status === 'Pending') poll(id, started, epoch); }
    catch { notice('Could not interpret the service response. Inspect the exchange below.', true); }
    finally { busy = false; controls(); }
  }, 1500);
}
async function run(action) {
  if (busy) return; busy = true; clearTimeout(timer); controls();
  try { await action(); } catch { notice('Could not complete the operation. Inspect the request and response, then look up the same order before retrying.', true); }
  finally { busy = false; controls(); }
}
function input() {
  const order = { version: 1, orderId: $('order-id').value.trim(), sku: $('sku').value, quantity: Number($('quantity').value), correlationId: $('correlation-id').value.trim() };
  if (!guid.test(order.orderId) || !guid.test(order.correlationId)) throw new Error('Use a UUID for both order ID and correlation ID.');
  if (!Number.isSafeInteger(order.quantity)) throw new Error('Quantity must be a whole number.');
  return order;
}
$('connection-form').addEventListener('submit', event => { event.preventDefault(); run(async () => {
  key = $('api-key').value.trim(); $('api-key').value = '';
  const result = await api('GET', `/api/orders/${crypto.randomUUID()}`, null, false);
  if (result.code === 404 || result.code === 200) {
    $('connection-title').textContent = 'Authenticated local session'; $('connection-hint').textContent = 'API key held in memory only. Reloading or disconnecting clears it. Clear your clipboard after pasting.';
    $('api-key').hidden = true; $('connect').hidden = true; $('disconnect').hidden = false; notice('Connected. Submit a new order or look up an existing identity.');
  } else { key = ''; notice(problem(result), true); }
}); });
$('disconnect').addEventListener('click', () => {
  key = ''; resetSelection(); $('api-key').hidden = false; $('connect').hidden = false; $('disconnect').hidden = true;
  $('connection-title').textContent = 'Connect your local session'; notice('Disconnected. No API key is stored by the dashboard.'); controls();
});
$('new-order').addEventListener('click', newOrder);
$('order-form').addEventListener('submit', event => { event.preventDefault(); let order; try { order = input(); } catch (error) { notice(error.message, true); return; }
  run(async () => {
    resetSelection(); const result = await api('POST', '/api/orders', order);
    if (result.code !== 202) { notice(problem(result), true); return; }
    renderStatus(result.data); notice('ERP accepted the request. Tracking the persisted delivery state…');
    if (result.data.status === 'Pending') poll(order.orderId); else await warehouse(order.orderId);
  });
});
$('lookup').addEventListener('click', () => {
  const id = $('order-id').value.trim(); if (!guid.test(id)) { notice('Enter a valid order UUID to look up.', true); return; }
  run(async () => { resetSelection(); if (await refresh(id, true)) { notice('Loaded persisted order status.'); if (selected.status === 'Pending') poll(id); } });
});
$('refresh').addEventListener('click', () => run(async () => { if (await refresh(selected.order.orderId, true)) { notice('Evidence refreshed from the services.'); if (selected.status === 'Pending') poll(selected.order.orderId); } }));
async function challenge(conflict) {
  if (selected?.status !== 'Acknowledged') return;
  await run(async () => {
    const order = { ...selected.order, correlationId: crypto.randomUUID() };
    if (conflict) order.quantity = order.quantity === 100000 ? 99999 : order.quantity + 1;
    const result = await api('POST', '/api/verification/acceptances', order);
    const pass = conflict ? result.code === 409 && result.data?.error === 'immutable_order_conflict' : result.code === 200 && result.data?.receiptId === selected.receipt?.receiptId;
    $('challenge-result').textContent = pass ? (conflict ? 'PASS · WMS rejected the changed quantity with HTTP 409. Inspect the persisted quantity and counts alongside.' : 'PASS · WMS returned HTTP 200 and the original receipt. Inspect the persisted counts alongside.') : `Check did not pass: ${problem(result)}`;
    notice(pass ? 'Receiver check completed. The exchange below is the actual WMS response.' : problem(result), !pass);
    await warehouse(selected.order.orderId);
  });
}
$('duplicate').addEventListener('click', () => challenge(false)); $('conflict').addEventListener('click', () => challenge(true));
async function health() {
  try { const response = await fetch('/health', { signal: AbortSignal.timeout(5000), cache: 'no-store' }); const ready = response.ok && (await response.json()).status === 'ready'; $('health').textContent = ready ? '● ERP + SQL ready' : 'ERP database unavailable'; $('health').className = ready ? 'health ready' : 'health'; }
  catch { $('health').textContent = 'ERP unavailable'; $('health').className = 'health'; }
}
newOrder(); health(); setInterval(health, 15000);

// SQL-backed batch sender. Polling is observational; it never starts or resumes a run.
let autoId = null, autoSnapshot = null, autoBusy = false, autoPolling = false;
function autoControls() {
  $('auto-arm').disabled = !key || autoBusy;
  $('auto-history').disabled = !key || autoBusy;
  $('auto-run').disabled = !key || autoBusy || autoSnapshot?.state !== 'Armed';
  $('auto-stop').disabled = !key || autoBusy || !['Armed','Running','Draining'].includes(autoSnapshot?.state);
}
function tileTone(m) {
  if (m.state === 'Queued') return 'queued';
  if (m.state === 'Cancelled') return 'cancelled';
  if (['Sending','AwaitingDelivery'].includes(m.state)) return 'sending';
  if (m.state === 'Acknowledged' && m.matched !== false) return 'accepted';
  if (m.matched === true) return 'expected';
  return 'unexpected';
}
async function autoRequest(method, path, body) {
  const response = await fetch(path, {method,headers:{'X-Api-Key':key,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(30000),cache:'no-store',credentials:'omit'});
  const text = await response.text(); let data; try { data = JSON.parse(text); } catch { data = null; }
  if (!response.ok) throw new Error(data?.error || `HTTP ${response.status}. Reconnect or check the service.`);
  return data;
}
async function autoLoad() {
  if (!key || !autoId || autoPolling) return;
  const requestedId=autoId;autoPolling=true;
  try {
    const r=await autoRequest('GET',`/api/automation/${requestedId}`);if(requestedId!==autoId)return;autoSnapshot=r;
    badge('auto-state',r.state,r.state==='Completed'?'good':r.state==='Armed'?'neutral':'warn');
    const messages=r.messages, terminal=messages.filter(m=>!['Queued','Sending','AwaitingDelivery'].includes(m.state));
    $('auto-sent').textContent=`${messages.filter(m=>m.sentAt).length} / ${r.messageCount}`;
    $('auto-analyzed').textContent=messages.filter(m=>m.analyzedAt).length;
    $('auto-accepted').textContent=messages.filter(m=>m.state==='Acknowledged').length;
    $('auto-expected').textContent=messages.filter(m=>m.matched===true && m.state!=='Acknowledged').length;
    $('auto-unexpected').textContent=messages.filter(m=>tileTone(m)==='unexpected').length;
    $('auto-progress').max=r.messageCount;$('auto-progress').value=terminal.length;
    $('auto-notice').textContent=`Run ${r.runId} · seed ${r.seed} · ${terminal.length}/${r.messageCount} terminal (including cancelled) · updated ${new Date().toLocaleTimeString()}.`;
    // Preserve focused tiles across refreshes.
    for(const m of messages){let tile=document.getElementById(`message-${m.sequence}`);if(!tile){tile=document.createElement('button');tile.id=`message-${m.sequence}`;tile.type='button';tile.addEventListener('click',()=>autoDetail(m.sequence));$('message-map').append(tile);}tile.className=tileTone(m);tile.title=`#${m.sequence} · ${m.scenario} · ${m.state}`;tile.setAttribute('aria-label',tile.title);}
  }catch(error){$('auto-notice').textContent=`Live refresh failed: ${error.message} Displayed values may be stale.`;}finally{autoPolling=false;autoControls();}
}
async function autoDetail(sequence){try{const r=await autoRequest('GET',`/api/automation/${autoId}/messages/${sequence}`);$('auto-detail-label').textContent=`Message ${sequence} · ${r.scenario} · ${r.state} · timestamps are UTC`;$('auto-detail-json').textContent=JSON.stringify(r,null,2);}catch(error){$('auto-notice').textContent=error.message;}}
async function autoAction(action){if(autoBusy)return;autoBusy=true;autoControls();try{await action();}catch(error){$('auto-notice').textContent=error.message;}finally{autoBusy=false;autoControls();}}
$('auto-arm').addEventListener('click',()=>autoAction(async()=>{
 const options={count:Number($('auto-count').value),errorPercent:Number($('auto-errors').value),intervalMs:Number($('auto-interval').value),seed:Number($('auto-seed').value)};
 if(!Object.values(options).every(Number.isInteger))throw new Error('Use whole numbers for all settings.');
 const r=await autoRequest('POST','/api/automation/arm',options);autoId=r.runId;autoSnapshot=null;$('message-map').replaceChildren();await autoLoad();
}));
$('auto-run').addEventListener('click',()=>autoAction(async()=>{await autoRequest('POST',`/api/automation/${autoId}/run`);await autoLoad();}));
$('auto-stop').addEventListener('click',()=>autoAction(async()=>{await autoRequest('POST',`/api/automation/${autoId}/stop`);await autoLoad();}));
$('auto-history').addEventListener('click',()=>autoAction(async()=>{const runs=await autoRequest('GET','/api/automation');$('auto-runs').replaceChildren(new Option('Select a recorded run',''));for(const r of runs)$('auto-runs').append(new Option(`${r.createdAt} UTC · ${r.messageCount} · ${r.state}`,r.runId));$('auto-notice').textContent=`${runs.length} recent runs loaded.`;}));
$('auto-runs').addEventListener('change',()=>{if(!$('auto-runs').value)return;autoId=$('auto-runs').value;autoSnapshot=null;$('message-map').replaceChildren();autoLoad();});
setInterval(()=>{autoControls();if(key&&autoId&&!autoBusy)autoLoad();},2000);autoControls();
